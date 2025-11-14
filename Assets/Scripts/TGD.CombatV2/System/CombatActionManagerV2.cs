using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.CoreV2.Rules;
using TGD.DataV2;
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
        [Header("Factory Mode")]
        public bool useFactoryMode = false;

        public event Action<Unit> ChainFocusChanged;

        [SerializeField]
        [Tooltip("Only one CAM should register phase/turn gates in a scene.")]
        bool registerAsGateHub = true;

        [Header("Tools (managed automatically by UnitFactory)")]
        [SerializeField, HideInInspector]
        List<MonoBehaviour> tools = new();

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
        [Tooltip("Enables verbose binder/chain/rulebook diagnostics when Debug Log is also on.")]
        public bool advancedDebugLog = false;
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

        readonly Dictionary<string, List<IActionToolV2>> _toolsById = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<IActionToolV2, DestroySubscription> _destroySubscriptions = new();
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
        int _chainDepth;
        readonly Dictionary<UnitRuntimeContext, AttackControllerV2> _attackControllerCache = new();
        bool _ownsGateHub;
        int _bonusPlanDepth = -1;
        Unit _activeUnit;
        UnitRuntimeContext _activeCtx;
        readonly HashSet<DerivedScopeKey> _derivedScopes = new();
        int _actionTokenSequence;
        int _currentActionToken;
        bool _auditedGlobalTools;

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
            public string skillId;
            public int chainDepth;
            public bool ruleOverride;
        }

        struct ActionPlan
        {
            public string kind;
            public Hex target;
            public PlannedCost cost;
            public int chainDepth;
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

        readonly struct ChainOptionDedupKey : IEquatable<ChainOptionDedupKey>
        {
            public readonly string ownerId;
            public readonly string toolId;
            public readonly ActionKind kind;
            public readonly Type toolType;

            public ChainOptionDedupKey(string ownerId, string toolId, ActionKind kind, Type toolType)
            {
                this.ownerId = ownerId ?? string.Empty;
                this.toolId = toolId ?? string.Empty;
                this.kind = kind;
                this.toolType = toolType;
            }

            public bool Equals(ChainOptionDedupKey other)
            {
                return ownerId == other.ownerId
                    && toolId == other.toolId
                    && kind == other.kind
                    && toolType == other.toolType;
            }

            public override bool Equals(object obj)
            {
                return obj is ChainOptionDedupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ownerId.GetHashCode();
                    hash = (hash * 397) ^ toolId.GetHashCode();
                    hash = (hash * 397) ^ (int)kind;
                    hash = (hash * 397) ^ (toolType != null ? toolType.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        struct ChainQueuedAction
        {
            public IActionToolV2 tool;
            public Unit owner;
            public ActionPlan plan;
            public ITurnBudget budget;
            public IResourcePool resources;
            public int depth;
        }

        struct DerivedQueuedAction
        {
            public IActionToolV2 tool;
            public ActionPlan plan;
            public ITurnBudget budget;
            public IResourcePool resources;
            public int depth;
        }

        enum DerivedCandidateSource
        {
            Tools
        }

        enum DerivedCandidateWhy
        {
            Ok,
            ToolInvalid,
            WrongKind,
            NullOwner,
            OwnerMismatch,
            NotLearned,
            CostInvalid,
            BudgetFail,
            OnCooldown
        }

        readonly struct DerivedScopeKey : IEquatable<DerivedScopeKey>
        {
            public readonly string ownerId;
            public readonly string baseId;
            public readonly int token;

            public DerivedScopeKey(string ownerId, string baseId, int token)
            {
                this.ownerId = ownerId ?? string.Empty;
                this.baseId = baseId ?? string.Empty;
                this.token = token;
            }

            public bool Equals(DerivedScopeKey other)
            {
                return ownerId == other.ownerId && baseId == other.baseId && token == other.token;
            }

            public override bool Equals(object obj)
            {
                return obj is DerivedScopeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ownerId.GetHashCode();
                    hash = (hash * 397) ^ baseId.GetHashCode();
                    hash = (hash * 397) ^ token;
                    return hash;
                }
            }
        }

        struct ChainQueueOutcome
        {
            public bool queued;
            public bool cancel;
            public IActionToolV2 tool;
        }

        struct DestroySubscription
        {
            public DestroyNotifier notifier;
            public Action handler;
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
        struct ChainOptionDebug
        {
            public string toolId;
            public ActionKind kind;
            public Unit owner;
            public string reason;
            public int secs;
            public int energy;
            public KeyCode key;
        }

        readonly List<ChainOptionDebug> _chainDiagnostics = new();
        readonly Dictionary<string, string> _chainDiagLast = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _chainPromptLast = new(StringComparer.Ordinal);
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
        // =======================
        // Combo & Pending-Attack 还原块（必需）
        // =======================

        // 本回合已累计的普攻次数（用于连击倍率/能量/动画段位）
        int _attacksThisTurn = 0;

        // 用于在动画层回调（StrikeFired/AnimationEnded）之间临时挂起一次攻击态
        struct PendingAttack
        {
            public bool active;
            public bool strikeProcessed;
            public Unit unit;
            public Hex target;
            public int comboIndex;
        }
        PendingAttack _pendingAttack;

        // 至少1、最多4段（你之前的规则），基于 _attacksThisTurn 计算当前段位
        int ResolveComboIndex()
        {
            return Mathf.Clamp(Mathf.Max(1, _attacksThisTurn), 1, 4);
        }

        // 清理一次挂起的攻击态（在动画结束、移动结束、禁用时都会调用）
        void ClearPendingAttack()
        {
            _pendingAttack.active = false;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = null;
            _pendingAttack.target = default;
            _pendingAttack.comboIndex = 0;
        }

        // 触发一次攻击动画：挂起待击打态，并发事件给动画系统
        void TriggerAttackAnimation(Unit unit, Hex target)
        {
            if (unit == null) return;

            int comboIndex = ResolveComboIndex();

            // 先清空旧的待击打态
            ClearPendingAttack();

            // 挂起本次
            _pendingAttack.active = true;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = unit;
            _pendingAttack.target = target;
            _pendingAttack.comboIndex = comboIndex;

            // 通知动画层（你的动画层会在合适时机 RaiseAttackStrikeFired / RaiseAttackAnimationEnded）
            AttackEventsV2.RaiseAttackAnimation(unit, comboIndex);
        }

        void DiscardRuleCostOverride(Unit unit, PreDeduct preDeduct)
        {
            if (!preDeduct.ruleOverride)
                return;

            if (string.IsNullOrEmpty(preDeduct.skillId))
                return;

            var context = (turnManager != null && unit != null) ? turnManager.GetContext(unit) : null;
            context?.RuleLedger?.TryDiscardCost(preDeduct.skillId, preDeduct.chainDepth);
        }

        void RestorePreDeduct(Unit unit, PreDeduct preDeduct, ITurnBudget budget)
        {
            DiscardRuleCostOverride(unit, preDeduct);

            if (!preDeduct.valid)
                return;

            if (budget != null && preDeduct.secs > 0)
                budget.RefundTime(preDeduct.secs);

            if (IsBonusTurnFor(unit) && preDeduct.bonusSecs > 0)
            {
                int before = _bonusTurn.remaining;
                int after = Mathf.Min(_bonusTurn.cap, before + preDeduct.bonusSecs);
                if (after != before)
                {
                    _bonusTurn.remaining = after;
                    NotifyBonusTurnStateChanged();
                }
            }
        }

        void ClearPlanStack(Unit unit)
        {
            if (_planStack.Count == 0)
                return;

            foreach (var pending in _planStack)
                DiscardRuleCostOverride(unit, pending);
            _planStack.Clear();
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

            if (useFactoryMode)
                SetActiveUnit(unit);

            SetChainFocus(unit);
            NotifyBonusTurnStateChanged();
        }

        void EndBonusTurn(Unit unit)
        {
            if (!IsBonusTurnFor(unit))
                return;

            Unit nextActive = turnManager != null ? turnManager.ActiveUnit : null;

            _bonusTurn.Reset();
            NotifyBonusTurnStateChanged();

            if (useFactoryMode)
                SetActiveUnit(nextActive);

            SetChainFocus(nextActive);
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

        // Bootstrap all pre-wired tools. In factory mode this is where we stitch newly spawned
        // unit tools back into TMV2 (turn manager) and cursor services before the first turn runs.
        void Awake()
        {
            for (int i = tools.Count - 1; i >= 0; i--)
            {
                var mb = tools[i];
                if (Dead(mb))
                {
                    tools.RemoveAt(i);
                    continue;
                }

                WireTurnManager(mb);
                InjectCursorHighlighter(mb);

                if (mb is IActionToolV2 tool)
                    RegisterTool(tool);
            }
        }

        void WireTurnManager(MonoBehaviour mb)
        {
            if (Dead(mb)) return;
            switch (mb)
            {
                case AttackControllerV2 attack:
                    attack.AttachTurnManager(turnManager);
                    break;
                case HexClickMover mover:
                    mover.AttachTurnManager(turnManager);
                    break;
            }
        }

        void InjectCursorHighlighter(MonoBehaviour mb)
        {
            if (Dead(mb))
                return;

            if (mb is ICursorUser cursorUser)
            {
                var highlighter = mb is ChainActionBase ? EnsureChainHighlighter() : EnsureAimHighlighter();
                cursorUser.SetCursorHighlighter(highlighter);
            }
        }

        void EnterAimForTool(IActionToolV2 tool)
        {
            if (tool == null)
                return;

            if (tool is ICursorUser cursorUser)
            {
                var highlighter = tool is ChainActionBase ? EnsureChainHighlighter() : EnsureAimHighlighter();
                cursorUser.SetCursorHighlighter(highlighter);
            }

            tool.OnEnterAim();
        }

        public void RegisterTool(IActionToolV2 tool)
        {
            if (tool == null)
                return;

            var id = tool.Id;
            if (string.IsNullOrEmpty(id))
                return;

            if (!_toolsById.TryGetValue(id, out var list) || list == null)
            {
                list = new List<IActionToolV2>();
                _toolsById[id] = list;
            }

            if (!list.Contains(tool))
                list.Add(tool);

            if (tool is MonoBehaviour behaviour && !Dead(behaviour) && !tools.Contains(behaviour))
                tools.Add(behaviour);

            TrackDestroySubscription(tool);
        }

        public void UnregisterTool(IActionToolV2 tool)
        {
            if (tool == null)
                return;

            RemoveDestroySubscription(tool);
            RemoveToolLookup(tool);

            if (ReferenceEquals(_activeTool, tool))
                CancelCurrent("toolRemoved");

            if (tool is MonoBehaviour behaviour)
            {
                for (int i = tools.Count - 1; i >= 0; i--)
                {
                    var entry = tools[i];
                    if (Dead(entry) || ReferenceEquals(entry, behaviour))
                        tools.RemoveAt(i);
                }
            }
            else
            {
                for (int i = tools.Count - 1; i >= 0; i--)
                {
                    if (Dead(tools[i]))
                        tools.RemoveAt(i);
                }
            }
        }

        void TrackDestroySubscription(IActionToolV2 tool)
        {
            if (tool == null || _destroySubscriptions.ContainsKey(tool))
                return;

            if (tool is MonoBehaviour behaviour && !Dead(behaviour))
            {
                var notifier = behaviour.GetComponent<DestroyNotifier>() ?? behaviour.gameObject.AddComponent<DestroyNotifier>();
                Action handler = () => OnToolDestroyed(tool);
                notifier.OnDestroyed += handler;
                _destroySubscriptions[tool] = new DestroySubscription
                {
                    notifier = notifier,
                    handler = handler
                };
            }
        }

        void RemoveDestroySubscription(IActionToolV2 tool)
        {
            if (tool == null)
                return;

            if (_destroySubscriptions.TryGetValue(tool, out var sub))
            {
                if (sub.notifier != null)
                    sub.notifier.OnDestroyed -= sub.handler;
                _destroySubscriptions.Remove(tool);
            }
        }

        void RemoveToolLookup(IActionToolV2 tool)
        {
            if (tool == null)
                return;

            var id = tool.Id;
            if (string.IsNullOrEmpty(id))
                return;

            if (!_toolsById.TryGetValue(id, out var list) || list == null)
                return;

            list.Remove(tool);
            if (list.Count == 0)
                _toolsById.Remove(id);
        }

        void OnToolDestroyed(IActionToolV2 tool)
        {
            RemoveDestroySubscription(tool);
            RemoveToolLookup(tool);

            if (ReferenceEquals(_activeTool, tool))
                CancelCurrent("toolDestroyed");

            if (tool is MonoBehaviour behaviour)
            {
                for (int i = tools.Count - 1; i >= 0; i--)
                {
                    var entry = tools[i];
                    if (Dead(entry) || ReferenceEquals(entry, behaviour))
                        tools.RemoveAt(i);
                }
            }
            else
            {
                for (int i = tools.Count - 1; i >= 0; i--)
                {
                    if (Dead(tools[i]))
                        tools.RemoveAt(i);
                }
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

        // Main entry point for hooking into TMV2 lifecycle events. When running under the factory
        // pipeline we also claim the phase gate responsibilities so CAMV2 remains the single
        // authority on W1→W4 transitions.
        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            AuditGlobalTools();

            if (turnManager != null)
            {
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.SideEnded += OnSideEnded;
                if (useFactoryMode)
                {
                    turnManager.TurnStarted += HandleFactoryTurnStarted;
                    turnManager.TurnEnded += HandleFactoryTurnEnded;
                    SetActiveUnit(turnManager.ActiveUnit);
                }
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

        // Mirror OnEnable tear-down to avoid double subscriptions when scenes hot-reload or the
        // factory swaps managers mid-play. Active unit focus is cleared so new spawns can rebind.
        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            if (turnManager != null)
            {
                if (useFactoryMode)
                {
                    turnManager.TurnStarted -= HandleFactoryTurnStarted;
                    turnManager.TurnEnded -= HandleFactoryTurnEnded;
                }
                turnManager.TurnStarted -= OnTurnStarted;
                turnManager.SideEnded -= OnSideEnded;
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
            if (useFactoryMode)
                SetActiveUnit(null);
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

        void AuditGlobalTools()
        {
            if (_auditedGlobalTools)
                return;
            if (!debugLog)
                return;
            if (!Application.isPlaying)
                return;

            var behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is not IActionToolV2)
                    continue;

                var go = behaviour.gameObject;
                if (go == null)
                    continue;

                var scene = go.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                var context = behaviour.GetComponentInParent<UnitRuntimeContext>(true);
                if (context != null)
                    continue;

                string path = GetHierarchyPath(go);
                Debug.LogWarning($"[Guard] Global IActionToolV2 found outside Unit: {behaviour.name} @ {path}", behaviour);
            }

            _auditedGlobalTools = true;
        }

        // Factory hook: TMV2 drives the active unit selection and we mirror it locally so every
        // action tool knows which UnitRuntimeContext to bind against for this turn.
        void HandleFactoryTurnStarted(Unit unit)
        {
            if (!useFactoryMode)
                return;

            SetActiveUnit(unit);
        }

        // Factory hook: drop ownership as soon as TMV2 signals turn end to prevent late callbacks
        // from mutating the wrong context when the factory schedules the next unit.
        void HandleFactoryTurnEnded(Unit unit)
        {
            if (!useFactoryMode)
                return;

            if (unit != null && unit == _activeUnit)
                SetActiveUnit(null);
        }

        // Factory-only: updates the active unit/context reference, rebinds tools, and recenters the
        // camera so designers just need to spawn via UnitFactory and CAMV2 will drive the UI.
        public void SetActiveUnit(Unit unit)
        {
            if (!useFactoryMode)
                return;

            if (_activeUnit == unit)
                return;

            _activeUnit = unit;
            _activeCtx = unit != null && turnManager != null ? turnManager.GetContext(unit) : null;

            RebindToolsFor(_activeCtx);
            FocusCameraOn(unit);
        }

        // Re-evaluate every registered tool against the provided context. Ensures that freshly
        // spawned units (or context swaps) only have the actions they have learned enabled.
        public void RebindToolsFor(UnitRuntimeContext context)
        {
            if (!useFactoryMode)
                return;

            foreach (var pair in _toolsById)
            {
                var list = pair.Value;
                if (list == null)
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var tool = list[i];
                    if (tool == null)
                        continue;

                    var ownerCtx = ResolveToolContext(tool);
                    if (ownerCtx == null)
                        ownerCtx = context;

                    if (ownerCtx != null && tool is IBindContext binder)
                        binder.BindContext(ownerCtx, turnManager);

                    if (tool is MonoBehaviour behaviour && behaviour != null)
                    {
                        bool enabled = ownerCtx != null && IsActionLearned(ownerCtx, pair.Key);
                        behaviour.enabled = enabled;
                    }
                }
            }
        }

        UnitRuntimeContext ResolveToolContext(IActionToolV2 tool)
        {
            if (tool is Component component && !Dead(component))
            {
                var ctx = component.GetComponentInParent<UnitRuntimeContext>(true);
                if (ctx != null)
                    return ctx;
            }

            if (tool is TGD.CoreV2.IToolOwner owner && owner.Ctx != null)
                return owner.Ctx;

            return null;
        }

        bool IsActionLearned(UnitRuntimeContext context, string skillId)
        {
            if (context == null)
                return false;

            var learned = context.LearnedActions;
            if (learned == null || learned.Count == 0)
                return false;

            string normalized = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(normalized))
                return false;

            for (int i = 0; i < learned.Count; i++)
            {
                string entry = NormalizeSkillId(learned[i]);
                if (string.IsNullOrEmpty(entry))
                    continue;

                if (string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Central gate used by UI/input layers to know if player commands are accepted. Factory
        // integration makes sure we only return true while TMV2 grants priority to this unit.
        public bool PlayerCanActNow()
        {
            if (!useFactoryMode)
                return true;

            if (!isActiveAndEnabled)
                return false;

            if (_activeUnit == null || _activeCtx == null || turnManager == null)
                return false;

            if (!IsEffectivePlayerPhase(_activeUnit))
                return false;

            if (IsPendingEndTurnFor(_activeUnit))
                return false;

            return true;
        }

        // Lightweight helper so the factory spawn flow can immediately orient the pick camera to
        // the currently acting unit without bespoke cutscene logic.
        public void FocusCameraOn(Unit unit)
        {
            if (!useFactoryMode)
                return;

            if (unit == null || _activeCtx == null)
                return;

            var cam = pickCamera != null ? pickCamera : Camera.main;
            if (cam == null)
                return;

            var target = _activeCtx.transform != null ? _activeCtx.transform.position : Vector3.zero;
            var delta = target - cam.transform.position;
            if (delta.sqrMagnitude > 0.01f)
                cam.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        // Sync point with TMV2: caches the active unit, aborts stale tools, and clears pending
        // auto-end requests. This is effectively CAMV2's transition into W1 for the new actor.
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

        void OnSideEnded(bool isPlayerSide)
        {
            if (turnManager == null)
                return;

            string phaseLabel = isPlayerSide ? "Player" : "Enemy";
            Debug.Log($"[Attack] ComboReset T{turnManager.CurrentPhaseIndex}({phaseLabel}) AttackCombo Reset!", this);
        }

        UnitRuntimeContext ResolveContext(Unit unit)
        {
            if (unit != null && turnManager != null)
                return turnManager.GetContext(unit);
            return _activeCtx;
        }

        bool IsToolGrantedForContext(UnitRuntimeContext ctx, IActionToolV2 tool)
        {
            if (tool == null)
                return false;

            if (tool is MonoBehaviour behaviour)
            {
                if (Dead(behaviour) || !behaviour.isActiveAndEnabled)
                    return false;
            }
            else if (tool is UnityEngine.Object unityObj && Dead(unityObj))
            {
                return false;
            }

            var grants = ctx?.GrantedSkillIds;
            if (grants != null && grants.Count > 0)
            {
                string key = (tool as ICooldownKeyProvider)?.CooldownKey;
                if (IsSkillGrantedIncludingDerived(grants, key))
                    return true;

                string idValue = tool.Id;
                if (IsSkillGrantedIncludingDerived(grants, idValue))
                    return true;

                return false;
            }

            return true;
        }

        IActionToolV2 SelectTool(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            PruneDeadTools();
            if (_activeCtx != null)
            {
                var ctxGo = _activeCtx.gameObject;
                if (ctxGo != null)
                {
                    var scopedTools = ctxGo.GetComponentsInChildren<IActionToolV2>(true);
                    for (int i = 0; i < scopedTools.Length; i++)
                    {
                        var scoped = scopedTools[i];
                        if (scoped != null && scoped.Id == id)
                        {
                            if (!IsToolGrantedForContext(_activeCtx, scoped))
                                continue;
                            return scoped;
                        }
                    }
                }
            }

            if (!_toolsById.TryGetValue(id, out var list) || list == null || list.Count == 0)
            {
                list = ResolveToolsForId(id);
                if (list == null || list.Count == 0)
                    return null;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var candidate = list[i];
                if (candidate == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                if (candidate is UnityEngine.Object unityObj && Dead(unityObj))
                {
                    list.RemoveAt(i);
                    continue;
                }
            }

            if (list.Count == 0)
                return null;

            if (useFactoryMode)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var tool = list[i];
                    if (!IsToolGrantedForContext(_activeCtx, tool))
                        continue;

                    if (tool is MonoBehaviour behaviour && !Dead(behaviour) && behaviour.isActiveAndEnabled)
                        return tool;
                }

                return null;
            }

            foreach (var tool in list)
            {
                if (tool != null && ResolveUnit(tool) == _currentUnit && IsToolGrantedForContext(ResolveContext(_currentUnit), tool))
                    return tool;
            }

            return null;
        }

        void HandleIdleKeybind(KeyCode key, string toolId)
        {
            if (!PlayerCanActNow())
                return;

            if (key == KeyCode.None || string.IsNullOrEmpty(toolId))
                return;

            if (Input.GetKeyDown(key))
                TryRequestIdleAction(toolId);
        }

        void TryRequestIdleAction(string toolId)
        {
            if (!PlayerCanActNow())
                return;

            if (_pendingEndTurn)
                return;

            var selected = SelectTool(toolId);
            if (!TryResolveAliveTool(selected, out var tool))
                return;

            if (!CanActivateAtIdle(tool))
                return;

            RequestAim(toolId);
        }

        bool CanActivateAtIdle(IActionToolV2 tool)
        {
            if (!TryResolveAliveTool(tool, out tool))
                return false;
            var owner = useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(tool);

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
            if (_pendingEndTurn)
                TryFinalizeEndTurn();

            if (!PlayerCanActNow())
                return;

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
                if (!TryGetActiveTool(out var activeTool))
                    return;

                var h = PickHexUnderMouse();
                if (h.HasValue && (!_hover.HasValue || !_hover.Value.Equals(h.Value)))
                {
                    _hover = h;
                    activeTool.OnHover(h.Value);
                }

                if (Input.GetMouseButtonDown(0)) Confirm();
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) Cancel(true);
            }
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

        IEnumerator RunSustainedBonusTurns(int capSeconds, string skillId)
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
            var prevActiveUnit = useFactoryMode ? _activeUnit : null;

            _pendingEndTurn = false;
            _pendingEndTurnUnit = null;
            _bonusPlanDepth = Mathf.Max(0, _planStack.Count);

            try
            {
                foreach (var unit in snapshot)
                {
                    if (unit == null)
                        continue;

                    BeginBonusTurn(unit, capSeconds, skillId);

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
                if (useFactoryMode)
                    SetActiveUnit(prevActiveUnit);
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
            if (tool is Component component && !Dead(component))
            {
                var ctx = component.GetComponentInParent<UnitRuntimeContext>(true);
                if (ctx != null)
                    return ctx.boundUnit;
            }

            if (tool is TGD.CoreV2.IToolOwner own && own.Ctx != null)
                return own.Ctx.boundUnit;

            if (_activeCtx != null)
                return _activeCtx.boundUnit;

            return null;
        }

        public void RequestAim(string toolId)
        {
            if (!PlayerCanActNow())
                return;

            if (_pendingEndTurn)
                return;

            if (_phase != Phase.Idle) return;

            var tool = SelectTool(toolId);
            if (!TryResolveAliveTool(tool, out tool)) return;
            if (!CanActivateAtIdle(tool))
                return;
            if (IsExecuting || IsAnyToolBusy()) return;

            var unit = useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(tool);
            if (_currentUnit != null && unit != _currentUnit)
                return;

            if (!TryBeginAim(tool, unit, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(unit, tool.Id, "W1_AimReject", $"(reason={reason})");
                return;
            }

            if (TryResolveAliveTool(_activeTool, out var existing))
                CleanupAfterAbort(existing, false);
            else if (_activeTool != null)
                HandleLostActiveTool();

            _activeTool = tool;
            _hover = null;
            EnterAimForTool(tool);
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(unit, tool.Id, "W1_AimBegin");
        }

        public void Cancel(bool userInitiated = false)
        {
            if (userInitiated && !PlayerCanActNow())
                return;

            if (_phase != Phase.Aiming)
                return;

            if (!TryGetActiveTool(out var activeTool))
                return;

            CleanupAfterAbort(activeTool, userInitiated);
        }

        public void Confirm()
        {
            if (!PlayerCanActNow())
                return;

            if (_phase != Phase.Aiming)
                return;

            if (!TryGetActiveTool(out var activeTool))
                return;

            var unit = useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(activeTool);
            var hex = _hover ?? PickHexUnderMouse();

            StartCoroutine(ConfirmRoutine(activeTool, unit, hex));
        }

        public bool TryAutoExecuteAction(string toolId, Hex target)
        {
            return TryAutoExecuteActionInternal(null, toolId, target, true);
        }

        public bool TryAutoExecuteActionForUnit(Unit unit, string toolId, Hex target)
        {
            if (unit != null && IsBonusTurnFor(unit))
                return TryAutoExecuteActionInternal(unit, toolId, target, true);

            return TryAutoExecuteActionInternal(unit, toolId, target, false);
        }

        bool TryAutoExecuteActionInternal(Unit explicitOwner, string toolId, Hex target, bool requirePlayerPhase)
        {
            if (!isActiveAndEnabled)
                return false;

            if (_pendingEndTurn)
                return false;

            if (_phase != Phase.Idle)
                return false;

            var tool = SelectTool(toolId);
            if (!TryResolveAliveTool(tool, out tool))
                return false;

            var owner = explicitOwner ?? (useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(tool));
            if (owner == null)
                return false;

            if (requirePlayerPhase)
            {
                if (!PlayerCanActNow())
                    return false;

                if (_currentUnit != null && owner != _currentUnit)
                    return false;

                if (!CanActivateAtIdle(tool))
                    return false;
            }
            else
            {
                if (!CanEnemyAutoExecute(owner, tool))
                    return false;
            }

            if (IsExecuting || IsAnyToolBusy())
                return false;

            if (!TryBeginAim(tool, owner, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ActionPhaseLogger.Log(owner, tool.Id, "W1_AimReject", $"(reason={reason})");
                return false;
            }

            if (TryResolveAliveTool(_activeTool, out var existing))
                CleanupAfterAbort(existing, false);
            else if (_activeTool != null)
                HandleLostActiveTool();

            _activeTool = tool;
            _hover = target;
            EnterAimForTool(tool);
            _phase = Phase.Aiming;
            ActionPhaseLogger.Log(owner, tool.Id, "W1_AimBegin");

            StartCoroutine(ConfirmRoutine(tool, owner, target));
            return true;
        }

        bool CanEnemyAutoExecute(Unit owner, IActionToolV2 tool)
        {
            if (owner == null || turnManager == null)
                return false;

            if (_currentUnit != null && owner != _currentUnit)
                return false;

            if (!IsBonusTurnFor(owner))
            {
                if (!IsEffectiveEnemyPhase(owner))
                    return false;
                if (!turnManager.IsEnemyUnit(owner))
                    return false;
                if (turnManager.ActiveUnit != owner)
                    return false;
                if (!turnManager.HasReachedIdle(owner))
                    return false;
            }

            if (IsPendingEndTurnFor(owner))
                return false;

            return CanActivateAtIdle(tool);
        }

        void TryHideAllAimUI()
        {
            if (TryResolveAliveTool(_activeTool, out var active))
                active.OnExitAim();
            else if (_activeTool != null)
                _activeTool = null;

            foreach (var mb in tools)
            {
                if (Dead(mb) || ReferenceEquals(mb, _activeTool))
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
            ClearPlanStack(unit);
            _phase = Phase.Executing;
            string kind = tool.Id;
            Unit guardUnit = null;
            bool guardActive = false;
            bool ruleCostOverridden = false;
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

            int previousChainDepth = _chainDepth;
            _chainDepth = 0;
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
                    cost = BuildPlannedCost(tool, unit, target.Value),
                    chainDepth = _chainDepth
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

                {
                    var context = turnManager != null && unit != null ? turnManager.GetContext(unit) : null;
                    var set = context != null ? context.Rules : null;
                    bool? friendlyHint = null;
                    if (turnManager != null && unit != null)
                        friendlyHint = turnManager.IsPlayerUnit(unit);

                    var ctx2 = RulesAdapter.BuildContext(
                        context,
                        skillId: tool.Id,
                        kind: tool.Kind,
                        chainDepth: _chainDepth,
                        comboIndex: GetAttackComboCount(unit),
                        planSecs: actionPlan.cost.TotalSeconds,
                        planEnergy: actionPlan.cost.TotalEnergy,
                        unitIdHint: unit != null ? unit.Id : null,
                        isFriendlyHint: friendlyHint
                    );

                    int originalMoveSecs = actionPlan.cost.moveSecs;
                    int originalAtkSecs = actionPlan.cost.atkSecs;
                    int originalMoveEnergy = actionPlan.cost.moveEnergy;
                    int originalAtkEnergy = actionPlan.cost.atkEnergy;

                    int moveSecs = originalMoveSecs;
                    int atkSecs = originalAtkSecs;
                    int moveE = originalMoveEnergy;
                    int atkE = originalAtkEnergy;

                    RuleEngineV2.Instance.ApplyCostModifiers(set, in ctx2, ref moveSecs, ref atkSecs, ref moveE, ref atkE);

                    int finalMoveSecs = Mathf.Max(0, moveSecs);
                    int finalAtkSecs = Mathf.Max(0, atkSecs);
                    int finalMoveEnergy = Mathf.Max(0, moveE);
                    int finalAtkEnergy = Mathf.Max(0, atkE);

                    ruleCostOverridden = finalMoveSecs != originalMoveSecs
                    || finalAtkSecs != originalAtkSecs
                        || finalMoveEnergy != originalMoveEnergy
                        || finalAtkEnergy != originalAtkEnergy;

                    if (ruleCostOverridden)
                    {
                        int originalTotalSecs = Mathf.Max(0, originalMoveSecs + originalAtkSecs);
                        int finalTotalSecs = Mathf.Max(0, finalMoveSecs + finalAtkSecs);
                        int originalTotalEnergy = Mathf.Max(0, originalMoveEnergy + originalAtkEnergy);
                        int finalTotalEnergy = Mathf.Max(0, finalMoveEnergy + finalAtkEnergy);
                        ActionPhaseLogger.Log($"[Rules] Cost secs:{originalTotalSecs}->{finalTotalSecs}, energy:{originalTotalEnergy}->{finalTotalEnergy} (CostMods)");
                    }

                    actionPlan.cost.moveSecs = finalMoveSecs;
                    actionPlan.cost.atkSecs = finalAtkSecs;
                    actionPlan.cost.moveEnergy = finalMoveEnergy;
                    actionPlan.cost.atkEnergy = finalAtkEnergy;
                    cost = actionPlan.cost;

                    if (ruleCostOverridden && context != null)
                    {
                        var ledger = context.RuleLedger;
                        ledger?.RecordCost(new RuleCostApplication
                        {
                            skillId = tool.Id,
                            chainDepth = actionPlan.chainDepth,
                            originalMoveSecs = originalMoveSecs,
                            originalAtkSecs = originalAtkSecs,
                            originalMoveEnergy = originalMoveEnergy,
                            originalAtkEnergy = originalAtkEnergy,
                            finalMoveSecs = finalMoveSecs,
                            finalAtkSecs = finalAtkSecs,
                            finalMoveEnergy = finalMoveEnergy,
                            finalAtkEnergy = finalAtkEnergy
                        });
                    }
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
                    var uctx = turnManager != null && unit != null ? turnManager.GetContext(unit) : null;
                    CAM.RaiseActionCancelled(uctx, tool.Id, failReason);
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
                    valid = true,
                    skillId = tool.Id,
                    chainDepth = actionPlan.chainDepth,
                    ruleOverride = ruleCostOverridden
                };
                ApplyBonusPreDeduct(unit, ref basePreDeduct);
                _planStack.Push(basePreDeduct);

                TryHideAllAimUI();

                if (cooldowns != null)
                    TryStartCooldownIfAny(tool, unit, cooldowns, _chainDepth);

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
                    if (pending.tool == null)
                        continue;

                    if (!TryResolveAliveTool(pending.tool, out var alivePending))
                        continue;

                    if (_queuedActionsPending > 0)
                        _queuedActionsPending--;
                    yield return ExecuteAndResolve(alivePending, pending.owner ?? unit, pending.plan, pending.budget, pending.resources, pending.depth);
                }

                if (cancelBase)
                {
                    PreDeduct popped = default;
                    if (_planStack.Count > 0)
                        popped = _planStack.Pop();
                    DiscardRuleCostOverride(unit, popped);
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
                    ActionPhaseLogger.Log(unit, actionPlan.kind, "W2_ConfirmAbort", "(reason=LinkCancelled)");
                    CAM.RaiseActionCancelled(turnManager != null && unit != null ? turnManager.GetContext(unit) : null, tool.Id, "LinkCancelled");
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
                    {
                        var popped = _planStack.Pop();
                        DiscardRuleCostOverride(unit, popped);
                    }

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
                yield return ExecuteAndResolve(tool, unit, actionPlan, budget, resources, 0);
                TryFinalizeEndTurn();
            }
            finally
            {
                _chainDepth = previousChainDepth;
                if (guardActive && guardUnit != null)
                    turnManager.PopAutoTurnEndGuard(guardUnit);
            }

        }

        IEnumerator ExecuteAndResolve(IActionToolV2 tool, Unit unit, ActionPlan plan, ITurnBudget budget, IResourcePool resources, int chainDepth)
        {
            _endTurnGuardDepth++;
            int previousDepth = _chainDepth;
            _chainDepth = chainDepth;
            int parentToken = _currentActionToken;
            int actionToken = ++_actionTokenSequence;
            _currentActionToken = actionToken;
            try
            {
                _phase = Phase.Executing;
                _hover = null;
                TryHideAllAimUI();

                int budgetBefore = budget != null ? budget.Remaining : 0;
                int energyBefore = resources != null ? resources.Get("Energy") : 0;
                ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteBegin", $"(budgetBefore={budgetBefore}, energyBefore={energyBefore})");

                if (!TryResolveAliveTool(tool, out tool))
                {
                    HandleToolDestroyedDuringExecution(unit, plan, budget, "toolDestroyed");
                    yield break;
                }

                var routine = tool.OnConfirm(plan.target);
                if (routine != null)
                {
                    yield return StartCoroutine(routine);

                    if (!TryResolveAliveTool(tool, out tool))
                    {
                        HandleToolDestroyedDuringExecution(unit, plan, budget, "toolDestroyed");
                        yield break;
                    }
                }

                var report = BuildExecReport(tool, out var exec);
                if (!report.valid || exec == null)
                {
                    ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");
                    CleanupAfterAbort(tool, false);
                    yield break;
                }

                report = ApplyRuleCostOverrides(unit, tool, plan, report);

                LogExecSummary(unit, plan.kind, report);

                ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteEnd");

                yield return StartCoroutine(Resolve(tool, unit, plan, exec, report, budget, resources));
            }
            finally
            {
                ClearDerivedScopeForToken(actionToken);
                _currentActionToken = parentToken;
                _chainDepth = previousDepth;
                _endTurnGuardDepth = Mathf.Max(0, _endTurnGuardDepth - 1);
                TryFinalizeEndTurn();
            }
        }

        void HandleToolDestroyedDuringExecution(Unit unit, ActionPlan plan, ITurnBudget budget, string reason)
        {
            PreDeduct popped = default;
            if (_planStack.Count > 0)
                popped = _planStack.Pop();

            RestorePreDeduct(unit, popped, budget);

            string suffix = string.IsNullOrEmpty(reason) ? string.Empty : $"(reason={reason})";
            ActionPhaseLogger.Log(unit, plan.kind, "W3_ExecuteAbort", suffix);

            var ctx = (turnManager != null && unit != null) ? turnManager.GetContext(unit) : null;
            CAM.RaiseActionCancelled(ctx, plan.kind, string.IsNullOrEmpty(reason) ? "toolDestroyed" : reason);

            TryHideAllAimUI();
            _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;

            TryFinalizeEndTurn();
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

            IActionToolV2 aliveTool = TryResolveAliveTool(tool, out var usableTool) ? usableTool : null;
            string toolId = aliveTool != null ? aliveTool.Id : plan.kind;

            if (aliveTool is IActionResolveEffect resolveEffect)
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

            var derivedQueue = new List<DerivedQueuedAction>();
            var cooldowns = (turnManager != null && unit != null) ? turnManager.GetCooldowns(unit) : null;
            bool shouldDerived = ShouldRunDerivedWindow(unit, aliveTool, plan.kind);
            bool skipDerived = shouldDerived && _pendingEndTurn && IsPendingEndTurnFor(unit);
            if (shouldDerived && !skipDerived)
                yield return RunDerivedWindow(unit, aliveTool, plan, report, budget, resources, cooldowns, derivedQueue);
            else
            {
                if (shouldDerived && skipDerived)
                    Log($"[Chain] DerivedPromptAbort(from={plan.kind}, reason=EndTurn)");
                derivedQueue.Clear();
            }

            int budgetAfter = budget != null ? budget.Remaining : 0;
            int energyAfter = resources != null ? resources.Get("Energy") : 0;
            ActionPhaseLogger.Log(unit, plan.kind, "W4_ResolveEnd", $"(budgetAfter={budgetAfter}, energyAfter={energyAfter})");

            var resolvedContext = (turnManager != null && unit != null) ? turnManager.GetContext(unit) : null;
            CAM.RaiseActionResolved(resolvedContext, toolId);

            exec.Consume();
            _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;

            if (derivedQueue.Count > 0)
            {
                for (int i = 0; i < derivedQueue.Count; i++)
                {
                    var pending = derivedQueue[i];
                    if (pending.tool == null)
                        continue;

                    if (!TryResolveAliveTool(pending.tool, out var alivePending))
                        continue;

                    if (_queuedActionsPending > 0)
                        _queuedActionsPending--;
                    var pendingBudget = pending.budget ?? budget;
                    var pendingResources = pending.resources ?? resources;
                    yield return ExecuteAndResolve(alivePending, unit, pending.plan, pendingBudget, pendingResources, pending.depth);
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

        ExecReportData ApplyRuleCostOverrides(Unit unit, IActionToolV2 tool, ActionPlan plan, ExecReportData report)
        {
            if (tool == null)
                return report;

            var context = (turnManager != null && unit != null) ? turnManager.GetContext(unit) : null;
            var ledger = context?.RuleLedger;
            if (ledger == null)
                return report;

            if (!ledger.TryConsumeCost(tool.Id, plan.chainDepth, out var application))
                return report;

            bool secsChanged = false;
            bool energyChanged = false;

            int finalMoveSecs = Mathf.Max(0, application.finalMoveSecs);
            if (report.plannedSecsMove != finalMoveSecs)
            {
                report.plannedSecsMove = finalMoveSecs;
                secsChanged = true;
            }

            int finalAtkSecs = Mathf.Max(0, application.finalAtkSecs);
            if (report.plannedSecsAtk != finalAtkSecs)
            {
                report.plannedSecsAtk = finalAtkSecs;
                secsChanged = true;
            }

            report.refundedSecsMove = Mathf.Min(report.refundedSecsMove, report.plannedSecsMove);
            report.refundedSecsAtk = Mathf.Min(report.refundedSecsAtk, report.plannedSecsAtk);

            int finalMoveEnergy = Mathf.Max(0, application.finalMoveEnergy);
            if (report.energyMoveNet > finalMoveEnergy)
            {
                report.energyMoveNet = finalMoveEnergy;
                energyChanged = true;
            }

            int finalAtkEnergy = Mathf.Max(0, application.finalAtkEnergy);
            if (report.energyAtkNet > finalAtkEnergy)
            {
                report.energyAtkNet = finalAtkEnergy;
                energyChanged = true;
            }

            if (secsChanged || energyChanged)
            {
                int originalSecs = Mathf.Max(0, application.originalMoveSecs + application.originalAtkSecs);
                int finalSecs = Mathf.Max(0, application.finalMoveSecs + application.finalAtkSecs);
                int originalEnergy = Mathf.Max(0, application.originalMoveEnergy + application.originalAtkEnergy);
                int finalEnergy = Mathf.Max(0, application.finalMoveEnergy + application.finalAtkEnergy);
                ActionPhaseLogger.Log($"[Rules] Exec adjust {tool.Id}: secs {originalSecs}->{finalSecs}, energy {originalEnergy}->{finalEnergy} (CostMods)");
            }

            return report;
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

        public bool AdvancedDebugLogsEnabled => debugLog && advancedDebugLog;

        void ChainDebugLog(string message)
        {
            if (!AdvancedDebugLogsEnabled)
                return;
            Debug.Log(message, this);
        }

        void CleanupAfterAbort(IActionToolV2 tool, bool logCancel)
        {
            if (tool == null) return;

            bool alive = TryResolveAliveTool(tool, out var usable);
            Unit unit = alive ? ResolveUnit(usable) : null;

            if (logCancel && _phase == Phase.Aiming && alive)
            {
                ActionPhaseLogger.Log(unit, usable.Id, "W1_AimCancel");
            }

            TryHideAllAimUI();
            if (_activeTool == tool)
                _activeTool = null;
            _hover = null;
            _phase = Phase.Idle;
            ClearPlanStack(unit);
            TryFinalizeEndTurn();
        }

        void CancelCurrent(string reason)
        {
            var current = _activeTool;
            if (current == null)
                return;

            var mapped = string.IsNullOrEmpty(reason) ? "toolDestroyed" : reason;

            if (_phase == Phase.Aiming)
            {
                if (TryResolveAliveTool(current, out var alive))
                {
                    var unit = useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(alive);
                    NotifyConfirmAbort(alive, unit, mapped);
                }

                CleanupAfterAbort(current, false);
                return;
            }

            if (TryResolveAliveTool(current, out var executing))
            {
                var unit = useFactoryMode && _activeUnit != null ? _activeUnit : ResolveUnit(executing);
                NotifyConfirmAbort(executing, unit, mapped);
            }

            TryHideAllAimUI();
            if (ReferenceEquals(_activeTool, current))
                _activeTool = null;
            _hover = null;
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
                    if (raiseHud)
                        RaiseAimRejectHud(tool, unit ?? ResolveUnit(tool), budgetFail);
                    reason = budgetFail;
                    return false;
                }

                var cooldowns = turnManager.GetCooldowns(unit);
                if (!IsCooldownReadyForConfirm(tool, cooldowns))
                {
                    if (raiseHud)
                        RaiseAimRejectHud(tool, unit ?? ResolveUnit(tool), "cooldown");
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

        void RaiseAimRejectHud(IActionToolV2 tool, Unit unit, string reason)
        {
            if (tool == null)
                return;

            switch (tool)
            {
                case HexClickMover:
                    switch (reason)
                    {
                        case "lackTime":
                            HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                            break;
                        case "lackEnergy":
                            HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                            break;
                        case "cooldown":
                            HexMoveEvents.RaiseRejected(unit, MoveBlockReason.OnCooldown, null);
                            break;
                        default:
                            HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotReady, null);
                            break;
                    }
                    break;
                case AttackControllerV2:
                    switch (reason)
                    {
                        case "lackTime":
                            AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.CantMove, "No more time.");
                            break;
                        case "lackEnergy":
                            AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                            break;
                        case "cooldown":
                            AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.OnCooldown, "Attack is on cooldown.");
                            break;
                        default:
                            AttackEventsV2.RaiseRejected(unit, AttackRejectReasonV2.NotReady, null);
                            break;
                    }
                    break;
            }
        }

        PlannedCost GetBaselineCost(IActionToolV2 tool)
        {
            // 1) 工具自己能给更精确的预览就用它（目标/路径已知）
            if (tool is IActionCostPreviewV2 preview &&
                preview.TryPeekCost(out var previewSecs, out var previewEnergy))
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

            // 2) 纯移动（HexClickMover）
            if (tool is HexClickMover mover)
            {
                int secsCeil = Mathf.Max(1, mover.ResolveMoveBudgetSeconds());
                int energyRate = Mathf.Max(0, mover.ResolveMoveEnergyPerSecond());
                return new PlannedCost
                {
                    moveSecs = secsCeil,
                    moveEnergy = energyRate,
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = true
                };
            }

            // 3) 攻击（AttackControllerV2）：
            //    这里 W2 阶段只需要一个“靠近移动”的费率做预算门槛。
            if (tool is TGD.CombatV2.AttackControllerV2 attack)
            {
                int moveEnergyRate = Mathf.Max(0, attack.ResolveMoveEnergyPerSecond());
                return new PlannedCost
                {
                    moveSecs = 1,                 // 最小估算，和你们旧逻辑一致
                    moveEnergy = moveEnergyRate,    // 靠近阶段按费率估算
                    atkSecs = 0,                 // 攻击本体的秒/能量在 W3 实际执行时依据 AttackProfile 结算
                    atkEnergy = 0,
                    valid = true
                };
            }

            // 4) 兜底：其它工具（未来新技能未接入也不会炸）
            return new PlannedCost { valid = true };
        }


        PlannedCost BuildPlannedCost(IActionToolV2 tool, Unit owner, Hex target)
        {
            bool TargetAllowed()
            {
                if (tool is ChainActionBase chainTool)
                {
                    Unit actor = owner;
                    if (actor == null)
                        actor = ResolveUnit(chainTool);
                    if (actor == null && _activeUnit != null)
                        actor = _activeUnit;
                    if (actor == null && _currentUnit != null)
                        actor = _currentUnit;

                    if (actor != null)
                    {
                        var check = chainTool.ValidateTarget(actor, target);
                        return check.ok;
                    }
                }

                return true;
            }

            bool targetValid = TargetAllowed();

            if (tool is IActionCostPreviewV2 preview && preview.TryPeekCost(out var previewSecs, out var previewEnergy))
            {
                return new PlannedCost
                {
                    moveSecs = Mathf.Max(0, previewSecs),
                    moveEnergy = Mathf.Max(0, previewEnergy),
                    atkSecs = 0,
                    atkEnergy = 0,
                    valid = targetValid
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
                    valid = planned.valid && targetValid
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
                    valid = planned.valid && targetValid
                };
            }

            return new PlannedCost { valid = targetValid };
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

        string NormalizeDerivedScopeId(string baseSkillId)
        {
            var normalized = NormalizeSkillId(baseSkillId);
            if (!string.IsNullOrEmpty(normalized))
                return normalized;
            return string.IsNullOrEmpty(baseSkillId) ? string.Empty : baseSkillId;
        }

        bool CanEnterDerivedScope(Unit owner, string baseSkillId)
        {
            var key = new DerivedScopeKey(owner != null ? owner.Id : string.Empty, NormalizeDerivedScopeId(baseSkillId), _currentActionToken);
            return !_derivedScopes.Contains(key);
        }

        void MarkDerivedScope(Unit owner, string baseSkillId)
        {
            var key = new DerivedScopeKey(owner != null ? owner.Id : string.Empty, NormalizeDerivedScopeId(baseSkillId), _currentActionToken);
            _derivedScopes.Add(key);
        }

        void ClearDerivedScopeForToken(int token)
        {
            if (token <= 0)
                return;
            _derivedScopes.RemoveWhere(key => key.token == token);
        }

        bool ShouldRunDerivedWindow(Unit unit, IActionToolV2 baseTool, string baseSkillId)
        {
            if (unit == null || baseTool == null)
                return false;
            if (turnManager == null)
                return false;
            if (!IsEffectivePlayerPhase(unit))
                return false;
            if (!turnManager.IsPlayerUnit(unit))
                return false;

            if (!IsDerivedSourceKind(baseTool.Kind))
                return false;

            return CanEnterDerivedScope(unit, baseSkillId);
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

        bool ShouldPreferChainOption(ChainOption candidate, ChainOption existing)
        {
            bool candidateIsDefinition = candidate.tool is SkillDefinitionActionTool;
            bool existingIsDefinition = existing.tool is SkillDefinitionActionTool;
            if (candidateIsDefinition == existingIsDefinition)
                return false;
            return !candidateIsDefinition && existingIsDefinition;
        }

        void DeduplicateChainOptions(List<ChainOption> options)
        {
            if (options == null || options.Count <= 1)
                return;

            var keep = new List<ChainOption>(options.Count);
            var map = new Dictionary<ChainOptionDedupKey, (ChainOption option, int index)>();

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                string ownerId = option.owner != null ? option.owner.Id : string.Empty;
                string toolId = string.Empty;
                Type toolType = null;

                if (option.tool != null)
                {
                    try
                    {
                        toolId = NormalizeSkillId(option.tool.Id) ?? option.tool.Id ?? string.Empty;
                    }
                    catch (MissingReferenceException)
                    {
                        toolId = string.Empty;
                    }

                    try
                    {
                        toolType = option.tool.GetType();
                    }
                    catch (MissingReferenceException)
                    {
                        toolType = null;
                    }
                }

                var key = new ChainOptionDedupKey(ownerId, toolId, option.kind, toolType);

                if (!map.TryGetValue(key, out var existing))
                {
                    int index = keep.Count;
                    keep.Add(option);
                    map[key] = (option, index);
                    continue;
                }

                if (ShouldPreferChainOption(option, existing.option))
                {
                    keep[existing.index] = option;
                    map[key] = (option, existing.index);
                }
            }

            options.Clear();
            options.AddRange(keep);
        }

        List<ChainOption> BuildChainOptions(Unit unit, ITurnBudget budget, IResourcePool resources, int baseTimeCost, IReadOnlyList<ActionKind> allowedKinds, ICooldownSink cooldowns, ISet<IActionToolV2> pending, bool isEnemyPhase, bool restrictToOwner, List<ChainOptionDebug> diagnostics = null)
        {
            _chainBuffer.Clear();
            if (allowedKinds == null || allowedKinds.Count == 0)
                return _chainBuffer;

            var rules = ResolveRules();
            bool allowFriendlyInsertion = !restrictToOwner && (isEnemyPhase || (rules?.AllowFriendlyInsertions() ?? false));
            bool enforceReactionWithinBase = rules?.ReactionMustBeWithinBaseTime() ?? true;
            diagnostics?.Clear();

            foreach (var pair in _toolsById)
            {
                foreach (var candidate in pair.Value)
                {
                    IActionToolV2 resolvedTool;
                    if (!TryResolveAliveTool(candidate, out resolvedTool))
                    {
                        diagnostics?.Add(new ChainOptionDebug
                        {
                            toolId = candidate?.Id,
                            kind = candidate != null ? candidate.Kind : ActionKind.Standard,
                            owner = null,
                            reason = "toolUnavailable",
                            secs = -1,
                            energy = -1,
                            key = KeyCode.None
                        });
                        continue;
                    }

                    var owner = ResolveUnit(resolvedTool);
                    string toolId = resolvedTool.Id;
                    ActionKind kind = resolvedTool.Kind;

                    bool Reject(string reason, int secsValue = -1, int energyValue = -1, KeyCode keyValue = KeyCode.None)
                    {
                        diagnostics?.Add(new ChainOptionDebug
                        {
                            toolId = toolId,
                            kind = kind,
                            owner = owner,
                            reason = reason,
                            secs = secsValue,
                            energy = energyValue,
                            key = keyValue
                        });
                        return true;
                    }

                    if (restrictToOwner && owner != unit)
                        if (Reject("restrictOwner")) continue;
                    if (!allowFriendlyInsertion && owner != unit)
                        if (Reject("ownerMismatch")) continue;
                    if (allowFriendlyInsertion && owner == null)
                        if (Reject("ownerMissing")) continue;

                    var ownerCtx = ResolveContext(owner);
                    if (!IsToolGrantedForContext(ownerCtx, resolvedTool))
                        if (Reject("notGranted")) continue;
                    if (allowFriendlyInsertion && isEnemyPhase)
                    {
                        if (turnManager == null || owner == null || !turnManager.IsPlayerUnit(owner))
                            if (Reject("ownerNotPlayer")) continue;
                    }
                    else if (allowFriendlyInsertion && owner != unit)
                    {
                        if (turnManager != null && turnManager.IsEnemyUnit(owner))
                            if (Reject("ownerIsEnemy")) continue;
                    }

                    ITurnBudget ownerBudget = budget;
                    IResourcePool ownerResources = resources;
                    ICooldownSink ownerCooldowns = cooldowns;
                    if (owner != unit)
                    {
                        if (turnManager == null)
                            if (Reject("noTurnManager")) continue;

                        ownerBudget = turnManager.GetBudget(owner);
                        ownerResources = turnManager.GetResources(owner);
                        ownerCooldowns = turnManager.GetCooldowns(owner);
                    }

                    if (allowFriendlyInsertion && ownerBudget == null && ownerResources == null && ownerCooldowns == null)
                        if (Reject("noHandles")) continue;
                    if (turnManager != null && owner != null && turnManager.HasActiveFullRound(owner))
                        if (Reject("fullRound")) continue;

                    if (pending != null && pending.Contains(resolvedTool))
                        if (Reject("pending")) continue;

                    bool kindAllowed = false;
                    for (int i = 0; i < allowedKinds.Count; i++)
                    {
                        if (allowedKinds[i] == resolvedTool.Kind)
                        {
                            kindAllowed = true;
                            break;
                        }
                    }

                    if (!kindAllowed)
                        if (Reject("kindNotAllowed")) continue;

                    if (resolvedTool.Kind == ActionKind.Derived)
                        if (Reject("skipDerived")) continue;

                    var cost = GetBaselineCost(resolvedTool);
                    if (!cost.valid)
                        if (Reject("costInvalid")) continue;

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (resolvedTool.Kind == ActionKind.Reaction)
                    {
                        if (enforceReactionWithinBase)
                        {
                            if (baseTimeCost <= 0 && secs > 0)
                                if (Reject("reactionNoBase", secs, energy)) continue;
                            if (secs > baseTimeCost)
                                if (Reject("reactionTime", secs, energy)) continue;
                        }
                    }

                    if (resolvedTool.Kind == ActionKind.Free && secs != 0)
                        if (Reject("freeHasTime", secs, energy)) continue;

                    string budgetFail = EvaluateBudgetFailure(owner, secs, energy, ownerBudget, ownerResources);
                    if (budgetFail != null)
                        if (Reject($"budget:{budgetFail}", secs, energy)) continue;

                    if (ownerCooldowns != null && !IsCooldownReadyForConfirm(resolvedTool, ownerCooldowns))
                        if (Reject("cooldown", secs, energy)) continue;

                    var key = ResolveChainKey(resolvedTool.Id);
                    if (key == KeyCode.None)
                        if (Reject("noKey", secs, energy)) continue;

                    _chainBuffer.Add(new ChainOption
                    {
                        tool = resolvedTool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = resolvedTool.Kind,
                        owner = owner,
                        budget = ownerBudget,
                        resources = ownerResources,
                        cooldowns = ownerCooldowns
                    });

                    diagnostics?.Add(new ChainOptionDebug
                    {
                        toolId = toolId,
                        kind = kind,
                        owner = owner,
                        reason = "ok",
                        secs = secs,
                        energy = energy,
                        key = key
                    });
                }
            }

            return _chainBuffer;
        }

        ActionKind? ResolveActionKindForLogging(string skillId, SkillIndex index)
        {
            var normalized = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (index != null && index.TryGet(normalized, out var info) && info.definition != null)
                return info.definition.ActionKind;

            if (_toolsById.TryGetValue(normalized, out var list) && list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    if (TryResolveAliveTool(entry, out var tool))
                        return tool.Kind;
                }
            }

            return null;
        }

        void LogLearnedChainActions(string contextLabel)
        {
            if (!AdvancedDebugLogsEnabled || turnManager == null)
                return;

            var index = ResolveSkillIndex();

            void LogSide(bool isPlayerSide)
            {
                var units = turnManager.GetSideUnits(isPlayerSide);
                if (units == null)
                    return;

                foreach (var unit in units)
                {
                    if (unit == null)
                        continue;

                    var ctx = turnManager.GetContext(unit);
                    var learned = ctx?.LearnedActions;
                    var reaction = new List<string>();
                    var free = new List<string>();

                    if (learned != null)
                    {
                        for (int i = 0; i < learned.Count; i++)
                        {
                            string id = learned[i];
                            var kind = ResolveActionKindForLogging(id, index);
                            if (kind == ActionKind.Reaction)
                                reaction.Add(NormalizeSkillId(id) ?? id);
                            else if (kind == ActionKind.Free)
                                free.Add(NormalizeSkillId(id) ?? id);
                        }
                    }

                    string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                    string context = string.IsNullOrEmpty(contextLabel) ? "?" : contextLabel;
                    string reactionText = reaction.Count > 0 ? string.Join(", ", reaction) : string.Empty;
                    string freeText = free.Count > 0 ? string.Join(", ", free) : string.Empty;
                    string learnedText = (learned != null && learned.Count > 0) ? string.Join(", ", learned) : string.Empty;
                    ChainDebugLog($"[ChainDiag] Learned base={context} unit={unitLabel} Reaction=[{reactionText}] Free=[{freeText}] All=[{learnedText}]");
                }
            }

            LogSide(true);
            LogSide(false);
        }

        void LogChainDiagnostics(Unit baseUnit, string basePlanKind, string stageLabel, int baseTimeCost, bool isEnemyPhase, List<ChainOptionDebug> diagnostics)
        {
            if (!AdvancedDebugLogsEnabled || diagnostics == null)
                return;

            string baseLabel = string.IsNullOrEmpty(basePlanKind) ? "?" : basePlanKind;
            string stage = string.IsNullOrEmpty(stageLabel) ? "?" : stageLabel;
            string ownerLabel = baseUnit != null ? TurnManagerV2.FormatUnitLabel(baseUnit) : "?";

            var entries = new System.Text.StringBuilder();
            if (diagnostics.Count > 0)
            {
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    var entry = diagnostics[i];
                    if (i > 0)
                        entries.Append(", ");

                    string optionOwner = entry.owner != null ? TurnManagerV2.FormatUnitLabel(entry.owner) : "?";
                    string optionId = string.IsNullOrEmpty(entry.toolId) ? "?" : entry.toolId;
                    string reason = string.IsNullOrEmpty(entry.reason) ? "unknown" : entry.reason;

                    entries.Append(optionOwner);
                    entries.Append(':');
                    entries.Append(optionId);
                    entries.Append('(');
                    entries.Append(reason);
                    if (entry.secs >= 0)
                    {
                        entries.Append(";secs=");
                        entries.Append(entry.secs);
                    }
                    if (entry.energy >= 0)
                    {
                        entries.Append(";energy=");
                        entries.Append(entry.energy);
                    }
                    if (entry.key != KeyCode.None)
                    {
                        entries.Append(";key=");
                        entries.Append(entry.key);
                    }
                    entries.Append(')');
                }
            }

            string list = entries.ToString();
            string phase = isEnemyPhase ? "Enemy" : "Player";
            int secs = Mathf.Max(0, baseTimeCost);
            string message = $"[ChainDiag] base={baseLabel} owner={ownerLabel} stage={stage} baseSecs={secs} phase={phase} -> [{list}]";
            string key = $"{baseLabel}|{ownerLabel}|{stage}|{phase}";
            if (_chainDiagLast.TryGetValue(key, out var last) && string.Equals(last, message, StringComparison.Ordinal))
                return;

            _chainDiagLast[key] = message;
            ChainDebugLog(message);
        }

        void LogChainPrompt(Unit baseUnit, string baseKind, string stageLabel, string phaseLabel, IList<string> entries)
        {
            if (!AdvancedDebugLogsEnabled)
                return;

            string baseLabel = string.IsNullOrEmpty(baseKind) ? "?" : baseKind;
            string ownerLabel = baseUnit != null ? TurnManagerV2.FormatUnitLabel(baseUnit) : "?";
            string stage = string.IsNullOrEmpty(stageLabel) ? "?" : stageLabel;
            string content = (entries != null && entries.Count > 0) ? string.Join(", ", entries) : string.Empty;
            string message = $"[ChainPrompt] owner={ownerLabel} base={baseLabel} stage={stage} {phaseLabel}=[{content}]";
            string key = $"{ownerLabel}|{baseLabel}|{stage}|{phaseLabel}";
            if (_chainPromptLast.TryGetValue(key, out var last) && string.Equals(last, message, StringComparison.Ordinal))
                return;

            _chainPromptLast[key] = message;
            ChainDebugLog(message);
        }

        void LogDerivedDiagnostics(Unit owner, string baseKind, DerivedCandidateSource source, List<string> entries)
        {
            if (!AdvancedDebugLogsEnabled)
                return;

            string baseLabel = string.IsNullOrEmpty(baseKind) ? "?" : baseKind;
            string ownerLabel = owner != null ? TurnManagerV2.FormatUnitLabel(owner) : "?";
            string list = entries != null && entries.Count > 0 ? string.Join(", ", entries) : string.Empty;
            ChainDebugLog($"[ChainDiag] base={baseLabel} owner={ownerLabel} stage=W4.5 src={source} -> [{list}]");
        }

        void LogChainAllowedKinds(Unit owner, string baseKind, string stageLabel, IReadOnlyList<ActionKind> kinds)
        {
            if (!AdvancedDebugLogsEnabled)
                return;

            string baseLabel = string.IsNullOrEmpty(baseKind) ? "?" : baseKind;
            string ownerLabel = owner != null ? TurnManagerV2.FormatUnitLabel(owner) : "?";
            string stage = string.IsNullOrEmpty(stageLabel) ? "?" : stageLabel;
            string allowed = (kinds != null && kinds.Count > 0) ? string.Join(",", kinds) : string.Empty;
            ChainDebugLog($"[ChainDiag] base={baseLabel} owner={ownerLabel} stage={stage} allowed=[{allowed}]");
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
            if (tool is ChainActionBase chainTool && chainTool != null)
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

        void UpdateChainPopupStage(IChainPopupUI popup, Unit baseUnit, string baseKind, string label, List<ChainOption> options, bool showSkip)
        {
            if (popup == null)
                return;

            if (options != null && label == "W4.5" && baseUnit != null)
            {
                for (int i = options.Count - 1; i >= 0; i--)
                {
                    var option = options[i];
                    bool hasTool = TryResolveAliveTool(option.tool, out var resolvedTool);
                    var resolvedOwner = hasTool ? ResolveUnit(resolvedTool) : null;
                    bool bad = option.owner == null || option.owner != baseUnit;
                    if (!bad)
                    {
                        bad = !hasTool || resolvedOwner == null || resolvedOwner != baseUnit;
                    }

                    if (bad)
                    {
                        string expected = TurnManagerV2.FormatUnitLabel(baseUnit);
                        string got = resolvedOwner != null
                            ? TurnManagerV2.FormatUnitLabel(resolvedOwner)
                            : (option.owner != null ? TurnManagerV2.FormatUnitLabel(option.owner) : "?");
                        string optionId = hasTool
                            ? (string.IsNullOrEmpty(resolvedTool.Id) ? "?" : resolvedTool.Id)
                            : (option.tool != null ? (string.IsNullOrEmpty(option.tool.Id) ? "?" : option.tool.Id) : "?");
                        Debug.LogWarning($"[ChainGuard] DROP foreign/anonymous candidate owner={expected} got={got} id={optionId}", this);
                        options.RemoveAt(i);
                        continue;
                    }

                    if (hasTool && !ReferenceEquals(option.tool, resolvedTool))
                    {
                        option.tool = resolvedTool;
                        options[i] = option;
                    }
                }
            }

            if (debugLog)
            {
                var rawEntries = new List<string>(options != null ? options.Count : 0);
                if (options != null)
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        var option = options[i];
                        string ownerLabel = option.owner != null ? TurnManagerV2.FormatUnitLabel(option.owner) : "?";
                        string optionId = "?";
                        if (option.tool != null)
                        {
                            try
                            {
                                optionId = string.IsNullOrEmpty(option.tool.Id) ? "?" : option.tool.Id;
                            }
                            catch (MissingReferenceException)
                            {
                                optionId = "?";
                            }
                        }

                        int instanceId = 0;
                        if (option.tool is UnityEngine.Object unityObj && !Dead(unityObj))
                            instanceId = unityObj.GetInstanceID();

                        rawEntries.Add(ownerLabel + ":" + optionId + "@" + instanceId);
                    }
                }

                LogChainPrompt(baseUnit, baseKind, label, "RAW", rawEntries);
            }

            _chainPopupOptionBuffer.Clear();
            if (options != null && options.Count > 1)
                options.Sort(CompareChainOptions);

            var skillIndex = ResolveSkillIndex();
            Unit lastOwner = null;
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                bool hasTool = TryResolveAliveTool(option.tool, out var tool);
                string id = hasTool ? tool.Id : string.Empty;
                string name = ResolveSkillDisplayNameForUi(id, hasTool ? tool : null, skillIndex);
                if (string.IsNullOrEmpty(name))
                    name = string.IsNullOrEmpty(id) ? "?" : id;
                string meta = FormatChainOptionMeta(option);
                var icon = ResolveChainOptionIcon(hasTool ? tool : null);

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
                    hasTool,
                    startsGroup,
                    groupLabel));
            }

            if (debugLog)
            {
                var finalEntries = new List<string>(_chainPopupOptionBuffer.Count);
                for (int i = 0; i < _chainPopupOptionBuffer.Count; i++)
                {
                    var entry = _chainPopupOptionBuffer[i];
                    string identifier = string.IsNullOrEmpty(entry.Id) ? "?" : entry.Id;
                    if (!string.IsNullOrEmpty(entry.GroupLabel))
                        identifier = entry.GroupLabel + ":" + identifier;
                    finalEntries.Add(identifier);
                }

                LogChainPrompt(baseUnit, baseKind, label, "FINAL", finalEntries);
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
            if (_chainWindowDepth == 1)
            {
                _chainDiagLast.Clear();
                _chainPromptLast.Clear();
                LogLearnedChainActions(basePlan.kind);
            }
            PushInputSuppression();
            SetChainFocus(unit);

            IChainPopupUI popup = null;
            bool popupOpened = false;

            try
            {
                var rules = ResolveRules();
                bool phaseStartPlan = !string.IsNullOrEmpty(basePlan.kind)
                    && basePlan.kind.StartsWith("PhaseStart", StringComparison.OrdinalIgnoreCase);
                IReadOnlyList<ActionKind> allowedKinds = phaseStartPlan
                    ? rules?.AllowedAtPhaseStartFree()
                    : rules?.AllowedChainFirstLayer(baseKind, isEnemyPhase);
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
                        if (entry.tool != null && TryResolveAliveTool(entry.tool, out var pendingTool))
                            pendingSet.Add(pendingTool);
                    }
                }

                popup = ChainPopupUI;
                ChainPopupWindowData popupWindow = default;
                Transform popupAnchor = null;
                if (popup != null)
                {
                    popupWindow = BuildChainPopupWindow(unit, basePlan, baseKind, isEnemyPhase);
                    popupAnchor = ResolveChainAnchor(unit);
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
                    bool stageAllowedLogged = false;

                    while (stageActive)
                    {
                        if (!stageAllowedLogged)
                        {
                            LogChainAllowedKinds(unit, basePlan.kind, label, stageKinds);
                            stageAllowedLogged = true;
                        }

                        if (stageSuppressCancel)
                        {
                            stageSuppressCancel = false;
                            yield return null;
                            continue;
                        }

                        var options = BuildChainOptions(unit, budget, resources, baseTimeCost, stageKinds, cooldowns, pendingSet, isEnemyPhase, restrictToOwner, _chainDiagnostics);
                        DeduplicateChainOptions(options);
                        LogChainDiagnostics(unit, basePlan.kind, label, baseTimeCost, isEnemyPhase, _chainDiagnostics);
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

                        if (!popupOpened && popup != null)
                        {
                            popup.OpenWindow(popupWindow);
                            popup.SetAnchor(popupAnchor);
                            popupOpened = true;
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
                            UpdateChainPopupStage(popup, unit, basePlan.kind, label, ownerOptions, true);

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
                            var optionId = TryResolveAliveTool(option.tool, out var optTool) ? optTool.Id : "?";
                            string selectSuffix = option.owner != null && option.owner != unit
                                ? $"(id={optionId}, owner={ownerLabel}, kind={option.kind}, secs={option.secs}, energy={option.energy})"
                                : $"(id={optionId}, kind={option.kind}, secs={option.secs}, energy={option.energy})";
                            ActionPhaseLogger.Log(unit, basePlan.kind, $"{label} Select", selectSuffix);

                            ChainQueueOutcome outcome = default;
                            yield return TryQueueChainSelection(option, unit, basePlan.kind, label, depth + 1, pendingActions, result => outcome = result);

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

                            if (TryResolveAliveTool(outcome.tool, out var pendingTool))
                            {
                                pendingSet ??= new HashSet<IActionToolV2>();
                                pendingSet.Add(pendingTool);
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

                        if (!popupOpened)
                        {
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

        List<ChainOption> BuildDerivedOptions(Unit unit, string baseKindId, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, IReadOnlyList<string> allowedIds)
        {
            _derivedBuffer.Clear();
            List<string> diagnostics = debugLog ? new List<string>() : null;

            if (allowedIds == null || allowedIds.Count == 0)
            {
                LogDerivedDiagnostics(unit, baseKindId, DerivedCandidateSource.Tools, diagnostics);
                return _derivedBuffer;
            }

            void Append(Unit owner, string toolId, DerivedCandidateWhy why)
            {
                if (diagnostics == null)
                    return;

                string ownerLabel = owner != null ? TurnManagerV2.FormatUnitLabel(owner) : "?";
                string idLabel = string.IsNullOrEmpty(toolId) ? "?" : toolId;
                diagnostics.Add($"{ownerLabel}:{idLabel}({why})");
            }

            for (int i = 0; i < allowedIds.Count; i++)
            {
                string id = allowedIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                var toolsForId = EnsureToolsForId(unit, id);
                if (toolsForId == null || toolsForId.Count == 0)
                {
                    Append(null, id, DerivedCandidateWhy.ToolInvalid);
                    continue;
                }

                for (int j = 0; j < toolsForId.Count; j++)
                {
                    var candidate = toolsForId[j];
                    if (candidate == null)
                    {
                        Append(null, id, DerivedCandidateWhy.ToolInvalid);
                        continue;
                    }
                    if (candidate is SkillDefinitionActionTool defTool && defTool.Definition == null)
                        TryAssignDefinitionFromIndex(defTool, id);

                    if (candidate is MonoBehaviour behaviour && !Dead(behaviour) && !behaviour.isActiveAndEnabled)
                        behaviour.enabled = true;

                    if (!TryResolveAliveTool(candidate, out var tool))
                    {
                        Append(null, id, DerivedCandidateWhy.ToolInvalid);
                        continue;
                    }

                    if (tool.Kind != ActionKind.Derived)
                    {
                        Append(ResolveUnit(tool), tool.Id, DerivedCandidateWhy.WrongKind);
                        continue;
                    }

                    var owner = ResolveUnit(tool);
                    if (owner == null)
                    {
                        Append(null, tool.Id, DerivedCandidateWhy.NullOwner);
                        continue;
                    }
                    if (owner != unit)
                    {
                        Append(owner, tool.Id, DerivedCandidateWhy.OwnerMismatch);
                        continue;
                    }

                    var ownerCtx = ResolveContext(owner);
                    if (!IsToolGrantedForContext(ownerCtx, tool))
                    {
                        Append(owner, tool.Id, DerivedCandidateWhy.NotLearned);
                        continue;
                    }

                    var cost = GetBaselineCost(tool);
                    if (!cost.valid)
                    {
                        Append(owner, tool.Id, DerivedCandidateWhy.CostInvalid);
                        continue;
                    }

                    int secs = cost.TotalSeconds;
                    int energy = cost.TotalEnergy;

                    if (!MeetsBudget(unit, secs, energy, budget, resources))
                    {
                        Append(owner, tool.Id, DerivedCandidateWhy.BudgetFail);
                        continue;
                    }

                    if (cooldowns != null && !IsCooldownReadyForConfirm(tool, cooldowns))
                    {
                        Append(owner, tool.Id, DerivedCandidateWhy.OnCooldown);
                        continue;
                    }

                    var key = ResolveChainKey(tool.Id);

                    _derivedBuffer.Add(new ChainOption
                    {
                        tool = tool,
                        key = key,
                        secs = secs,
                        energy = energy,
                        kind = tool.Kind,
                        owner = owner,
                        budget = budget,
                        resources = resources,
                        cooldowns = cooldowns
                    });

                    Append(owner, tool.Id, DerivedCandidateWhy.Ok);
                }
            }

            LogDerivedDiagnostics(unit, baseKindId, DerivedCandidateSource.Tools, diagnostics);
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

        IEnumerator RunDerivedWindow(Unit unit, IActionToolV2 baseTool, ActionPlan basePlan, ExecReportData report, ITurnBudget budget, IResourcePool resources, ICooldownSink cooldowns, List<DerivedQueuedAction> derivedQueue)
        {
            _chainWindowDepth++;
            PushInputSuppression();

            IChainPopupUI popup = null;
            bool popupOpened = false;

            try
            {
                derivedQueue ??= new List<DerivedQueuedAction>();

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

                MarkDerivedScope(unit, basePlan.kind);

                if (debugLog && allowedIds != null && allowedIds.Count > 0)
                {
                    string edgeList = string.Join(", ", allowedIds);
                    ChainDebugLog($"[Rulebook] DerivedLinks from {basePlan.kind} -> [{edgeList}] (NOT used for candidates)");
                }

                var options = BuildDerivedOptions(unit, basePlan.kind, budget, resources, cooldowns, allowedIds);
                DeduplicateChainOptions(options);
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
                    UpdateChainPopupStage(popup, unit, basePlan.kind, "W4.5", options, true);
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
                                var optionId = TryResolveAliveTool(option.tool, out var optTool) ? optTool.Id : "?";
                                Log($"[Derived] Select(id={optionId}, kind={option.kind})");
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
                                    UpdateChainPopupStage(popup, unit, basePlan.kind, "W4.5", options, true);

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
                    if (!popupOpened)
                    {
                        for (int i = 0; i < options.Count; i++)
                        {
                            var option = options[i];
                            if (option.key != KeyCode.None && Input.GetKeyDown(option.key))
                            {
                                handled = true;
                                var optionId = TryResolveAliveTool(option.tool, out var optTool) ? optTool.Id : "?";
                                Log($"[Derived] Select(id={optionId}, kind={option.kind})");
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
                {
                    _chainWindowDepth--;
                    if (_chainWindowDepth == 0)
                    {
                        _chainDiagLast.Clear();
                        _chainPromptLast.Clear();
                    }
                }
            }
        }

        IEnumerator TryQueueDerivedSelection(ChainOption option, Unit unit, string baseId, ITurnBudget budget, IResourcePool resources, List<DerivedQueuedAction> derivedQueue, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(default);
                yield break;
            }

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            string toolId = tool.Id;

            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(default);
                yield break;
            }

            EnterAimForTool(tool);
            ActionPhaseLogger.Log(unit, toolId, "W1_AimBegin");

            if (tool is ChainActionBase chainTool)
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
                        ActionPhaseLogger.Log(unit, toolId, "W1_AimCancel", "(reason=EndTurn)");
                        if (TryResolveAliveTool(tool, out tool))
                            tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    if (!TryResolveAliveTool(tool, out tool))
                    {
                        cursor?.Clear();
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
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetOk", $"(id={toolId}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(unit, baseId, "W4.5 TargetInvalid", $"(id={toolId}, reason={check.reason})");
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
                        ActionPhaseLogger.Log(unit, toolId, "W1_AimCancel");
                        if (TryResolveAliveTool(tool, out tool))
                            tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    yield return null;
                }

                cursor?.Clear();

                if (!targetChosen)
                {
                    if (TryResolveAliveTool(tool, out tool))
                        tool.OnExitAim();
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }

            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            tool.OnExitAim();
            ChainCursor?.Clear();

            ActionPhaseLogger.Log(unit, toolId, "W2_ConfirmStart");
            ActionPhaseLogger.Log(unit, toolId, "W2_PrecheckOk");

            if (!targetChosen)
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }
            // —— 新增：拿冷却池
            var cooldowns = (turnManager != null && unit != null) ? turnManager.GetCooldowns(unit) : null;

            // —— 新增：先做“是否就绪”检查（防止目录里该 Key 正在冷却）
            {
                string fail = EvaluateBudgetFailure(unit, option.secs, option.energy, budget, resources);
                if (fail == null && !IsCooldownReadyForConfirm(tool, cooldowns))
                    fail = "cooldown";

                if (fail != null)
                {
                    ActionPhaseLogger.Log(unit, toolId, "W2_PreDeductCheckFail", $"(reason={fail})");
                    ActionPhaseLogger.Log(unit, toolId, "W2_ConfirmAbort", $"(reason={fail})");
                    onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                    yield break;
                }
            }
            string failReason = EvaluateBudgetFailure(unit, option.secs, option.energy, budget, resources);

            if (failReason != null)
            {
                ActionPhaseLogger.Log(unit, toolId, "W2_PreDeductCheckFail", $"(reason={failReason})");
                ActionPhaseLogger.Log(unit, toolId, "W2_ConfirmAbort", $"(reason={failReason})");
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
            }

            ActionPhaseLogger.Log(unit, toolId, "W2_PreDeductCheckOk");
            TryStartCooldownIfAny(tool, unit, cooldowns, 1);

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
                valid = true,
                skillId = toolId,
                chainDepth = 1,
                ruleOverride = false
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
                kind = toolId,
                target = selectedTarget,
                cost = planCost,
                chainDepth = 1
            };

            derivedQueue.Add(new DerivedQueuedAction
            {
                tool = tool,
                plan = actionPlan,
                budget = budget,
                resources = resources,
                depth = 1
            });

            _queuedActionsPending++;

            ChainCursor?.Clear();

            onComplete?.Invoke(new ChainQueueOutcome { queued = true, cancel = false, tool = tool });
        }

        IEnumerator TryQueueChainSelection(ChainOption option, Unit baseUnit, string baseKind, string stageLabel, int chainDepth, List<ChainQueuedAction> pendingActions, Action<ChainQueueOutcome> onComplete)
        {
            var tool = option.tool;
            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(default);
                yield break;
            }

            string toolId = tool.Id;

            Hex selectedTarget = Hex.Zero;
            bool targetChosen = true;

            var owner = option.owner;
            if (owner == null)
                owner = ResolveUnit(tool);

            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(default);
                yield break;
            }

            EnterAimForTool(tool);
            ActionPhaseLogger.Log(owner, toolId, "W1_AimBegin");

            if (tool is ChainActionBase chainTool)
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
                        ActionPhaseLogger.Log(owner, toolId, "W1_AimCancel", "(reason=EndTurn)");
                        if (TryResolveAliveTool(tool, out tool))
                            tool.OnExitAim();
                        onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                        yield break;
                    }

                    if (!TryResolveAliveTool(tool, out tool))
                    {
                        cursor?.Clear();
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
                                ActionPhaseLogger.Log(baseUnit, baseKind, $"{stageLabel} TargetOk", $"(id={toolId}, hex={hover.Value})");
                                targetChosen = true;
                                awaitingSelection = false;
                            }
                            else
                            {
                                ActionPhaseLogger.Log(baseUnit, baseKind, $"{stageLabel} TargetInvalid", $"(id={toolId}, reason={check.reason})");
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
                        ActionPhaseLogger.Log(owner, toolId, "W1_AimCancel");
                        if (TryResolveAliveTool(tool, out tool))
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

            if (!TryResolveAliveTool(tool, out tool))
            {
                onComplete?.Invoke(new ChainQueueOutcome { queued = false, cancel = false, tool = null });
                yield break;
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
            var cooldowns = (turnManager != null && owner != null) ? turnManager.GetCooldowns(owner) : null;
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
            TryStartCooldownIfAny(tool, owner, cooldowns, chainDepth);
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
                valid = true,
                skillId = tool.Id,
                chainDepth = chainDepth,
                ruleOverride = false
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
                cost = planCost,
                chainDepth = chainDepth
            };

            pendingActions.Add(new ChainQueuedAction
            {
                tool = tool,
                owner = owner,
                plan = actionPlan,
                budget = budget,
                resources = resources,
                depth = Mathf.Max(0, chainDepth)
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

        private static bool IsCooldownReadyForConfirm(IActionToolV2 tool, ICooldownSink sink)
        {
            if (sink == null) return true;
            var key = GetCooldownKey(tool);
            if (string.IsNullOrEmpty(key)) return true;
            return sink.Ready(key); // Hub 内部若不存在该 key，应视为 Ready
        }

        bool IsAnyToolBusy()
        {
            foreach (var mb in tools)
            {
                if (Dead(mb))
                    continue;
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
                if (skipPhaseStartFreeChain)
                {
                    Log($"[Free] PhaseStart(P1) freeskip unit={TurnManagerV2.FormatUnitLabel(unit)}");
                    yield break;
                }

                yield return RunStartFreeChainWindow(unit, "PhaseStart(P1)", false);
                yield break;
            }

            // 敌方分支：整批友方连锁期间加守卫，避免被 AutoFinishEnemyTurn 抢先结束

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

            ClearPlanStack(unit);

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
                cost = new PlannedCost { valid = true },
                chainDepth = 0
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
                ClearPlanStack(unit);
                yield break;
            }

            for (int i = pendingChain.Count - 1; i >= 0; --i)
            {
                var pending = pendingChain[i];
                if (pending.tool == null)
                    continue;

                if (!TryResolveAliveTool(pending.tool, out var alivePending))
                    continue;

                if (_queuedActionsPending > 0)
                    _queuedActionsPending--;
                yield return ExecuteAndResolve(alivePending, pending.owner ?? unit, pending.plan, pending.budget, pending.resources, pending.depth);
            }

            ClearPlanStack(unit);
            TryFinalizeEndTurn();
        }
        int GetAttackComboCount(Unit unit)
        {
            if (unit == null || turnManager == null)
                return 0;

            var context = turnManager.GetContext(unit);
            var controller = ResolveAttackController(context);
            return controller != null ? Mathf.Max(0, controller.ReportComboBaseCount) : 0;
        }

        AttackControllerV2 ResolveAttackController(UnitRuntimeContext context)
        {
            if (context == null)
                return null;

            if (_attackControllerCache.TryGetValue(context, out var cached))
            {
                if (cached != null)
                    return cached;
                _attackControllerCache.Remove(context);
            }

            var resolved = context.GetComponentInChildren<AttackControllerV2>(true);
            if (resolved != null)
                _attackControllerCache[context] = resolved;
            return resolved;
        }

        private static string GetCooldownKey(IActionToolV2 tool)
        {
            if (tool is ICooldownKeyProvider p && !string.IsNullOrEmpty(p.CooldownKey))
                return p.CooldownKey;
            if (tool is ChainActionBase chain && !string.IsNullOrEmpty(chain.CooldownId))
                return chain.CooldownId;
            return tool?.Id; // 兜底
        }
        void TryStartCooldownIfAny(IActionToolV2 tool, Unit unit, ICooldownSink sink, int chainDepth)
        {
            if (sink == null || tool == null) return;

            string key = null;
            if (tool is ICooldownKeyProvider p && !string.IsNullOrEmpty(p.CooldownKey))
                key = p.CooldownKey;
            if (string.IsNullOrEmpty(key))
                key = tool.Id;

            if (string.IsNullOrEmpty(key))
                return;

            int seconds = TGD.DataV2.ActionCooldownCatalog.Instance != null
                   ? TGD.DataV2.ActionCooldownCatalog.Instance.GetSeconds(key)
                   : 0;

            int previousDepth = _chainDepth;
            _chainDepth = chainDepth;
            try
            {
                var context = (turnManager != null && unit != null) ? turnManager.GetContext(unit) : null;
                var set = context != null ? context.Rules : null;
                bool? friendlyHint = null;
                if (turnManager != null && unit != null)
                    friendlyHint = turnManager.IsPlayerUnit(unit);

                var ctx2 = RulesAdapter.BuildContext(
                    context,
                    skillId: key,
                    kind: tool.Kind,
                    chainDepth: _chainDepth,
                    comboIndex: GetAttackComboCount(unit),
                    planSecs: 0,
                    planEnergy: 0,
                    unitIdHint: unit != null ? unit.Id : null,
                    isFriendlyHint: friendlyHint
                );

                int before = seconds;
                RuleEngineV2.Instance.OnStartCooldown(set, in ctx2, ref seconds);
                if (seconds != before)
                    ActionPhaseLogger.Log($"[Rules] CD start {key}: {before}->{seconds} (StartMods)");
            }
            finally
            {
                _chainDepth = previousDepth;
            }

            if (seconds < 0)
                seconds = 0;

            sink.StartSeconds(key, seconds);
        }

        static bool Dead(UnityEngine.Object o) => o == null || o.Equals(null);

        static bool TryResolveAliveTool(IActionToolV2 candidate, out IActionToolV2 tool)
        {
            if (candidate == null)
            {
                tool = null;
                return false;
            }

            if (candidate is MonoBehaviour behaviour)
            {
                if (Dead(behaviour) || !behaviour.isActiveAndEnabled)
                {
                    tool = null;
                    return false;
                }
            }
            else if (candidate is UnityEngine.Object unityObj && Dead(unityObj))
            {
                tool = null;
                return false;
            }

            tool = candidate;
            return true;
        }

        List<IActionToolV2> ResolveToolsForId(string id)
        {
            var normalized = NormalizeSkillId(id);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (_activeCtx != null)
            {
                var list = EnsureToolsFromContext(_activeCtx, normalized);
                if (list != null && list.Count > 0)
                    return list;
            }

            if (_currentUnit != null)
            {
                var ctx = ResolveContext(_currentUnit);
                var list = EnsureToolsFromContext(ctx, normalized);
                if (list != null && list.Count > 0)
                    return list;
            }

            if (useFactoryMode && _activeUnit != null)
            {
                var list = EnsureToolsForId(_activeUnit, normalized);
                if (list != null && list.Count > 0)
                    return list;
            }

            if (_toolsById.TryGetValue(normalized, out var existing) && existing != null && existing.Count > 0)
                return existing;

            return null;
        }

        List<IActionToolV2> EnsureToolsForId(Unit unit, string id)
        {
            var normalized = NormalizeSkillId(id);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (_toolsById.TryGetValue(normalized, out var list) && list != null && list.Count > 0)
                return list;

            UnitRuntimeContext context = null;
            if (unit != null)
                context = ResolveContext(unit);
            if (context == null)
                context = _activeCtx;
            if (context == null)
                return null;

            var resolved = EnsureToolsFromContext(context, normalized);
            if (resolved != null && resolved.Count > 0)
                return resolved;

            if (_toolsById.TryGetValue(normalized, out var finalList) && finalList != null && finalList.Count > 0)
                return finalList;

            return null;
        }

        List<IActionToolV2> EnsureToolsFromContext(UnitRuntimeContext context, string id)
        {
            if (context == null)
                return null;

            var normalized = NormalizeSkillId(id);
            if (string.IsNullOrEmpty(normalized))
                return null;

            var behaviours = context.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is IActionToolV2 tool)
                {
                    var toolId = NormalizeSkillId(tool.Id);
                    if (!string.Equals(toolId, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        if (behaviour is SkillDefinitionActionTool defTool && TryAssignDefinitionFromIndex(defTool, normalized))
                            toolId = NormalizeSkillId(defTool.Id);
                    }
                    if (string.Equals(toolId, normalized, StringComparison.OrdinalIgnoreCase))
                        RegisterTool(tool);
                }
            }

            if (_toolsById.TryGetValue(normalized, out var list) && list != null && list.Count > 0)
                return list;

            return null;
        }
        bool TryAssignDefinitionFromIndex(SkillDefinitionActionTool tool, string skillId)
        {
            if (tool == null)
                return false;

            var index = ResolveSkillIndex();
            if (index == null)
                return false;

            var normalized = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(normalized))
                return false;

            if (!index.TryGet(normalized, out var info) || info.definition == null)
                return false;

            if (!ReferenceEquals(tool.Definition, info.definition))
                tool.SetDefinition(info.definition);

            return true;
        }
        SkillIndex ResolveSkillIndex()
        {
            if (rulebook is ActionRulebook soRulebook && soRulebook.skillIndex != null)
                return soRulebook.skillIndex;

            return null;
        }

        string ResolveSkillDisplayNameForUi(string skillId, IActionToolV2 tool, SkillIndex index = null)
        {
            SkillDefinitionV2 fallbackDefinition = null;
            if (tool is SkillDefinitionActionTool defTool)
                fallbackDefinition = defTool.Definition;

            index ??= ResolveSkillIndex();
            var display = SkillDisplayNameUtility.ResolveDisplayName(skillId, index, fallbackDefinition);
            if (!string.IsNullOrEmpty(display))
                return display;

            return SkillDisplayNameUtility.NormalizeId(skillId);
        }

        static string GetHierarchyPath(GameObject go)
        {
            if (go == null)
                return "?";

            var builder = new StringBuilder();
            var current = go.transform;
            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.Length > 0 ? builder.ToString() : "?";
        }

        static string NormalizeSkillId(string skillId)
        {
            return string.IsNullOrWhiteSpace(skillId) ? null : skillId.Trim();
        }

        bool IsSkillGrantedIncludingDerived(HashSet<string> grants, string skillId)
        {
            if (grants == null || grants.Count == 0)
                return true;

            var normalized = NormalizeSkillId(skillId);
            if (string.IsNullOrEmpty(normalized))
                return false;

            return grants.Contains(normalized);
        }

        bool TryGetActiveTool(out IActionToolV2 tool)
        {
            if (TryResolveAliveTool(_activeTool, out tool))
                return true;

            if (_activeTool != null)
                HandleLostActiveTool();

            tool = null;
            return false;
        }

        void HandleLostActiveTool()
        {
            TryHideAllAimUI();
            _activeTool = null;
            _hover = null;
            if (_phase == Phase.Aiming)
            {
                _phase = Phase.Idle;
                ClearPlanStack(null);
                TryFinalizeEndTurn();
            }
        }

        void PruneDeadTools()
        {
            if (_toolsById == null) return;

            List<string> emptyKeys = null;
            HashSet<IActionToolV2> removed = null;

            foreach (var kv in _toolsById)
            {
                var list = kv.Value;
                if (list == null)
                    continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var tool = list[i];
                    if (tool is UnityEngine.Object unityObj && Dead(unityObj))
                    {
                        list.RemoveAt(i);
                        removed ??= new HashSet<IActionToolV2>();
                        removed.Add(tool);
                    }
                }

                if (list.Count == 0)
                {
                    emptyKeys ??= new List<string>();
                    emptyKeys.Add(kv.Key);
                }
            }

            if (emptyKeys != null)
            {
                for (int i = 0; i < emptyKeys.Count; i++)
                    _toolsById.Remove(emptyKeys[i]);
            }

            if (removed != null)
            {
                foreach (var tool in removed)
                    _destroySubscriptions.Remove(tool);
            }

            for (int i = tools.Count - 1; i >= 0; i--)
            {
                if (Dead(tools[i]))
                    tools.RemoveAt(i);
            }
        }

    }
}
