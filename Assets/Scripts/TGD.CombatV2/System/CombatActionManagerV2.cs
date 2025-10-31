using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class CombatActionManagerV2 : MonoBehaviour
    {
        static CombatActionManagerV2 s_gateHubInstance;

        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
        public float pickPlaneY = 0.01f;
        public float rayMaxDistance = 2000f;
        public HexBoardTiler tiler;

        [Header("Chain Cursor Colors")]
        public Color chainValidColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color chainInvalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Turn Runtime")]
        public TurnManagerV2 turnManager;
        public HexBoardTestDriver unitDriver;

        public event Action<Unit> ChainFocusChanged;

        [SerializeField]
        [Tooltip("Only one CAM should register phase/turn gates in a scene.")]
        bool registerAsGateHub = true;

        [Header("Tools (drag any components that implement IActionToolV2)")]
        public List<MonoBehaviour> tools = new();

        [Header("Keybinds")]
        public KeyCode keyMoveAim = KeyCode.V;
        public KeyCode keyAttackAim = KeyCode.A;

        [Header("Rulebook")]
        public ActionRulebook rulebook;

        [System.Serializable]
        public struct ChainKeybind
        {
            public string id;
            public KeyCode key;
        }

        [Header("Chain Keybinds")]
        public List<ChainKeybind> chainKeybinds = new();

        [Header("Chain Popup UI")]
        public MonoBehaviour chainPopupUiBehaviour;

        public bool debugLog = true;
        public bool quietInternalToolLogs = true;

        [Header("Phase Start Free Chain")]
        public bool skipPhaseStartFreeChain = false;

        [Header("Sustained Bonus Turns")]
        [Tooltip("Automatically finish bonus idle turns when their allotted time is consumed.")]
        public bool autoEndBonusTurns = true;

        enum Phase
        {
            Idle,
            Aiming,
            Executing
        }

        [SerializeField]
        Phase _phase = Phase.Idle;
        public bool IsExecuting => _phase == Phase.Executing;

        readonly Dictionary<string, List<IActionToolV2>> _toolsById = new();
        IActionToolV2 _activeTool;
        Unit _currentUnit;
        Hex? _hover;
        TargetSelectionCursor _chainCursor;
        IHexHighlighter _aimHighlighter;
        IHexHighlighter _chainHighlighter;
        int _inputSuppressionDepth;
        bool _pendingEndTurn;
        Unit _pendingEndTurnUnit;
        int _endTurnGuardDepth;
        int _queuedActionsPending;
        int _chainWindowDepth;
        bool _ownsGateHub;
        int _bonusPlanDepth = -1;

        bool IsInputSuppressed => _inputSuppressionDepth > 0;
        bool IsAnyChainWindowActive => _chainWindowDepth > 0;

        void PushInputSuppression()
        {
            _inputSuppressionDepth++;
        }

        void PopInputSuppression()
        {
            if (_inputSuppressionDepth > 0)
                _inputSuppressionDepth--;
        }

        struct PlannedCost
        {
            public int moveSecs;
            public int atkSecs;
            public int moveEnergy;
            public int atkEnergy;
            public bool valid;

            public int TotalSeconds => Mathf.Max(0, moveSecs + atkSecs);
            public int TotalEnergy => Mathf.Max(0, moveEnergy + atkEnergy);
        }

        struct PreDeduct
        {
            public int secs;
            public int energyMove;
            public int energyAtk;
            public bool valid;
            public int bonusSecs;
        }

        struct ActionPlan
        {
            public string kind;
            public Hex target;
            public PlannedCost cost;
        }

        struct ChainOption
        {
            public IActionToolV2 tool;
            public KeyCode key;
            public int secs;
            public int energy;
            public ActionKind kind;
            public Unit owner;
            public ITurnBudget budget;
            public IResourcePool resources;
            public ICooldownSink cooldowns;
        }

        struct ChainQueuedAction
        {
            public IActionToolV2 tool;
            public Unit owner;
            public ActionPlan plan;
            public ITurnBudget budget;
            public IResourcePool resources;
        }

        struct ChainQueueOutcome
        {
            public bool queued;
            public bool cancel;
            public IActionToolV2 tool;
        }

        struct ExecReportData
        {
            public bool valid;
            public int plannedSecsMove;
            public int plannedSecsAtk;
            public int refundedSecsMove;
            public int refundedSecsAtk;
            public int energyMoveNet;
            public int energyAtkNet;
            public bool freeMoveApplied;
            public string refundTag;
            public bool attackExecuted;

            public int TotalPlanned => Mathf.Max(0, plannedSecsMove + plannedSecsAtk);
            public int TotalRefunded => Mathf.Max(0, refundedSecsMove + refundedSecsAtk);
            public int NetSeconds => Mathf.Max(0, TotalPlanned - TotalRefunded);
            public int TotalEnergyNet => energyMoveNet + energyAtkNet;
            public bool AttackExecuted => attackExecuted;
        }

        readonly Stack<PreDeduct> _planStack = new();
        readonly List<ChainOption> _chainBuffer = new();
        readonly List<ChainOption> _derivedBuffer = new();
        readonly List<ChainPopupOptionData> _chainPopupOptionBuffer = new();
        Unit _currentChainFocus;
        IChainPopupUI _chainPopupUi;
        MonoBehaviour _chainPopupUiComponent;

        struct BonusTurnState
        {
            public bool active;
            public Unit unit;
            public int cap;
            public int remaining;
            public string sourceId;

            public void Reset()
            {
                active = false;
                unit = null;
                cap = 0;
                remaining = 0;
                sourceId = null;
            }
        }

        BonusTurnState _bonusTurn;

        public event Action BonusTurnStateChanged;

        public Unit CurrentBonusTurnUnit => IsBonusTurnActive ? _bonusTurn.unit : null;
        public int CurrentBonusTurnRemaining => IsBonusTurnActive ? Mathf.Max(0, _bonusTurn.remaining) : 0;
        public int CurrentBonusTurnCap => IsBonusTurnActive ? Mathf.Max(0, _bonusTurn.cap) : 0;
        public Unit CurrentChainFocus => _currentChainFocus;

        void NotifyBonusTurnStateChanged()
        {
            BonusTurnStateChanged?.Invoke();
        }

        IActionRules ResolveRules()
        {
            return rulebook != null ? (IActionRules)rulebook : ActionRulebook.Default;
        }

        public bool IsBonusTurnActive => _bonusTurn.active && _bonusTurn.unit != null;

        public bool IsBonusTurnFor(Unit unit)
        {
            return IsBonusTurnActive && unit != null && unit == _bonusTurn.unit;
        }

        int GetBonusRemaining(Unit unit)
        {
            return IsBonusTurnFor(unit) ? Mathf.Max(0, _bonusTurn.remaining) : int.MaxValue;
        }

        void ApplyBonusPreDeduct(Unit unit, ref PreDeduct preDeduct)
        {
            if (!IsBonusTurnFor(unit))
                return;

            int secs = Mathf.Max(0, preDeduct.secs);
            if (secs <= 0)
                return;

            int before = _bonusTurn.remaining;
            int after = Mathf.Max(0, before - secs);
            if (after != before)
            {
                _bonusTurn.remaining = after;
                NotifyBonusTurnStateChanged();
            }
            preDeduct.bonusSecs = secs;
        }

        void ApplyBonusResolve(Unit unit, PreDeduct preDeduct, int netSeconds)
        {
            if (!IsBonusTurnFor(unit))
                return;

            if (!preDeduct.valid || preDeduct.bonusSecs <= 0)
                return;

            int planned = Mathf.Max(0, preDeduct.bonusSecs);
            int delta = netSeconds - planned;
            if (delta < 0)
            {
                int refund = -delta;
                int before = _bonusTurn.remaining;
                int after = Mathf.Min(_bonusTurn.cap, before + refund);
                if (after != before)
                {
                    _bonusTurn.remaining = after;
                    NotifyBonusTurnStateChanged();
                }
            }
            else if (delta > 0)
            {
                int before = _bonusTurn.remaining;
                int after = Mathf.Max(0, before - delta);
                if (after != before)
                {
                    _bonusTurn.remaining = after;
                    NotifyBonusTurnStateChanged();
                }
            }
        }

        bool IsEffectivePlayerPhase(Unit unit)
        {
            if (IsBonusTurnFor(unit))
                return true;
            return turnManager != null && turnManager.IsPlayerPhase;
        }

        bool IsEffectiveEnemyPhase(Unit unit)
        {
            if (IsBonusTurnFor(unit))
                return false;
            return turnManager != null && !turnManager.IsPlayerPhase;
        }

        bool HasBonusTime(Unit unit, int secs)
        {
            if (secs <= 0)
                return true;
            if (!IsBonusTurnFor(unit))
                return true;
            return secs <= Mathf.Max(0, _bonusTurn.remaining);
        }

        string EvaluateBudgetFailure(Unit unit, int secs, int energy, ITurnBudget budget, IResourcePool resources)
        {
            if (!HasBonusTime(unit, secs))
                return "lackTime";

            if (secs > 0 && budget != null && !budget.HasTime(secs))
                return "lackTime";

            if (energy > 0 && resources != null && !resources.Has("Energy", energy))
                return "lackEnergy";

            return null;
        }

        bool MeetsBudget(Unit unit, int secs, int energy, ITurnBudget budget, IResourcePool resources)
        {
            return EvaluateBudgetFailure(unit, secs, energy, budget, resources) == null;
        }

        void BeginBonusTurn(Unit unit, int capSeconds, string sourceId)
        {
            if (unit == null)
                return;

            _bonusTurn.active = true;
            _bonusTurn.unit = unit;
            _bonusTurn.cap = Mathf.Max(0, capSeconds);
            _bonusTurn.remaining = Mathf.Max(0, capSeconds);
            _bonusTurn.sourceId = sourceId;
            SetChainFocus(unit);
            NotifyBonusTurnStateChanged();
        }

        void EndBonusTurn(Unit unit)
        {
            if (!IsBonusTurnFor(unit))
                return;

            _bonusTurn.Reset();
            NotifyBonusTurnStateChanged();
            SetChainFocus(turnManager != null ? turnManager.ActiveUnit : null);
        }

        void SetChainFocus(Unit unit)
        {
            if (_currentChainFocus == unit)
                return;
            _currentChainFocus = unit;
            var popup = ChainPopupUI;
            if (popup != null)
                popup.SetAnchor(ResolveChainAnchor(unit));
            ChainFocusChanged?.Invoke(unit);
        }

        TargetSelectionCursor ChainCursor
        {
            get
            {
                if (_chainCursor == null)
                {
                    var highlighter = EnsureChainHighlighter();
                    if (highlighter != null)
                        _chainCursor = new TargetSelectionCursor(highlighter);
                }
                return _chainCursor;
            }
        }

        IChainPopupUI ChainPopupUI
        {
            get
            {
                if (_chainPopupUi != null)
                {
                    if (_chainPopupUiComponent == null || !_chainPopupUiComponent)
                    {
                        _chainPopupUi = null;
                        _chainPopupUiComponent = null;
                    }
                }

                if (_chainPopupUi == null)
                    _chainPopupUi = ResolveChainPopupUI(out _chainPopupUiComponent);

                return _chainPopupUi;
            }
        }

        IChainPopupUI ResolveChainPopupUI(out MonoBehaviour component)
        {
            component = null;

            if (chainPopupUiBehaviour != null && chainPopupUiBehaviour is IChainPopupUI specified)
            {
                component = chainPopupUiBehaviour;
                return specified;
            }

            var local = GetComponent<IChainPopupUI>();
            if (local != null)
            {
                component = local as MonoBehaviour;
                return local;
            }

            var child = GetComponentInChildren<IChainPopupUI>(true);
            if (child != null)
            {
                component = child as MonoBehaviour;
                return child;
            }

#if UNITY_2023_1_OR_NEWER
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var behaviours = FindObjectsOfType<MonoBehaviour>(true);
#endif
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is IChainPopupUI ui)
                {
                    component = behaviour;
                    return ui;
                }
            }

            return null;
        }


        void Reset()
        {
            chainKeybinds = new List<ChainKeybind>
            {
                new ChainKeybind { id = "Reaction40", key = KeyCode.Alpha1 },
                new ChainKeybind { id = "Reaction20", key = KeyCode.Alpha2 },
                new ChainKeybind { id = "Free10", key = KeyCode.Alpha3 },
                new ChainKeybind { id = "FullRoundTest", key = KeyCode.Alpha4 },
                new ChainKeybind { id = "DerivedAfterAttack", key = KeyCode.Alpha5 },
                new ChainKeybind { id = "DerivedAfterDerived", key = KeyCode.Alpha6 }
            };
        }

        void Awake()
        {
            foreach (var mb in tools)
            {
                if (!mb) continue;
                WireTurnManager(mb);
                InjectCursorHighlighter(mb);
                if (mb is IActionToolV2 tool)
                {
                    if (!_toolsById.TryGetValue(tool.Id, out var list))
                    {
                        list = new List<IActionToolV2>();
                        _toolsById[tool.Id] = list;
                    }
                    if (!list.Contains(tool))
                        list.Add(tool);
                }
            }
        }

        void WireTurnManager(MonoBehaviour mb)
        {
            if (mb == null) return;
            switch (mb)
            {
                case AttackControllerV2 attack:
                    attack.AttachTurnManager(turnManager);
                    break;
                case HexClickMover mover:
                    mover.AttachTurnManager(turnManager);
                    if (turnManager != null)
                        WireMoveCostAdapter(mover.costProvider as MoveCostServiceV2Adapter);
                    break;
                case MoveCostServiceV2Adapter moveAdapter:
                    WireMoveCostAdapter(moveAdapter);
                    break;
            }
        }

        void InjectCursorHighlighter(MonoBehaviour mb)
        {
            if (mb == null)
                return;

            switch (mb)
            {
                case AttackControllerV2 attack:
                    attack.SetCursorHighlighter(EnsureAimHighlighter());
                    break;
                case ChainTestActionBase chainTool:
                    chainTool.SetCursorHighlighter(EnsureChainHighlighter());
                    break;
            }
        }

        HexBoardTiler ResolveTiler()
        {
            if (!tiler && authoring != null)
                tiler = authoring.GetComponent<HexBoardTiler>() ?? authoring.GetComponentInParent<HexBoardTiler>(true);
            return tiler;
        }

        IHexHighlighter EnsureAimHighlighter()
        {
            if (_aimHighlighter == null)
            {
                var resolved = ResolveTiler();
                if (resolved != null)
                    _aimHighlighter = new HexAreaPainter(resolved);
            }
            return _aimHighlighter;
        }

        IHexHighlighter EnsureChainHighlighter()
        {
            if (_chainHighlighter == null)
            {
                var resolved = ResolveTiler();
                if (resolved != null)
                    _chainHighlighter = new HexAreaPainter(resolved);
            }
            return _chainHighlighter;
        }

        void WireMoveCostAdapter(MoveCostServiceV2Adapter adapter)
        {
            if (adapter == null) return;
            adapter.turnManager = turnManager;
        }

        void RegisterPhaseGate()
        {
            if (turnManager != null)
                turnManager.RegisterPhaseStartGate(HandlePhaseStartGate);
        }

        void UnregisterPhaseGate()
        {
            if (turnManager != null)
                turnManager.UnregisterPhaseStartGate(HandlePhaseStartGate);
        }

        void OnEnable()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted += OnTurnStarted;
            }
            if (registerAsGateHub && TryClaimGateHub())
            {
                RegisterPhaseGate();
                if (turnManager != null)
                {
                    turnManager.RegisterTurnStartGate(HandleTurnStartGate);
                }
                _ownsGateHub = true;
            }
        }

        void OnDisable()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted -= OnTurnStarted;
            }
            if (_ownsGateHub)
            {
                UnregisterPhaseGate();
                if (turnManager != null)
                {
                    turnManager.UnregisterTurnStartGate(HandleTurnStartGate);
                }
                ReleaseGateHub();
                _ownsGateHub = false;
            }
            else if (turnManager != null)
            {
                turnManager.UnregisterTurnStartGate(HandleTurnStartGate);
            }
            ChainCursor?.Clear();
        }

        bool TryClaimGateHub()
        {
            if (s_gateHubInstance != null && s_gateHubInstance != this)
            {
                Debug.LogWarning($"[CAM] Multiple gate hubs detected. '{s_gateHubInstance.name}' already registered as gate hub.", this);
                return false;
            }

            s_gateHubInstance = this;
            return true;
        }

        void ReleaseGateHub()
        {
            if (ReferenceEquals(s_gateHubInstance, this))
                s_gateHubInstance = null;
        }

        void OnTurnStarted(Unit unit)
        {
            _currentUnit = unit;
            if (_activeTool != null && ResolveUnit(_activeTool) != _currentUnit)
                Cancel(false);

            if (_pendingEndTurn && unit != _pendingEndTurnUnit)
                ClearPendingEndTurn();

            _queuedActionsPending = 0;

            if (!registerAsGateHub || turnManager == null || unit == null)
                return;
        }

        IActionToolV2 SelectTool(string id)
        {
            if (!_toolsById.TryGetValue(id, out var list)) return null;
            foreach (var tool in list)
            {
                if (ResolveUnit(tool) == _currentUnit)
                    return tool;
            }
            return null;
        }

        void HandleIdleKeybind(KeyCode key, string toolId)
        {
            if (key == KeyCode.None || string.IsNullOrEmpty(toolId))
                return;

            if (Input.GetKeyDown(key))
                TryRequestIdleAction(toolId);
        }

        void TryRequestIdleAction(string toolId)
        {
            if (_pendingEndTurn)
                return;

            var tool = SelectTool(toolId);
            if (tool == null)
                return;

            if (!CanActivateAtIdle(tool))
                return;

            RequestAim(toolId);
        }

        bool CanActivateAtIdle(IActionToolV2 tool)
        {
            if (tool == null)
                return false;
            var owner = ResolveUnit(tool);

            if (IsBonusTurnActive)
            {
                if (!IsBonusTurnFor(owner))
                    return false;
                if (tool.Kind == ActionKind.FullRound)
                    return false;
            }

            var rules = ResolveRules();
            if (rules != null && !rules.CanActivateAtIdle(tool.Kind))
                return false;

            if (rules != null && !rules.AllowFriendlyInsertions())
            {
                if (_currentUnit != null && owner != _currentUnit)
                    return false;
            }
            if (turnManager != null && owner != null && turnManager.HasActiveFullRound(owner))
                return false;

            return true;
        }

        void Update()
        {
            if (_phase == Phase.Idle && !IsInputSuppressed && !_pendingEndTurn)
            {
                HandleIdleKeybind(keyMoveAim, "Move");
                HandleIdleKeybind(keyAttackAim, "Attack");
                if (chainKeybinds != null)
                {
                    foreach (var bind in chainKeybinds)
                    {
                        if (bind.key == KeyCode.None || string.IsNullOrEmpty(bind.id))
                            continue;
                        if (Input.GetKeyDown(bind.key))
                            TryRequestIdleAction(bind.id);
                    }
                }
            }

            if (_phase == Phase.Aiming)
            {
                var h = PickHexUnderMouse();
                if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
                {
                    _hover = h;
                    _activeTool?.OnHover(h.Value);
                }

                if (Input.GetMouseButtonDown(0)) Confirm();
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(true);
            }

            if (_pendingEndTurn)
                TryFinalizeEndTurn();
        }

        bool HasPendingEndTurn => _pendingEndTurn && _pendingEndTurnUnit != null;

        bool IsPendingEndTurnFor(Unit unit)
        {
            return HasPendingEndTurn && unit != null && unit == _pendingEndTurnUnit;
        }

        void ClearPendingEndTurn()
        {
            _pendingEndTurn = false;
            _pendingEndTurnUnit = null;
            _queuedActionsPending = 0;
        }

        bool CanFinalizeEndTurn()
        {
            if (!_pendingEndTurn || turnManager == null)
                return false;

            var unit = _pendingEndTurnUnit;
            if (unit == null)
                return false;

            if (!IsEffectivePlayerPhase(unit))
                return false;

            if (!IsBonusTurnFor(unit) && turnManager.ActiveUnit != unit)
                return false;

            if (_phase != Phase.Idle)
                return false;

            if (IsInputSuppressed)
                return false;

            if (_activeTool != null)
                return false;

            int allowedPlanDepth = _bonusPlanDepth >= 0 ? _bonusPlanDepth : 0;
            if (_planStack.Count > allowedPlanDepth)
                return false;

            if (_endTurnGuardDepth > 0)
                return false;

            if (_queuedActionsPending > 0)
                return false;

            return true;
        }

        void TryFinalizeEndTurn()
        {
            if (!CanFinalizeEndTurn())
                return;

            var unit = _pendingEndTurnUnit;
            ClearPendingEndTurn();
            if (IsBonusTurnFor(unit))
            {
                EndBonusTurn(unit);
            }
            else
            {
                turnManager.EndTurn(unit);
            }
        }

        IEnumerator RunSustainedBonusTurns(int capSeconds, string actionId)
        {
            if (turnManager == null)
                yield break;
            if (capSeconds <= 0)
                yield break;

            var players = turnManager.GetSideUnits(true);
            if (players == null || players.Count == 0)
                yield break;

            var snapshot = new List<Unit>(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                    snapshot.Add(players[i]);
            }

            if (snapshot.Count == 0)
                yield break;

            var prevUnit = _currentUnit;
            var prevPhase = _phase;
            bool prevPending = _pendingEndTurn;
            var prevPendingUnit = _pendingEndTurnUnit;
            int prevPlanDepth = _bonusPlanDepth;

            _pendingEndTurn = false;
            _pendingEndTurnUnit = null;
            _bonusPlanDepth = Mathf.Max(0, _planStack.Count);

            try
            {
                foreach (var unit in snapshot)
                {
                    if (unit == null)
                        continue;

                    BeginBonusTurn(unit, capSeconds, actionId);

                    string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                    Debug.Log($"[Turn] Idle BonusT({unitLabel}) cap={capSeconds}", this);

                    _currentUnit = unit;
                    _phase = Phase.Idle;
                    _activeTool = null;
                    _hover = null;

                    if (turnManager != null && turnManager.HasActiveFullRound(unit))
                    {
                        Log($"[FullRound] BonusT skip unit={TurnManagerV2.FormatUnitLabel(unit)} reason=fullround");
                        EndBonusTurn(unit);
                    }

                    while (IsBonusTurnFor(unit))
                    {
                        if (autoEndBonusTurns
                            && _bonusTurn.remaining <= 0
                            && !_pendingEndTurn
                            && !IsAnyChainWindowActive
                            && _queuedActionsPending <= 0
                            && _planStack.Count <= _bonusPlanDepth
                            && _activeTool == null
                            && !IsInputSuppressed
                            && _phase == Phase.Idle)
                        {
                            _pendingEndTurn = true;
                            _pendingEndTurnUnit = unit;
                            TryFinalizeEndTurn();
                        }
                        yield return null;
                    }

                    Debug.Log($"[Turn] End BonusT({unitLabel})", this);
                }
            }
            finally
            {
                _currentUnit = prevUnit;
                _phase = prevPhase;
                _pendingEndTurn = prevPending;
                _pendingEndTurnUnit = prevPendingUnit;
                _bonusPlanDepth = prevPlanDepth;
                bool hadBonus = _bonusTurn.active;
                _bonusTurn.Reset();
                if (hadBonus)
                    NotifyBonusTurnStateChanged();
            }
        }

        public bool RequestEndTurn(Unit unit = null)
        {
            if (turnManager == null)
                return false;

            if (unit == null)
                unit = _currentUnit;

            if (unit == null)
                return false;

            bool effectivePlayer = IsEffectivePlayerPhase(unit);
            if (!effectivePlayer)
                return false;

            if (!IsBonusTurnFor(unit) && !turnManager.IsPlayerUnit(unit))
                return false;

            if (!IsBonusTurnFor(unit) && turnManager.ActiveUnit != unit)
                return false;

            if (!IsBonusTurnFor(unit) && !turnManager.HasReachedIdle(unit))
                return false;

            if (_pendingEndTurn)
            {
                _pendingEndTurnUnit = unit;
            }
            else
            {
                _pendingEndTurn = true;
                _pendingEndTurnUnit = unit;
            }

            if (_phase == Phase.Aiming && _activeTool != null)
                Cancel(true);

            TryFinalizeEndTurn();
            return true;
        }

        Unit ResolveUnit(IActionToolV2 tool)
        {
            if (tool is HexClickMover mover && mover != null && mover.driver != null)
                return mover.driver.UnitRef;
            if (tool is AttackControllerV2 attack && attack != null && attack.driver != null)
                return attack.driver.UnitRef;
            if (tool is ChainTestActionBase chain && chain != null)
                return chain.ResolveUnit();
            return unitDriver != null ? unitDriver.UnitRef : null;
        }

        public void RequestAim(string toolId)
        {
            if (_pendingEndTurn)
                return;

            if (_phase != Phase.Idle) return;

            var tool = SelectTool(toolId);
            if (tool == null) return;
            if (!CanActivateAtIdle(tool))
                return;
            if (IsExecuting || IsAnyToolBusy()) return;

            var unit = ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimReject", $"(reason={reason})");
                return;
            }

            if (_activeTool != null)
                CleanupAfterAbort(_activeTool, false);

            _activeTool = tool;
            _hover = null;
            _activeTool.OnEnterAim();
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");
        }

        public void Cancel(bool userInitiated = false)
        {
            if (_phase != Phase.Aiming || _activeTool == null)
                return;

            CleanupAfterAbort(_activeTool, userInitiated);
        }

        public void Confirm()
        {
            if (_phase != Phase.Aiming || _activeTool == null)
                return;

            var unit = ResolveUnit(_activeTool);
            var hex = _hover ?? PickHexUnderMouse();

            StartCoroutine(ConfirmRoutine(_activeTool, unit, hex));
        }

        public bool TryAutoExecuteAction(string toolId, Hex target)
        {
            if (_pendingEndTurn)
                return false;

            if (_phase != Phase.Idle)
                return false;

            var tool = SelectTool(toolId);
            if (tool == null)
                return false;

            if (!CanActivateAtIdle(tool))
                return false;

            if (IsExecuting || IsAnyToolBusy())
                return false;

            var unit = ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return false;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimReject", $"(reason={reason})");
                return false;
            }

            if (_activeTool != null)
                CleanupAfterAbort(_activeTool, false);

            _activeTool = tool;
            _hover = target;
            _activeTool.OnEnterAim();
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");

            StartCoroutine(ConfirmRoutine(_activeTool, unit, target));
            return true;
        }

        void TryHideAllAimUI()
        {
            if (_activeTool != null)
                _activeTool.OnExitAim();

            foreach (var mb in tools)
            {
                if (mb == null || ReferenceEquals(mb, _activeTool))
                    continue;

                switch (mb)
                {
                    case HexClickMover mover:
                        mover.HideRange();
                        break;
                    case AttackControllerV2 attack:
                        attack.OnExitAim();
                        break;
                }
            }
        }

        IEnumerator ConfirmRoutine(IActionToolV2 tool, Unit unit, Hex? target)
        {
            _planStack.Clear();
            _phase = Phase.Executing;
            string kind = tool.Id;
            Unit guardUnit = null;
            bool guardActive = false;
            if (turnManager != null && !IsEffectivePlayerPhase(unit))
            {
                // 敌方回合：guard 键应当是当前激活的敌人，而不是本动作的 owner（可能是友方）
                guardUnit = turnManager.ActiveUnit ?? unit;
                if (guardUnit != null)
                {
                    turnManager.PushAutoTurnEndGuard(guardUnit);
                    guardActive = true;
                }
            }

            try
            {
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmStart");

                if (!target.HasValue)
                {
                    ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                    ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=targetInvalid)");
                    ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=targetInvalid)");
                    NotifyConfirmAbort(tool, unit, "targetInvalid");
                    CleanupAfterAbort(tool, false);
                    yield break;
                }

                if (!TryBeginAim(tool, unit, out var aimReason, false))
                {
                    ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");
                    string fail = string.IsNullOrEmpty(aimReason) ? "notReady" : aimReason;
                    ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", $"(reason={fail})");
                    ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", $"(reason={fail})");
                    NotifyConfirmAbort(tool, unit, fail);
                    CleanupAfterAbort(tool, false);
                    yield break;
                }

                ActionPhaseLogger.Log(unit, kind, "W2_PrecheckOk");

                var actionPlan = new ActionPlan
                {
                    kind = kind,
                    target = target.Value,
                    cost = BuildPlannedCost(tool, target.Value)
                };

                var budget = turnManager != null && unit != null ? turnManager.GetBudget(unit) : null;
                var resources = turnManager != null && unit != null ? turnManager.GetResources(unit) : null;
                var cooldowns = turnManager != null && unit != null ? turnManager.GetCooldowns(unit) : null;

                var cost = actionPlan.cost;
                if (tool.Kind == ActionKind.FullRound)
                {
                    int remaining = budget != null ? Mathf.Max(0, budget.Remaining) : 0;
                    if (remaining <= 0)
                    {
                        ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", "(reason=lackTime)");
                        ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", "(reason=lackTime)");
                        NotifyConfirmAbort(tool, unit, "lackTime");
                        CleanupAfterAbort(tool, false);
                        yield break;
                    }

                    cost.moveSecs = remaining;
                    cost.atkSecs = 0;
                    actionPlan.cost = cost;
                    cost = actionPlan.cost;

                    if (tool is IFullRoundActionTool fullRoundTool)
                        fullRoundTool.PrepareFullRoundSeconds(cost.TotalSeconds);
                }

            int remain = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;

            Log($"[Gate] W2 PreDeduct (move={cost.moveSecs}s/{cost.moveEnergy}, atk={cost.atkSecs}s/{cost.atkEnergy}, total={cost.TotalSeconds}s/{cost.TotalEnergy}, remain={remain}s, energy={energyBefore})");

            string failReason = null;
            if (!cost.valid)
                failReason = "targetInvalid";
            else
                failReason = EvaluateBudgetFailure(unit, cost.TotalSeconds, cost.TotalEnergy, budget, resources);

            if (failReason == null && !IsCooldownReadyForConfirm(tool, cooldowns))
                failReason = "cooldown";

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, kind, "W2_ConfirmAbort", $"(reason={failReason})");
                NotifyConfirmAbort(tool, unit, failReason);
                CleanupAfterAbort(tool, false);
                yield break;
            }

            ActionPhaseLogger.Log(unit, kind, "W2_PreDeductCheckOk");

            if (budget != null && cost.TotalSeconds > 0)
                budget.SpendTime(cost.TotalSeconds);

            if (resources != null)
            {
                if (cost.moveEnergy > 0)
                    resources.Spend("Energy", cost.moveEnergy, "PreDeduct_Move");
                if (cost.atkEnergy > 0)
                    resources.Spend("Energy", cost.atkEnergy, "PreDeduct_Attack");
            }

            var basePreDeduct = new PreDeduct
            {
                secs = cost.TotalSeconds,
                energyMove = cost.moveEnergy,
                energyAtk = cost.atkEnergy,
                valid = true
            };
            ApplyBonusPreDeduct(unit, ref basePreDeduct);
            _planStack.Push(basePreDeduct);

            TryHideAllAimUI();

            var pendingChain = new List<ChainQueuedAction>();
            bool cancelBase = false;
            if (ShouldOpenChainWindow(tool, unit))
            {
                bool isEnemyPhase = IsEffectiveEnemyPhase(unit);
                yield return RunChainWindow(unit, actionPlan, tool.Kind, isEnemyPhase, budget, resources, cooldowns, cost.TotalSeconds, pendingChain, cancelled => cancelBase = cancelled);
            }

            for (int i = pendingChain.Count - 1; i >= 0; --i)
            {
                var pending = pendingChain[i];
                if (pending.tool != null)
                {
                    if (_queuedActionsPending > 0)
                        _queuedActionsPending--;
                    yield return ExecuteAndResolve(pending.tool, pending.owner ?? unit, pending.plan, pending.budget, pending.resources);
                }
            }

            if (cancelBase)
            {
                if (_planStack.Count > 0)
                    _planStack.Pop();
                if (budget != null && basePreDeduct.valid && basePreDeduct.secs > 0)
                    budget.RefundTime(basePreDeduct.secs);
                if (IsBonusTurnFor(unit) && basePreDeduct.valid && basePreDeduct.bonusSecs > 0)
                {
                    int before = _bonusTurn.remaining;
                    int after = Mathf.Min(_bonusTurn.cap, before + basePreDeduct.bonusSecs);
                    if (after != before)
                    {
                        _bonusTurn.remaining = after;
                        NotifyBonusTurnStateChanged();
                    }
                }
                ActionPhaseLogger.Log(unit, actionPlan.kind, "W2_ConfirmAbort", "(reason={LinkCancelled})");
                NotifyConfirmAbort(tool, unit, "LinkCancelled");
                CleanupAfterAbort(tool, false);
                yield break;
            }

            if (tool.Kind == ActionKind.Sustained)
            {
                int bonusCap = Mathf.Max(0, basePreDeduct.secs);
                yield return RunSustainedBonusTurns(bonusCap, actionPlan.kind);
            }
            if (tool.Kind == ActionKind.FullRound && tool is IFullRoundActionTool frTool)
            {
                var planData = BuildFullRoundPlan(actionPlan, basePreDeduct, budget, resources);
                frTool.TriggerFullRoundImmediate(unit, turnManager, planData);

                if (budget != null)
                    planData.budgetAfter = budget.Remaining;
                if (resources != null)
                    planData.energyAfter = resources.Get("Energy");

                if (_planStack.Count > 0)
                    _planStack.Pop();

                ApplyCooldown(tool, unit);

                if (turnManager != null && unit != null)
                {
                    int rounds = Mathf.Max(1, frTool.FullRoundRounds);
                    turnManager.RegisterFullRound(unit, rounds, actionPlan.kind, frTool, planData);
                    turnManager.EndTurn(unit);
                }

                if (tool is IActionExecReportV2 execReport)
                    execReport.Consume();

                _activeTool = null;
                _hover = null;
                _phase = Phase.Idle;
                yield break;
            }
            yield return ExecuteAndResolve(tool, unit, actionPlan, budget, resources);
            TryFinalizeEndTurn();
        }
        finally
        {
            if (guardActive && guardUnit != null)
                turnManager.PopAutoTurnEndGuard(guardUnit);
        }

        }

        IEnumerator ExecuteAndResolve(IActionToolV2 tool, Unit unit, ActionPlan plan, ITurnBudget budget, IResourcePool resources)
        {
            _endTurnGuardDepth++;
            try
            {
                _phase = Phase.Executing;
                _hover = null;
                TryHideAllAimUI();

                int budgetBefore = budget != null ? budget.Remaining : 0;
                int energyBefore = resources != null ? resources.Get("Energy") : 0;
                ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteBegin", $"(budgetBefore={budgetBefore}, energyBefore={energyBefore})");

                var routine = tool.OnConfirm(plan.target);
                if (routine != null)
                    yield return StartCoroutine(routine);

                var report = BuildExecReport(tool, out var exec);
                if (!report.valid || exec == null)
                {
                    ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");
                    CleanupAfterAbort(tool, false);
                    yield break;
                }

                LogExecSummary(unit, plan.kind, report);

                ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");

                yield return StartCoroutine(Resolve(tool, unit, plan, exec, report, budget, resources));
            }
            finally
            {
                _endTurnGuardDepth = Mathf.Max(0, _endTurnGuardDepth - 1);
                TryFinalizeEndTurn();
            }
        }

        IEnumerator Resolve(IActionToolV2 tool, Unit unit, ActionPlan plan, IActionExecReportV2 exec, ExecReportData report, ITurnBudget budget, IResourcePool resources)
        {
            int used = report.TotalPlanned;
            int refunded = report.TotalRefunded;
            int net = report.NetSeconds;
            int energyMove = report.energyMoveNet;
            int energyAtk = report.energyAtkNet;
            int energyAction = report.TotalEnergyNet;
            bool freeMove = report.freeMoveApplied;
            string refundTag = report.refundTag;
            string reasonSuffix = string.IsNullOrEmpty(refundTag) ? string.Empty : $", refundReason={refundTag}";

            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveBegin", $"(used={used}, refunded={refunded}, net={net}, energyMove={energyMove}, energyAtk={energyAtk}, energyAction={energyAction}{reasonSuffix})");

            if (tool is IActionResolveEffect resolveEffect)
            {
                try
                {
                    resolveEffect.OnResolve(unit, plan.target);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }

            PreDeduct preDeduct = _planStack.Count > 0 ? _planStack.Pop() : default;

            int plannedSecs = preDeduct.valid ? Mathf.Max(0, preDeduct.secs) : 0;
            if (budget != null && preDeduct.valid)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                string timeReason = !string.IsNullOrEmpty(refundTag) ? refundTag : (freeMove ? "FreeMove" : null);
                string timeSuffix = string.IsNullOrEmpty(timeReason) ? string.Empty : $" (reason={timeReason})";
                int delta = net - plannedSecs;
                if (delta < 0)
                {
                    int refundAmount = -delta;
                    if (refundAmount > 0)
                    {
                        budget.RefundTime(refundAmount);
                    }
                }
                else if (delta > 0)
                {
                    budget.SpendTime(delta);
                    Log($"[Time] Spend {unitLabel} {delta}s -> Remain={budget.Remaining}");
                }
            }

            ApplyBonusResolve(unit, preDeduct, net);

            if (resources != null)
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                int plannedMoveEnergy = preDeduct.valid ? Mathf.Max(0, preDeduct.energyMove) : 0;
                int plannedAtkEnergy = preDeduct.valid ? Mathf.Max(0, preDeduct.energyAtk) : 0;

                int moveDelta = energyMove - plannedMoveEnergy;
                if (moveDelta > 0)
                {
                    resources.Spend("Energy", moveDelta, "Resolve_Move");
                }
                else if (moveDelta < 0)
                {
                    int refundAmount = -moveDelta;
                    string moveReason = !string.IsNullOrEmpty(refundTag)
                        ? refundTag
                        : (freeMove ? "FreeMove" : null);
                    bool moveSilent = string.IsNullOrEmpty(moveReason);
                    resources.Refund("Energy", refundAmount, moveSilent ? string.Empty : moveReason, moveSilent);
                }


                int atkDelta = energyAtk - plannedAtkEnergy;
                if (atkDelta > 0)
                {
                    resources.Spend("Energy", atkDelta, "Resolve_Attack");
                }
                else if (atkDelta < 0)
                {
                    int refundAmount = -atkDelta;
                    string atkReason = !string.IsNullOrEmpty(refundTag) ? refundTag : null;
                    bool atkSilent = string.IsNullOrEmpty(atkReason);
                    resources.Refund("Energy", refundAmount, atkSilent ? string.Empty : atkReason, atkSilent);
                }
            }

            var derivedQueue = new List<Tuple<IActionToolV2, ActionPlan>>();
            var cooldowns = (turnManager != null && unit != null) ? turnManager.GetCooldowns(unit) : null;
            bool shouldDerived = ShouldRunDerivedWindow(unit, tool);
            bool skipDerived = shouldDerived && _pendingEndTurn && IsPendingEndTurnFor(unit);
            if (shouldDerived && !skipDerived)
                yield return RunDerivedWindow(unit, tool, plan, report, budget, resources, cooldowns, derivedQueue);
            else
            {
                if (shouldDerived && skipDerived)
                    Log($"[Chain] DerivedPromptAbort(from={plan.kind}, reason=EndTurn)");
                derivedQueue.Clear();
            }

            int budgetAfter = budget != null ? budget.Remaining : 0;
            int energyAfter = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveEnd", $"(budgetAfter={budgetAfter}, energyAfter={energyAfter})");

            ApplyCooldown(tool, unit);

            exec.Consume();
            _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;

            if (derivedQueue.Count > 0)
            {
                for (int i = 0; i < derivedQueue.Count; i++)
                {
                    var pending = derivedQueue[i];
                    if (pending?.Item1 != null)
                    {
                        if (_queuedActionsPending > 0)
                            _queuedActionsPending--;
                        yield return ExecuteAndResolve(pending.Item1, unit, pending.Item2, budget, resources);
                    }
                }
            }
            TryFinalizeEndTurn();
        }
        FullRoundQueuedPlan BuildFullRoundPlan(ActionPlan plan, PreDeduct preDeduct, ITurnBudget budget, IResourcePool resources)
        {
            var queued = new FullRoundQueuedPlan
            {
                valid = preDeduct.valid,
                target = plan.target,
                plannedSeconds = Mathf.Max(0, preDeduct.secs),
                plannedMoveEnergy = Mathf.Max(0, preDeduct.energyMove),
                plannedAttackEnergy = Mathf.Max(0, preDeduct.energyAtk),
                budgetBefore = budget != null ? budget.Remaining : 0,
                energyBefore = resources != null ? resources.Get("Energy") : 0,
                budgetAfter = budget != null ? budget.Remaining : 0,
                energyAfter = resources != null ? resources.Get("Energy") : 0
            };
            return queued;
        }
        void ApplyCooldown(IActionToolV2 tool, Unit unit)
        {
            if (turnManager == null || tool == null || unit == null)
                return;

            var cooldowns = turnManager.GetCooldowns(unit);
            if (cooldowns == null)
                return;

            switch (tool)
            {
                case ChainTestActionBase chainTool:
                    int seconds = chainTool.CooldownSeconds;
                    string skillId = chainTool.CooldownId;
                    if (!string.IsNullOrEmpty(skillId) && seconds > 0)
                        cooldowns.StartSeconds(skillId, seconds);
                    break;
            }
        }

        ExecReportData BuildExecReport(IActionToolV2 tool, out IActionExecReportV2 exec)
        {
            exec = tool as IActionExecReportV2;
            if (exec == null)
                return default;

            var data = new ExecReportData
            {
                valid = false,
                plannedSecsMove = 0,
                plannedSecsAtk = 0,
                refundedSecsMove = 0,
                refundedSecsAtk = 0,
                energyMoveNet = 0,
                energyAtkNet = 0,
                freeMoveApplied = false,
                refundTag = null
            };

            if (tool is HexClickMover mover)
            {
                if (mover.HasPendingExecReport)
                {
                    data.valid = true;
                    data.plannedSecsMove = Mathf.Max(0, mover.ReportUsedSeconds);
                    data.refundedSecsMove = Mathf.Max(0, mover.ReportRefundedSeconds);
                    data.energyMoveNet = mover.ReportEnergyMoveNet;
                    data.energyAtkNet = 0;
                    data.freeMoveApplied = mover.ReportFreeMoveApplied;
                    data.refundTag = mover.ReportRefundTag;
                    data.attackExecuted = true;
                }
            }
            else if (tool is AttackControllerV2 attack)
            {
                if (attack.HasPendingExecReport)
                {
                    data.valid = true;
                    data.plannedSecsMove = Mathf.Max(0, attack.ReportMoveUsedSeconds);
                    data.plannedSecsAtk = Mathf.Max(0, attack.ReportAttackUsedSeconds);
                    data.refundedSecsMove = Mathf.Max(0, attack.ReportMoveRefundSeconds);
                    data.refundedSecsAtk = Mathf.Max(0, attack.ReportAttackRefundSeconds);
                    data.energyMoveNet = attack.ReportEnergyMoveNet;
                    data.energyAtkNet = attack.ReportEnergyAtkNet;
                    data.freeMoveApplied = attack.ReportFreeMoveApplied;
                    data.refundTag = attack.ReportRefundTag;
                    data.attackExecuted = attack.ReportAttackExecuted;
                }
            }
            else
            {
                data.valid = true;
                data.plannedSecsMove = Mathf.Max(0, exec.UsedSeconds);
                data.refundedSecsMove = Mathf.Max(0, exec.RefundedSeconds);
                if (exec is IActionEnergyReportV2 energyReport)
                {
                    data.energyMoveNet = energyReport.EnergyUsed;
                }
                data.attackExecuted = true;
            }

            return data;
        }

        void LogExecSummary(Unit unit, string kind, ExecReportData report)
        {
            string label = TurnManagerV2.FormatUnitLabel(unit);
            string freeMove = report.freeMoveApplied ? " (FreeMove)" : string.Empty;
            string reason = string.IsNullOrEmpty(report.refundTag) ? string.Empty : $" [{report.refundTag}]";
            if (string.Equals(kind, "Move", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Move] Use secs={report.TotalPlanned}s refund={report.TotalRefunded}s energy={report.energyMoveNet} U={label}{freeMove}{reason}");
            }
            else if (string.Equals(kind, "Attack", System.StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Attack] Use moveSecs={report.plannedSecsMove}s atkSecs={report.plannedSecsAtk}s energyMove={report.energyMoveNet} energyAtk={report.energyAtkNet} U={label}{freeMove}{reason}");
            }
            else
            {
                Log($"[Action] {label} [{kind}] ExecSummary used={report.TotalPlanned}s refund={report.TotalRefunded}s energy={report.energyMoveNet + report.energyAtkNet}{freeMove}{reason}");
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void Log(string message)
        {
            if (debugLog)
                Debug.Log(message, this);
        }

        void CleanupAfterAbort(IActionToolV2 tool, bool logCancel)
        {
            if (tool == null) return;

            if (logCancel && _phase == Phase.Aiming)
            {
                var unit = ResolveUnit(tool);
                ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
            }

            TryHideAllAimUI();
            if (_activeTool == tool)
                _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;
            _planStack.Clear();
            TryFinalizeEndTurn();
        }

        void NotifyConfirmAbort(IActionToolV2 tool, Unit unit, string reason)
        {
            if (tool == null)
                return;

            if (string.IsNullOrEmpty(reason))
                reason = "notReady";

            switch (tool)
            {
                case HexClickMover mover:
                    mover.HandleConfirmAbort(unit, reason);
                    break;
                case AttackControllerV2 attack:
                    attack.HandleConfirmAbort(unit, reason);
                    break;
            }
        }

        bool TryBeginAim(IActionToolV2 tool, Unit unit, out string reason, bool raiseHud = true)
        {
            reason = null;
            string raw = null;
            bool ready = tool switch
            {
                HexClickMover mover => mover.TryPrecheckAim(out raw, raiseHud),
                AttackControllerV2 attack => attack.TryPrecheckAim(out raw, raiseHud),
                _ => true
            };

            if (!ready)
            {
                reason = MapAimReason(raw);
                return false;
            }

            if (turnManager != null && unit != null)
            {
                var cost = GetBaselineCost(tool);
                var budget = turnManager.GetBudget(unit);
                var resources = turnManager.GetResources(unit);
                var budgetFail = EvaluateBudgetFailure(unit, cost.TotalSeconds, cost.TotalEnergy, budget, resources);
                if (budgetFail != null)
                {
                    reason = budgetFail;
                    return false;
                }

                var cooldowns = turnManager.GetCooldowns(unit);
                if (!IsCooldownReadyForConfirm(tool, cooldowns))
                {
                    reason = "cooldown";
                    return false;
                }
                if (tool.Kind == ActionKind.FullRound && !turnManager.CanDeclareFullRound(unit, out var frReason))
                {
                    reason = string.IsNullOrEmpty(frReason) ? "fullRoundBlock" : frReason;
                    return false;
                }
            }

            return true;
        }

        PlannedCost GetBaselineCost(IActionToolV2 tool)
        {
            if (tool is IActionCostPreviewV2 preview && preview.TryPeekCost(out var previewSecs, out var previewEnergy))
            {
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, previewSecs),
                    moveEnergy = Mathf.Max(0, previewEnergy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is HexClickMover mover)
            {
                int secs = Mathf.Max(1, Mathf.CeilToInt(mover.config ? mover.config.timeCostSeconds : 1f));
                int energyRate = mover.config ? Mathf.Max(0, mover.config.energyCost) : 0;
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, secs),
                    moveEnergy = Mathf.Max(0, energyRate),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is AttackControllerV2 attack)
            {
                int moveSecs = 1;
                int moveEnergyRate = attack.moveConfig ? Mathf.Max(0, attack.moveConfig.energyCost) : 0;
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(1, moveSecs),
                    atkSecs = 0,
                    moveEnergy = Mathf.Max(0, moveEnergyRate),
                    atkEnergy = 0,
                    valid = true
                };
            }

            return new PlannedCost { valid = true };
        }

        PlannedCost BuildPlannedCost(IActionToolV2 tool, Hex target)
        {
            if (tool is IActionCostPreviewV2 preview && preview.TryPeekCost(out var previewSecs, out var previewEnergy))
            {
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, previewSecs),
                    moveEnergy = Mathf.Max(0, previewEnergy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            if (tool is HexClickMover mover)
            {
                var planned = mover.PeekPlannedCost(target);
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, planned.moveSecs),
                    atkSecs = 0,
                    moveEnergy = Mathf.Max(0, planned.moveEnergy),
                    atkEnergy = 0,
                    valid = planned.valid
                };
            }

            if (tool is AttackControllerV2 attack)
            {
                var planned = attack.PeekPlannedCost(target);
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, planned.moveSecs),
                    atkSecs = Mathf.Max(0, planned.atkSecs),
                    moveEnergy = Mathf.Max(0, planned.moveEnergy),
                    atkEnergy = Mathf.Max(0, planned.atkEnergy),
                    valid = planned.valid
                };
            }

            return new PlannedCost { valid = true };
        }

        bool ShouldOpenChainWindow(IActionToolV2 tool, Unit unit)
        {
            if (tool == null || unit == null || turnManager == null)
                return false;

            bool treatAsPlayerPhase = IsEffectivePlayerPhase(unit);
            bool isEnemyPhase = !treatAsPlayerPhase;
            if (!isEnemyPhase)
            {
                if (!turnManager.IsPlayerUnit(unit))
                    return false;
            }
            else
            {
                if (!turnManager.IsEnemyUnit(unit))
                    return false;
            }

            var rules = ResolveRules();
            if (rules == null)
                return false;

            var allowed = rules.AllowedChainFirstLayer(tool.Kind, isEnemyPhase);
            return allowed != null && allowed.Count > 0;
        }

        static bool IsDerivedSourceKind(ActionKind kind)
        {
            return kind == ActionKind.Standard
                || kind == ActionKind.Reaction
                || kind == ActionKind.Derived;
        }

        bool ShouldRunDerivedWindow(Unit unit, IActionToolV2 baseTool)
        {
            if (unit == null || baseTool == null)
                return false;
            if (turnManager == null)
                return false;
            if (!IsEffectivePlayerPhase(unit))
                return false;
            if (!turnManager.IsPlayerUnit(unit))
                return false;

            return IsDerivedSourceKind(baseTool.Kind);
        }

        KeyCode ResolveChainKey(string id)
        {
            if (string.IsNullOrEmpty(id))
                return KeyCode.None;

            foreach (var bind in chainKeybinds)
            {
                if (!string.IsNullOrEmpty(bind.id) && string.Equals(bind.id, id, StringComparison.OrdinalIgnoreCase))
                    return bind.key;
            }

            return KeyCode.None;
        }

        List<ChainOption> BuildChainOptions(Unit unit, ITurnBudget budget, IResourcePool resources, int baseTimeCost, IReadOnlyList<ActionKind> allowedKinds, ICooldownSink cooldowns, ISet<IActionToolV2> pending, bool isEnemyPhase, bool restrictToOwner)
        {
            _chainBuffer.Clear();
            if (allowedKinds == null || allowedKinds.Count == 0)
                return _chainBuffer;

            var rules = ResolveRules();
            bool allowFriendlyInsertion = !restrictToOwner && (isEnemyPhase || (rules?.AllowFriendlyInsertions() ?? false));
            bool enforceReactionWithinBase = rules?.ReactionMustBeWithinBaseTime() ?? true;

            foreach (var pair in _toolsById)
            {
                foreach (var tool in pair.Value)
                {
                    if (tool == null)
                        continue;

                    var owner = ResolveUnit(tool);
                    if (restrictToOwner && owner != unit)
                        continue;
                    if (!allowFriendlyInsertion && owner != unit)
                        continue;
                    if (allowFriendlyInsertion && owner == null)
                        continue;
                    if (allowFriendlyInsertion && isEnemyPhase)
                    {
                        if (turnManager == null || owner == null || !turnManager.IsPlayerUnit(owner))
                            continue;
                    }
                    else if (allowFriendlyInsertion && owner != unit)
                    {
                        if (turnManager != null && turnManager.IsEnemyUnit(owner))
                            continue;
                    }

                    ITurnBudget ownerBudget = budget;
                    IResourcePool ownerResources = resources;
                    ICooldownSink ownerCooldowns = cooldowns;
                    if (owner != unit)
                    {
                        if (turnManager == null)
                            continue;

                        ownerBudget = turnManager.GetBudget(owner);
                        ownerResources = turnManager.GetResources(owner);
                        ownerCooldowns = turnManager.GetCooldowns(owner);
                    }

                    if (allowFriendlyInsertion && ownerBudget == null && ownerResources == null && ownerCooldowns == null)
                        continue;
                    if (turnManager != null && owner != null && turnManager.HasActiveFullRound(owner))
                        continue;

                    if (pending != null && pending.Contains(tool))
                        continue;

                    bool kindAllowed = false;
                    for (int i = 0; i < allowedKinds.Count; i++)
                    {
                        if (allowedKinds[i] == tool.Kind)
                        {
                            kindAllowed = true;
                            break;
                        }
                    }

                    if (!kindAllowed)
                        continue;

                    if (tool.Kind == ActionKind.Derived)
                        continue;

                    var cost = GetBaselineCost(tool);
                    if (!cost.valid)
                        continue;

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (tool.Kind == ActionKind.Reaction)
                    {
                        if (enforceReactionWithinBase)
                        {
                            if (baseTimeCost <= 0 && secs > 0)
                                continue;
                            if (secs > baseTimeCost)
                                continue;
                        }
                    }

                    if (tool.Kind == ActionKind.Free && secs != 0)
                        continue;

                    if (!MeetsBudget(owner, secs, energy, ownerBudget, ownerResources))
                        continue;

                    if (ownerCooldowns != null && !IsCooldownReadyForConfirm(tool, ownerCooldowns))
                        continue;

                    var key = ResolveChainKey(tool.Id);
                    if (key == KeyCode.None)
                        continue;

                    _chainBuffer.Add(new ChainOption
                    {
                        tool = tool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = tool.Kind,
                        owner = owner,
                        budget = ownerBudget,
                        resources = ownerResources,
                        cooldowns = ownerCooldowns
                    });
                }
            }

            return _chainBuffer;
        }

        static string FormatChainStageLabel(int depth)
        {
            if (depth <= 0)
                return "W2.1";

            var builder = new System.Text.StringBuilder("W2.1");
            for (int i = 0; i < depth; i++)
                builder.Append(".1");
            return builder.ToString();
        }

        int GetChainOwnerOrder(Unit owner)
        {
            if (owner == null || turnManager == null)
                return int.MaxValue;

            if (turnManager.IsPlayerUnit(owner))
                return turnManager.GetTurnOrderIndex(owner, true);

            if (turnManager.IsEnemyUnit(owner))
                return turnManager.GetTurnOrderIndex(owner, false);

            return int.MaxValue - 1;
        }

        Unit ResolveNextChainOwner(List<ChainOption> options, HashSet<Unit> usedOwners)
        {
            if (options == null || options.Count == 0)
                return null;

            Unit bestOwner = null;
            int bestOrder = int.MaxValue;
            string bestLabel = null;

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var owner = option.owner;
                if (owner == null)
                    continue;
                if (usedOwners != null && usedOwners.Contains(owner))
                    continue;

                int order = GetChainOwnerOrder(owner);
                string label = TurnManagerV2.FormatUnitLabel(owner);
                if (bestOwner == null
                    || order < bestOrder
                    || (order == bestOrder && string.CompareOrdinal(label, bestLabel) < 0))
                {
                    bestOwner = owner;
                    bestOrder = order;
                    bestLabel = label;
                }
            }

            return bestOwner;
        }

        static string BuildOwnerStageMessage(Unit owner, int optionCount)
        {
            string ownerLabel = owner != null ? TurnManagerV2.FormatUnitLabel(owner) : "?";
            return $"(owner={ownerLabel} count={optionCount})";
        }

        string BuildStageMessage(List<ChainOption> options, bool isEnemyPhase)
        {
            if (options == null || options.Count == 0)
                return "(count:0)";

            string message = $"(count:{options.Count})";

            if (!isEnemyPhase)
                return message;

            var perOwner = new Dictionary<Unit, int>();
            for (int i = 0; i < options.Count; i++)
            {
                var owner = options[i].owner;
                if (owner == null)
                    continue;
                if (turnManager != null && !turnManager.IsPlayerUnit(owner))
                    continue;

                perOwner.TryGetValue(owner, out var current);
                perOwner[owner] = current + 1;
            }

            if (perOwner.Count == 0)
                return message;

            var ordered = perOwner.Keys
                .Select(u => (unit: u, label: TurnManagerV2.FormatUnitLabel(u)))
                .OrderBy(t => t.label, StringComparer.Ordinal)
                .ToList();

            var breakdown = new System.Text.StringBuilder();
            foreach (var entry in ordered)
            {
                if (breakdown.Length > 0)
                    breakdown.Append(' ');
                breakdown.Append(entry.label);
                breakdown.Append(" count=");
                breakdown.Append(perOwner[entry.unit]);
            }

            if (breakdown.Length > 0)
                return $"(count:{options.Count} {breakdown})";

            return message;
        }

        Transform ResolveChainAnchor(Unit unit)
        {
            if (unit == null || turnManager == null)
                return null;

            var context = turnManager.GetContext(unit);
            return context != null ? context.transform : null;
        }

        Sprite ResolveChainOptionIcon(IActionToolV2 tool)
        {
            if (tool is ChainTestActionBase chainTool && chainTool != null)
                return chainTool.Icon;

            return null;
        }

        string FormatChainOptionMeta(ChainOption option)
        {
            var parts = new List<string>(3);

            switch (option.kind)
            {
                case ActionKind.Reaction:
                    parts.Add("Reaction");
                    break;
                case ActionKind.Free:
                    parts.Add("Free");
                    break;
                case ActionKind.Derived:
                    parts.Add("Derived");
                    break;
                case ActionKind.FullRound:
                    parts.Add("Full Round");
                    break;
                case ActionKind.Sustained:
                    parts.Add("Sustained");
                    break;
                default:
                    parts.Add(option.kind.ToString());
                    break;
            }

            if (option.secs > 0)
                parts.Add($"{option.secs}s");

            if (option.energy > 0)
                parts.Add($"Energy {option.energy}");

            return parts.Count > 0 ? string.Join(" · ", parts) : string.Empty;
        }

        ChainPopupWindowData BuildChainPopupWindow(Unit unit, ActionPlan basePlan, ActionKind baseKind, bool isEnemyPhase)
        {
            string header = BuildChainWindowHeader(unit, basePlan, isEnemyPhase);
            string prompt = BuildChainWindowPrompt(basePlan, baseKind, isEnemyPhase);
            return new ChainPopupWindowData(header, prompt, isEnemyPhase);
        }

        ChainPopupWindowData BuildDerivedPopupWindow(Unit unit, IActionToolV2 baseTool, ActionPlan basePlan, bool isEnemyPhase)
        {
            string header = BuildChainWindowHeader(unit, basePlan, isEnemyPhase);
            string prompt = BuildChainWindowPrompt(basePlan, ActionKind.Derived, isEnemyPhase);
            return new ChainPopupWindowData(header, prompt, isEnemyPhase);
        }

        string BuildChainWindowHeader(Unit unit, ActionPlan basePlan, bool isEnemyPhase)
        {
            if (!string.IsNullOrEmpty(basePlan.kind)
                && basePlan.kind.StartsWith("PhaseStart", StringComparison.OrdinalIgnoreCase))
            {
                return BuildPhaseStartHeader(unit, isEnemyPhase);
            }

            if (!string.IsNullOrEmpty(basePlan.kind))
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                if (!string.IsNullOrEmpty(unitLabel) && unitLabel != "?")
                    return $"{basePlan.kind} ({unitLabel})";
                return basePlan.kind;
            }

            return "Chain";
        }

        string BuildPhaseStartHeader(Unit unit, bool isEnemyPhase)
        {
            if (turnManager == null)
                return "Begin";

            int phaseIndex = Mathf.Max(1, turnManager.CurrentPhaseIndex);
            string ownerTag = BuildChainOwnerTag(unit, isEnemyPhase);
            if (!string.IsNullOrEmpty(ownerTag))
                return $"Begin T{phaseIndex}({ownerTag})";
            return $"Begin T{phaseIndex}";
        }

        string BuildChainOwnerTag(Unit unit, bool isEnemyPhase)
        {
            if (unit != null)
            {
                string label = TurnManagerV2.FormatUnitLabel(unit);
                if (!string.IsNullOrEmpty(label) && label != "?")
                    return label;
            }

            if (turnManager == null || unit == null)
                return isEnemyPhase ? "Enemy" : "1P";

            bool isPlayer = turnManager.IsPlayerUnit(unit);
            int order = turnManager.GetTurnOrderIndex(unit, isPlayer);
            if (order < 0 || order == int.MaxValue)
                order = 0;

            if (isPlayer)
                return $"{order + 1}P";

            return $"E{order + 1}";
        }

        string BuildChainWindowPrompt(ActionPlan basePlan, ActionKind baseKind, bool isEnemyPhase)
        {
            if (!string.IsNullOrEmpty(basePlan.kind)
                && basePlan.kind.StartsWith("PhaseStart", StringComparison.OrdinalIgnoreCase))
            {
                return "Use Free Action?";
            }

            return baseKind switch
            {
                ActionKind.Reaction => "Select Reaction",
                ActionKind.Free => "Select Free Action",
                ActionKind.Derived => "Select Derived Action",
                _ => "Select Chain Action"
            };
        }

        void UpdateChainPopupStage(IChainPopupUI popup, string label, List<ChainOption> options, bool showSkip)
        {
            if (popup == null)
                return;

            _chainPopupOptionBuffer.Clear();
            if (options != null && options.Count > 1)
                options.Sort(CompareChainOptions);

            Unit lastOwner = null;
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                string id = option.tool != null ? option.tool.Id : string.Empty;
                string name = string.IsNullOrEmpty(id) ? "?" : id;
                string meta = FormatChainOptionMeta(option);
                var icon = ResolveChainOptionIcon(option.tool);

                bool startsGroup = false;
                string groupLabel = null;
                var owner = option.owner;
                if (owner != null)
                {
                    if (owner != lastOwner)
                    {
                        startsGroup = true;
                        groupLabel = TurnManagerV2.FormatUnitLabel(owner);
                    }
                }
                else if (lastOwner != null)
                {
                    startsGroup = true;
                }

                lastOwner = owner;

                _chainPopupOptionBuffer.Add(new ChainPopupOptionData(
                    id,
                    name,
                    meta,
                    icon,
                    option.key,
                    option.tool != null,
                    startsGroup,
                    groupLabel));
            }

            popup.UpdateStage(new ChainPopupStageData(label, _chainPopupOptionBuffer, showSkip));
        }

        int CompareChainOptions(ChainOption a, ChainOption b)
        {
            int orderA = GetChainOwnerOrder(a.owner);
            int orderB = GetChainOwnerOrder(b.owner);
            if (orderA != orderB)
                return orderA.CompareTo(orderB);

            string labelA = a.owner != null ? TurnManagerV2.FormatUnitLabel(a.owner) : string.Empty;
            string labelB = b.owner != null ? TurnManagerV2.FormatUnitLabel(b.owner) : string.Empty;
            int labelCompare = string.CompareOrdinal(labelA, labelB);
            if (labelCompare != 0)
                return labelCompare;

            int kindCompare = a.kind.CompareTo(b.kind);
            if (kindCompare != 0)
                return kindCompare;

            int secsCompare = a.secs.CompareTo(b.secs);
            if (secsCompare != 0)
                return secsCompare;

            int energyCompare = a.energy.CompareTo(b.energy);
            if (energyCompare != 0)
                return energyCompare;

            string idA = a.tool != null ? a.tool.Id : string.Empty;
            string idB = b.tool != null ? b.tool.Id : string.Empty;
            return string.CompareOrdinal(idA, idB);
        }

        IEnumerator RunChainWindow(Unit unit, ActionPlan basePlan, ActionKind baseKind, bool isEnemyPhase, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, int baseTimeCost, List<ChainQueuedAction> pendingActions, Action<bool> onComplete, bool restrictToOwner = false, bool allowOwnerCancel = false)
        {
            _chainWindowDepth++;
            PushInputSuppression();
            SetChainFocus(unit);

            IChainPopupUI popup = null;
            bool popupOpened = false;

            try
            {
                var rules = ResolveRules();
                IReadOnlyList<ActionKind> allowedKinds = rules?.AllowedChainFirstLayer(baseKind, isEnemyPhase);
                bool cancelledBase = false;
                int depth = 0;
                bool keepLooping = allowedKinds != null && allowedKinds.Count > 0;
                HashSet<IActionToolV2> pendingSet = null;
                if (pendingActions != null && pendingActions.Count > 0)
                {
                    pendingSet = new HashSet<IActionToolV2>();
                    for (int i = 0; i < pendingActions.Count; i++)
                    {
                        var entry = pendingActions[i];
                        if (entry.tool != null)
                            pendingSet.Add(entry.tool);
                    }
                }

                popup = ChainPopupUI;
                if (popup != null && keepLooping)
                {
                    popup.OpenWindow(BuildChainPopupWindow(unit, basePlan, baseKind, isEnemyPhase));
                    popup.SetAnchor(ResolveChainAnchor(unit));
                    popupOpened = true;
                }

                if (!keepLooping)
                {
                    string initialLabel = FormatChainStageLabel(depth);
                    ActionPhaseLogger.Log(unit, basePlan.kind, initialLabel, "(count:0)");
                    ActionPhaseLogger.Log(unit, basePlan.kind, $"{initialLabel} Skip");
                    onComplete?.Invoke(false);
                    yield break;
                }

                while (keepLooping)
                {
                    string label = FormatChainStageLabel(depth);
                    var stageKinds = allowedKinds;
                    var stageOwnersUsed = new HashSet<Unit>();
                    bool stageHasSelection = false;
                    bool stageActive = true;
                    bool stageLoggedOnce = false;
                    string lastStageMessage = null;
                    bool stageCancelledByInput = false;
                    Unit activeOwner = null;
                    bool activeOwnerLogged = false;
                    List<ActionKind> stageNextKinds = null;
                    bool stageSuppressCancel = false;
                    HashSet<Unit> stageFullRoundLogged = depth == 0 ? new HashSet<Unit>() : null;

                    while (stageActive)
                    {
                        if (stageSuppressCancel)
                        {
                            stageSuppressCancel = false;
                            yield return null;
                            continue;
                        }

                        var options = BuildChainOptions(unit, budget, resources, baseTimeCost, stageKinds, cooldowns, pendingSet, isEnemyPhase, restrictToOwner);
                        if (stageOwnersUsed.Count > 0 && options.Count > 0)
                        {
                            for (int i = options.Count - 1; i >= 0; --i)
                            {
                                var owner = options[i].owner;
                                if (owner != null && stageOwnersUsed.Contains(owner))
                                    options.RemoveAt(i);
                            }
                        }

                        if (activeOwner != null)
                        {
                            bool ownerStillAvailable = false;
                            for (int i = 0; i < options.Count; i++)
                            {
                                if (options[i].owner == activeOwner)
                                {
                                    ownerStillAvailable = true;
                                    break;
                                }
                            }

                            if (!ownerStillAvailable)
                            {
                                stageOwnersUsed.Add(activeOwner);
                                activeOwner = null;
                                activeOwnerLogged = false;
                                SetChainFocus(unit);
                                continue;
                            }
                        }

                        if (options.Count == 0)
                        {
                            if (!stageLoggedOnce)
                            {
                                ActionPhaseLogger.Log(unit, basePlan.kind, label, "(count:0)");
                                stageLoggedOnce = true;
                            }

                            ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Skip");
                            stageActive = false;
                            break;
                        }

                        bool ownerMode = false;
                        if (allowOwnerCancel)
                        {
                            for (int i = 0; i < options.Count; i++)
                            {
                                if (options[i].owner != null)
                                {
                                    ownerMode = true;
                                    break;
                                }
                            }
                        }

                        if (ownerMode && activeOwner == null)
                        {
                            activeOwner = ResolveNextChainOwner(options, stageOwnersUsed);
                            activeOwnerLogged = false;

                            SetChainFocus(activeOwner != null ? activeOwner : unit);

                            if (activeOwner == null)
                                ownerMode = false;
                        }

                        List<ChainOption> ownerOptions = options;
                        if (ownerMode && activeOwner != null)
                        {
                            ownerOptions = new List<ChainOption>();
                            for (int i = 0; i < options.Count; i++)
                            {
                                if (options[i].owner == activeOwner)
                                    ownerOptions.Add(options[i]);
                            }

                            if (ownerOptions.Count == 0)
                            {
                                if (stageFullRoundLogged != null
                                    && turnManager != null
                                    && turnManager.HasActiveFullRound(activeOwner)
                                    && stageFullRoundLogged.Add(activeOwner))
                                {
                                    string ownerLabel = TurnManagerV2.FormatUnitLabel(activeOwner);
                                    ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Skip", $"(owner={ownerLabel} reason=fullround)");
                                    stageLoggedOnce = true;
                                }

                                stageOwnersUsed.Add(activeOwner);
                                activeOwner = null;
                                activeOwnerLogged = false;
                                SetChainFocus(unit);
                                continue;
                            }
                        }

                        string message;
                        if (ownerMode && activeOwner != null)
                        {
                            message = BuildOwnerStageMessage(activeOwner, ownerOptions.Count);
                            if (!activeOwnerLogged)
                            {
                                ActionPhaseLogger.Log(unit, basePlan.kind, label, message);
                                stageLoggedOnce = true;
                                activeOwnerLogged = true;
                            }
                        }
                        else
                        {
                            message = BuildStageMessage(options, isEnemyPhase);
                            if (!stageLoggedOnce || !string.Equals(lastStageMessage, message, StringComparison.Ordinal))
                            {
                                ActionPhaseLogger.Log(unit, basePlan.kind, label, message);
                                stageLoggedOnce = true;
                                lastStageMessage = message;
                            }
                        }

                        if (popupOpened)
                            UpdateChainPopupStage(popup, label, ownerOptions, depth == 0 && !stageHasSelection);

                        if (_pendingEndTurn)
                        {
                            string cancelSuffix = ownerMode && activeOwner != null
                                ? $"(owner={TurnManagerV2.FormatUnitLabel(activeOwner)}, reason=EndTurn)"
                                : "(reason=EndTurn)";
                            ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel", cancelSuffix);
                            stageActive = false;
                            stageCancelledByInput = true;
                            keepLooping = false;
                            break;
                        }

                        bool handledInput = false;
                        bool selectionHandled = false;

                        System.Collections.IEnumerator HandleSelection(ChainOption option)
                        {
                            handledInput = true;

                            string ownerLabel = option.owner != null ? TurnManagerV2.FormatUnitLabel(option.owner) : "?";
                            string selectSuffix = option.owner != null && option.owner != unit
                                ? $"(id={option.tool.Id}, owner={ownerLabel}, kind={option.kind}, secs={option.secs}, energy={option.energy})"
                                : $"(id={option.tool.Id}, kind={option.kind}, secs={option.secs}, energy={option.energy})";
                            ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Select", selectSuffix);

                            ChainQueueOutcome outcome = default;
                            yield return TryQueueChainSelection(option, unit, basePlan.kind, label, pendingActions, result => outcome = result);

                            if (outcome.cancel)
                            {
                                if (isEnemyPhase)
                                {
                                    string cancelSelectionSuffix = ownerMode && activeOwner != null
                                        ? $"(owner={TurnManagerV2.FormatUnitLabel(activeOwner)})"
                                        : null;

                                    if (!string.IsNullOrEmpty(cancelSelectionSuffix))
                                        ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} CancelSelection", cancelSelectionSuffix);
                                    else
                                        ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} CancelSelection");
                                }
                                else
                                {
                                    ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel");
                                    keepLooping = false;
                                    stageActive = false;
                                }

                                selectionHandled = true;
                                yield break;
                            }

                            if (!outcome.queued)
                            {
                                if (!outcome.cancel)
                                {
                                    stageSuppressCancel = true;
                                }

                                selectionHandled = true;
                                yield break;
                            }

                            if (outcome.tool != null)
                            {
                                pendingSet ??= new HashSet<IActionToolV2>();
                                pendingSet.Add(outcome.tool);
                            }

                            if (option.owner != null)
                            {
                                SetChainFocus(option.owner);
                                stageOwnersUsed.Add(option.owner);
                            }

                            stageHasSelection = true;

                            if (option.kind == ActionKind.Reaction && turnManager != null && IsEffectivePlayerPhase(unit) && turnManager.IsPlayerUnit(unit))
                                cancelledBase = true;

                            if (rules != null)
                            {
                                var allowedNext = rules.AllowedChainNextLayer(option.kind);
                                if (allowedNext != null && allowedNext.Count > 0)
                                {
                                    stageNextKinds ??= new List<ActionKind>();
                                    for (int j = 0; j < allowedNext.Count; j++)
                                    {
                                        var nextKind = allowedNext[j];
                                        if (!stageNextKinds.Contains(nextKind))
                                            stageNextKinds.Add(nextKind);
                                    }
                                }
                            }

                            activeOwner = null;
                            activeOwnerLogged = false;
                            SetChainFocus(unit);

                            selectionHandled = true;
                        }

                        if (popupOpened)
                        {
                            if (popup.TryConsumeSkip())
                            {
                                ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Skip");
                                stageActive = false;
                                stageHasSelection = false;
                                keepLooping = false;
                                handledInput = true;
                                SetChainFocus(unit);
                                continue;
                            }

                            if (popup.TryConsumeSelection(out int selectedIndex))
                            {
                                if (selectedIndex >= 0 && selectedIndex < ownerOptions.Count)
                                {
                                    selectionHandled = false;
                                    yield return HandleSelection(ownerOptions[selectedIndex]);

                                    if (!stageActive)
                                        break;

                                    if (selectionHandled)
                                        continue;
                                }
                            }
                        }

                        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                        {
                            string cancelSuffix = null;
                            if (ownerMode && activeOwner != null)
                                cancelSuffix = $"(owner={TurnManagerV2.FormatUnitLabel(activeOwner)})";

                            if (!string.IsNullOrEmpty(cancelSuffix))
                                ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel", cancelSuffix);
                            else
                                ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Cancel");

                            if (ownerMode && activeOwner != null && allowOwnerCancel && !stageHasSelection)
                            {
                                stageOwnersUsed.Add(activeOwner);
                                activeOwner = null;
                                activeOwnerLogged = false;
                                yield return null;
                                continue;
                            }

                            stageActive = false;
                            stageCancelledByInput = true;
                            break;
                        }

                        for (int i = 0; i < ownerOptions.Count; i++)
                        {
                            var option = ownerOptions[i];
                            if (option.key == KeyCode.None || !Input.GetKeyDown(option.key))
                                continue;

                            selectionHandled = false;
                            yield return HandleSelection(option);

                            if (!stageActive)
                                break;

                            if (selectionHandled)
                                break;
                        }

                        if (!stageActive)
                        {
                            SetChainFocus(unit);
                            break;
                        }

                        if (handledInput)
                            continue;

                        yield return null;
                    }
                    if (stageCancelledByInput)
                    {
                        yield return null;
                        stageCancelledByInput = false;
                    }

                    if (!keepLooping)
                        break;

                    if (!stageHasSelection)
                    {
                        keepLooping = false;
                        break;
                    }

                    if (stageNextKinds != null && stageNextKinds.Count > 0)
                        allowedKinds = stageNextKinds;
                    else
                        allowedKinds = Array.Empty<ActionKind>();
                    depth += 1;
                    keepLooping = allowedKinds != null && allowedKinds.Count > 0;
                }
                onComplete?.Invoke(cancelledBase);
            }
            finally
            {
                SetChainFocus(turnManager != null ? turnManager.ActiveUnit : null);
                PopInputSuppression();
                if (_chainWindowDepth > 0)
                    _chainWindowDepth--;
                if (popupOpened && popup != null)
                    popup.CloseWindow();
                TryFinalizeEndTurn();
            }
        }

        List<ChainOption> BuildDerivedOptions(Unit unit, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, IReadOnlyList<string> allowedIds)
        {
            _derivedBuffer.Clear();
            if (allowedIds == null || allowedIds.Count == 0)
                return _derivedBuffer;

            for (int i = 0; i < allowedIds.Count; i++)
            {
                string id = allowedIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!_toolsById.TryGetValue(id, out var toolsForId))
                    continue;

                for (int j = 0; j < toolsForId.Count; j++)
                {
                    var tool = toolsForId[j];
                    if (tool == null)
                        continue;

                    if (tool.Kind != ActionKind.Derived)
                        continue;

                    var owner = ResolveUnit(tool);
                    if (owner != unit)
                        continue;

                    var cost = GetBaselineCost(tool);
                    if (!cost.valid)
                        continue;

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (!MeetsBudget(unit, secs, energy, budget, resources))
                        continue;

                    if (cooldowns != null && !IsCooldownReadyForConfirm(tool, cooldowns))
                        continue;

                    var key = ResolveChainKey(tool.Id);

                    _derivedBuffer.Add(new ChainOption
                    {
                        tool = tool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = tool.Kind
                    });
                }
            }

            return _derivedBuffer;
        }

        bool DetermineDerivedBaseSuccess(IActionToolV2 tool, ExecReportData report)
        {
            if (!report.valid)
                return false;

            if (tool == null)
                return true;

            if (tool is AttackControllerV2)
                return report.AttackExecuted;

            return true;
        }

        IEnumerator RunDerivedWindow(Unit unit, IActionToolV2 baseTool, ActionPlan basePlan, ExecReportData report, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, List<Tuple<IActionToolV2, ActionPlan>> derivedQueue)
        {
            _chainWindowDepth++;
            PushInputSuppression();

            IChainPopupUI popup = null;
            bool popupOpened = false;

            try
            {
                derivedQueue ??= new List<Tuple<IActionToolV2, ActionPlan>>();

                var rules = ResolveRules();
                var allowedIds = rules?.AllowedDerivedActions(basePlan.kind);
                bool baseSuccess = DetermineDerivedBaseSuccess(baseTool, report);

                if (!baseSuccess)
                {
                    Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count=0, baseSuccess=false)");
                    Log("[Chain] DerivedPromptAbort(base-fail)");
                    _derivedBuffer.Clear();
                    yield break;
                }

                if (allowedIds == null || allowedIds.Count == 0)
                {
                    Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count=0, baseSuccess=true)");
                    Log("[Chain] DerivedPromptAbort(auto-skip)");
                    _derivedBuffer.Clear();
                    yield break;
                }

                var options = BuildDerivedOptions(unit, budget, resources, cooldowns, allowedIds);
                Log($"[Chain] DerivedPromptOpen(from={basePlan.kind}, count={options.Count}, baseSuccess=true)");

                if (options.Count == 0)
                {
                    Log("[Chain] DerivedPromptAbort(auto-skip)");
                    options.Clear();
                    yield break;
                }

                popup = ChainPopupUI;
                bool isEnemyPhase = turnManager != null && turnManager.IsEnemyUnit(unit);
                if (popup != null)
                {
                    popup.OpenWindow(BuildDerivedPopupWindow(unit, baseTool, basePlan, isEnemyPhase));
                    popup.SetAnchor(ResolveChainAnchor(unit));
                    UpdateChainPopupStage(popup, "W4.5", options, true);
                    popupOpened = true;
                }

                bool resolved = false;
                while (!resolved)
                {
                    if (_pendingEndTurn)
                    {
                        Log("[Chain] DerivedPromptAbort(cancel-endturn)");
                        resolved = true;
                        break;
                    }

                    if (popupOpened)
                    {
                        if (popup.TryConsumeSkip())
                        {
                            Log("[Chain] DerivedPromptAbort(cancel)");
                            resolved = true;
                            break;
                        }

                        if (popup.TryConsumeSelection(out int selectedIndex))
                        {
                            if (selectedIndex >= 0 && selectedIndex < options.Count)
                            {
                                var option = options[selectedIndex];
                                Log($"[Derived] Select(id={option.tool.Id}, kind={option.kind})");
                                ChainQueueOutcome outcome = default;
                                yield return TryQueueDerivedSelection(option, unit, basePlan.kind, budget, resources, derivedQueue, result => outcome = result);

                                if (outcome.cancel)
                                {
                                    Log("[Chain] DerivedPromptAbort(cancel)");
                                    resolved = true;
                                }
                                else if (outcome.queued)
                                {
                                    resolved = true;
                                }

                                if (!resolved && popupOpened)
                                    UpdateChainPopupStage(popup, "W4.5", options, true);

                                continue;
                            }
                        }
                    }

                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                    {
                        Log("[Chain] DerivedPromptAbort(cancel)");
                        resolved = true;
                        break;
                    }

                    bool handled = false;
                    for (int i = 0; i < options.Count; i++)
                    {
                        var option = options[i];
                        if (option.key != KeyCode.None && Input.GetKeyDown(option.key))
                        {
                            handled = true;
                            Log($"[Derived] Select(id={option.tool.Id}, kind={option.kind})");
                            ChainQueueOutcome outcome = default;
                            yield return TryQueueDerivedSelection(option, unit, basePlan.kind, budget, resources, derivedQueue, result => outcome = result);

                            if (outcome.cancel)
                            {
                                Log("[Chain] DerivedPromptAbort(cancel)");
                                resolved = true;
                            }
                            else if (outcome.queued)
                            {
                                resolved = true;
                            }
                            break;
                        }
                    }

                    if (resolved)
                        break;

                    if (!handled)
                        yield return null;
                    else
                        yield return null;
                }

                options.Clear();
            }
            finally
            {
                if (popupOpened && popup != null)
                    popup.CloseWindow();

                PopInputSuppression();
                if (_chainWindowDepth > 0)
                    _chainWindowDepth--;
            }
        }

        IEnumerator TryQueueDerivedSelection(ChainOption option, Unit unit, string baseId, ITurnBudget budget, IResourcePool resources, List<Tuple<IActionToolV2, ActionPlan>> derivedQueue, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (tool == null)
            {
                onComplete?.Invoke(default);
                yield break;
            }

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            tool.OnEnterAim();
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");

            if (tool is ChainTestActionBase chainTool)
            {
                bool awaitingSelection = true;
                targetChosen = false;
                var cursor = ChainCursor;
                cursor?.Clear();
                Hex? lastHover = null;

                while (awaitingSelection)
                {
                    if (_pendingEndTurn)
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel", "(reason=EndTurn)");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    var hover = PickHexUnderMouse();
                    if (hover.HasValue)
                    {
                        if (!lastHover.HasValue || !lastHover.Value.Equals(hover.Value))
                        {
                            chainTool.OnHover(hover.Value);
                            lastHover = hover.Value;
                        }
                        var check = chainTool.ValidateTarget(unit, hover.Value);

                        if (Input.GetMouseButtonDown(0))
                        {
                            if (check.ok)
                            {
                                selectedTarget = hover.Value;
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetOk", $"(id={tool.Id}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetInvalid", $"(id={tool.Id}, reason={check.reason})");
                            }
                        }
                    }
                    else
                    {
                        cursor?.Clear();
                        lastHover = null;
                    }

                    if (!awaitingSelection)
                        break;

                    if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(unit, tool.Id, "W1_AimCancel");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    yield return null;
                }

                cursor?.Clear();

                if (!targetChosen)
                {
                    tool.OnExitAim();
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }

            tool.OnExitAim();
            ChainCursor?.Clear();

            ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmStart");
            ActionPhaseLogger.Log(unit, tool.Id, "W2_PrecheckOk");

            if (!targetChosen)
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            string failReason = EvaluateBudgetFailure(unit, option.secs, option.energy, budget, resources);

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, tool.Id, "W2_ConfirmAbort", $"(reason={failReason})");
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            ActionPhaseLogger.Log(unit, tool.Id, "W2_PreDeductCheckOk");

            int timeBefore = budget != null ? budget.Remaining : 0;
            int energyBefore = resources != null ? resources.Get("Energy") : 0;
            Log($"[Gate] W2' PreDeduct planSecs={option.secs}, planEnergy={option.energy} before=Time:{timeBefore}/Energy:{energyBefore}");

            if (budget != null && option.secs > 0)
                budget.SpendTime(option.secs);

            if (resources != null && option.energy > 0)
                resources.Spend("Energy", option.energy, "PreDeduct_Derived");

            var plan = new PreDeduct
            {
                secs = option.secs,
                energyMove = option.energy,
                energyAtk = 0,
                valid = true
            };

            ApplyBonusPreDeduct(unit, ref plan);

            _planStack.Push(plan);

            var planCost = new PlannedCost
            {
                moveSecs = option.secs,
                atkSecs = 0,
                moveEnergy = option.energy,
                atkEnergy = 0,
                valid = true
            };

            var actionPlan = new ActionPlan
            {
                kind = tool.Id,
                target = selectedTarget,
                cost = planCost
            };

            derivedQueue.Add(Tuple.Create(tool, actionPlan));

            _queuedActionsPending++;

            ChainCursor?.Clear();

            onComplete?.Invoke(new ChainQueueOutcome { queued = true, cancel = false, tool = option.tool });
        }

        IEnumerator TryQueueChainSelection(ChainOption option, Unit baseUnit, string baseKind, string stageLabel, List<ChainQueuedAction> pendingActions, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (tool == null)
            {
                onComplete?.Invoke(default);
                yield break;
            }

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            var owner = option.owner;
            if (owner == null)
                owner = ResolveUnit(tool);

            tool.OnEnterAim();
            ActionPhaseLogger.Log(owner, tool.Id, "W1_AimBegin");

            if (tool is ChainTestActionBase chainTool)
            {
                bool awaitingSelection = true;
                targetChosen = false;
                var cursor = ChainCursor;
                cursor?.Clear();
                Hex? lastHover = null;

                while (awaitingSelection)
                {
                    if (_pendingEndTurn)
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(owner, tool.Id, "W1_AimCancel", "(reason=EndTurn)");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    var hover = PickHexUnderMouse();
                    if (hover.HasValue)
                    {
                        if (!lastHover.HasValue || !lastHover.Value.Equals(hover.Value))
                        {
                            chainTool.OnHover(hover.Value);
                            lastHover = hover.Value;
                        }
                        var check = chainTool.ValidateTarget(owner, hover.Value);

                        if (Input.GetMouseButtonDown(0))
                        {
                            if (check.ok)
                            {
                                selectedTarget = hover.Value;
                                ActionPhaseLogger.Log(baseUnit, baseKind, $"{stageLabel} TargetOk", $"(id={tool.Id}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(baseUnit, baseKind, $"{stageLabel} TargetInvalid", $"(id={tool.Id}, reason={check.reason})");
                            }
                        }
                    }
                    else
                    {
                        cursor?.Clear();
                        lastHover = null;
                    }

                    if (!awaitingSelection)
                        break;

                    if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                    {
                        cursor?.Clear();
                        ActionPhaseLogger.Log(owner, tool.Id, "W1_AimCancel");
                        tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    yield return null;
                }

                cursor?.Clear();

                if (!targetChosen)
                {
                    tool.OnExitAim();
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }

            tool.OnExitAim();
            ChainCursor?.Clear();

            ActionPhaseLogger.Log(owner, tool.Id, "W2_ConfirmStart");
            ActionPhaseLogger.Log(owner, tool.Id, "W2_PrecheckOk");

            if (!targetChosen)
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            var budget = option.budget;
            var resources = option.resources;
            string failReason = EvaluateBudgetFailure(owner, option.secs, option.energy, budget, resources);

            if (failReason != null)
            {
                ActionPhaseLogger.Log(owner, tool.Id, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(owner, tool.Id, "W2_ConfirmAbort", $"(reason={failReason})");
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            ActionPhaseLogger.Log(owner, tool.Id, "W2_PreDeductCheckOk");

            if (budget != null && option.secs > 0)
                budget.SpendTime(option.secs);

            if (resources != null && option.energy > 0)
            {
                string reason = $"PreDeduct_{option.kind}";
                resources.Spend("Energy", option.energy, reason);
            }

            var plan = new PreDeduct
            {
                secs = option.secs,
                energyMove = option.energy,
                energyAtk = 0,
                valid = true
            };

            ApplyBonusPreDeduct(owner, ref plan);

            _planStack.Push(plan);

            var planCost = new PlannedCost
            {
                moveSecs = option.secs,
                atkSecs = 0,
                moveEnergy = option.energy,
                atkEnergy = 0,
                valid = true
            };

            var actionPlan = new ActionPlan
            {
                kind = tool.Id,
                target = selectedTarget,
                cost = planCost
            };

            pendingActions.Add(new ChainQueuedAction
            {
                tool = tool,
                owner = owner,
                plan = actionPlan,
                budget = budget,
                resources = resources
            });

            _queuedActionsPending++;

            ChainCursor?.Clear();

            onComplete?.Invoke(new ChainQueueOutcome { queued = true, cancel = false, tool = option.tool });
        }
        static string MapAimReason(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "notReady";
            raw = raw.ToLowerInvariant();
            if (raw.Contains("no-time"))
                return "lackTime";
            if (raw.Contains("no-energy"))
                return "lackEnergy";
            if (raw.Contains("cooldown"))
                return "cooldown";
            if (raw.Contains("fullround"))
                return "fullRound";
            if (raw.Contains("prepaid"))
                return "prepaid";
            if (raw.Contains("timespent"))
                return "timeSpent";
            return "notReady";
        }

        bool IsCooldownReadyForConfirm(IActionToolV2 tool, ICooldownSink sink)
        {
            if (sink == null) return true;
            string skillId = null;

            if (tool is HexClickMover mover && mover.config != null && !string.IsNullOrEmpty(mover.config.actionId))
                skillId = mover.config.actionId;
            else if (tool is AttackControllerV2 attack && attack.attackConfig != null)
                skillId = attack.attackConfig.name;
            else if (tool is ChainTestActionBase chain)
                skillId = chain.CooldownId;

            if (string.IsNullOrEmpty(skillId))
                skillId = tool?.Id;

            if (string.IsNullOrEmpty(skillId))
                return true;

            return sink.Ready(skillId);
        }

        bool IsAnyToolBusy()
        {
            foreach (var mb in tools)
            {
                switch (mb)
                {
                    case HexClickMover mover when mover.IsBusy:
                        return true;
                    case AttackControllerV2 attack when attack.IsBusy:
                        return true;
                }
            }
            return false;
        }

        Hex? PickHexUnderMouse()
        {
            var cam = pickCamera ? pickCamera : Camera.main;
            if (!cam || authoring?.Layout == null) return null;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, rayMaxDistance, pickMask))
            {
                float t = (pickPlaneY - ray.origin.y) / ray.direction.y;
                if (t < 0) return null;
                hit.point = ray.origin + ray.direction * t;
            }

            var hex = authoring.Layout.HexAt(hit.point);
            return hex;
        }

        List<Unit> BuildOrderedSideUnits(bool isPlayerSide)
        {
            if (turnManager == null)
                return null;

            var source = turnManager.GetSideUnits(isPlayerSide);
            if (source == null || source.Count == 0)
                return null;

            var ordered = new List<Unit>(source.Count);
            foreach (var unit in source)
            {
                if (unit == null)
                    continue;
                ordered.Add(unit);
            }

            if (ordered.Count <= 1)
                return ordered;

            ordered.Sort((a, b) =>
            {
                int ia = turnManager.GetTurnOrderIndex(a, isPlayerSide);
                int ib = turnManager.GetTurnOrderIndex(b, isPlayerSide);

                if (ia == ib)
                {
                    string la = TurnManagerV2.FormatUnitLabel(a);
                    string lb = TurnManagerV2.FormatUnitLabel(b);
                    return string.Compare(la, lb, StringComparison.Ordinal);
                }

                if (ia == int.MaxValue)
                    return 1;
                if (ib == int.MaxValue)
                    return -1;

                return ia.CompareTo(ib);
            });

            return ordered;
        }

        int PreviewStartFreeChainOptions(Unit unit, ITurnBudget budget, IResourcePool resources)
        {
            if (unit == null)
                return 0;

            var cooldowns = turnManager != null ? turnManager.GetCooldowns(unit) : null;
            var rules = ResolveRules();
            var allowed = rules?.AllowedAtPhaseStartFree();
            if (allowed == null || allowed.Count == 0)
                return 0;
            bool enemyPhase = turnManager != null && turnManager.IsEnemyUnit(unit);
            var options = BuildChainOptions(unit, budget, resources, 0, allowed,
                cooldowns, null, enemyPhase, /*restrictToOwner:*/ enemyPhase);
            int count = options != null ? options.Count : 0;
            options?.Clear();
            return count;
        }

        void LogPhaseStartPreview(string unitLabel, string phaseKind, int count)
        {
            Log($"[Free] {phaseKind} W2.1 (count={count}) unit={unitLabel}");
        }

        IEnumerator HandlePhaseStartGate(bool isPlayerPhase)
        {
            if (turnManager == null)
                yield break;

            if (isPlayerPhase)
            {
                // Player units handle their own free windows on TurnStart.
                yield break;
            }

            if (skipPhaseStartFreeChain)
            {
                var friendlies = BuildOrderedSideUnits(true);
                if (friendlies != null)
                {
                    foreach (var unit in friendlies)
                    {
                        if (unit == null)
                            continue;
                        Log($"[Free] PhaseStart(Enemy) freeskip unit={TurnManagerV2.FormatUnitLabel(unit)}");
                    }
                }
            }
        }

        IEnumerator HandleTurnStartGate(Unit unit)
        {
            if (turnManager == null || unit == null)
                yield break;

            bool isPlayerUnit = turnManager.IsPlayerUnit(unit);
            bool isEnemyUnit = turnManager.IsEnemyUnit(unit);

            if (!isPlayerUnit && !isEnemyUnit)
                yield break;

            if (isPlayerUnit)
            {
                if (unitDriver != null && unitDriver.UnitRef != unit)
                    yield break;

                if (skipPhaseStartFreeChain)
                {
                    Log($"[Free] PhaseStart(P1) freeskip unit={TurnManagerV2.FormatUnitLabel(unit)}");
                    yield break;
                }

                yield return RunStartFreeChainWindow(unit, "PhaseStart(P1)", false);
                yield break;
            }

            // 敌方分支：整批友方连锁期间加守卫，避免被 AutoFinishEnemyTurn 抢先结束
            if (unitDriver != null)
            {
                var driverUnit = unitDriver.UnitRef;
                if (driverUnit != null && turnManager.IsEnemyUnit(driverUnit))
                    yield break;
            }

            if (skipPhaseStartFreeChain)
            {
                var friendlies = BuildOrderedSideUnits(true);
                if (friendlies != null)
                {
                    foreach (var friendly in friendlies)
                    {
                        if (friendly == null) continue;
                        Log($"[Free] PhaseStart(Enemy) freeskip unit={TurnManagerV2.FormatUnitLabel(friendly)}");
                    }
                }
                yield break;
            }

            turnManager.PushAutoTurnEndGuard(unit);   // <<< 开守卫（整批）
            try
            {
                var orderedFriendlies = BuildOrderedSideUnits(true);
                if (orderedFriendlies != null)
                {
                    foreach (var friendly in orderedFriendlies)
                    {
                        if (friendly == null) continue;
                        // 敌方回合：依次为每个友方打开一次自由连锁窗口
                        yield return RunStartFreeChainWindow(friendly, "PhaseStart(Enemy)", true);
                    }
                }
            }
            finally
            {
                turnManager.PopAutoTurnEndGuard(unit); // <<< 全部处理完再关守卫
            }

            yield break;
        }

        IEnumerator RunStartFreeChainWindow(Unit unit, string planKind, bool isEnemyPhase)
        {
            if (unit == null || turnManager == null)
                yield break;

            var budget = turnManager.GetBudget(unit);
            var resources = turnManager.GetResources(unit);

            _planStack.Clear();

            if (turnManager.HasActiveFullRound(unit))
            {
                Log($"[FullRound] {planKind} skip unit={TurnManagerV2.FormatUnitLabel(unit)} reason=fullround");
                yield break;
            }

            var pendingChain = new List<ChainQueuedAction>();
            bool cancelBase = false;

            var phasePlan = new ActionPlan
            {
                kind = planKind,
                target = Hex.Zero,
                cost = new PlannedCost { valid = true }
            };

            var cooldowns = turnManager.GetCooldowns(unit);

            // 你项目里的 RunChainWindow 如果带 “restrictToOwner”等布尔尾参，保持原来调用方式
            yield return RunChainWindow(
                unit, phasePlan, ActionKind.Free, isEnemyPhase,
                budget, resources, cooldowns, 0,
                pendingChain,
                cancelled => cancelBase = cancelled,
                isEnemyPhase ? true : false, // 若你方法签名没有这个参数，就删掉这一位
                isEnemyPhase
            );

            if (cancelBase)
            {
                _planStack.Clear();
                yield break;
            }

            for (int i = pendingChain.Count - 1; i >= 0; --i)
            {
                var pending = pendingChain[i];
                if (pending.tool != null)
                {
                    if (_queuedActionsPending > 0)
                        _queuedActionsPending--;
                    yield return ExecuteAndResolve(pending.tool, pending.owner ?? unit, pending.plan, pending.budget, pending.resources);
                }
            }

            _planStack.Clear();
            TryFinalizeEndTurn();
        }


    }
}