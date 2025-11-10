// File: TGD.CombatV2/AttackControllerV2.cs
using System.Collections;
using System.Collections.Generic;
using TGD.CombatV2.Integration;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.CoreV2.Rules;
using TGD.HexBoard;
using TGD.HexBoard.Path;
using UnityEngine;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class AttackControllerV2 : ActionToolBase, IActionToolV2, IActionExecReportV2, ICooldownKeyProvider, IBindContext, ICursorUser
    {
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;

        [SerializeField]
        [Tooltip("Action identifier used when registering with CombatActionManagerV2.")]
        string skillId = AttackProfileRules.DefaultSkillId;

        public string Id => ResolveSkillId();
        public ActionKind Kind => ActionKind.Standard;

        [Header("Bridge (optional)")]
        [HideInInspector]
        public PlayerOccupancyBridge bridgeOverride;   // ★ 新增：强制使用角色自己的桥


        [Header("View (optional)")]
        [HideInInspector]
        public Transform viewOverride;

        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTiler tiler;
        public HexEnvironmentSystem env;
        public DefaultTargetValidator targetValidator;
        public HexOccupancyService occupancyService;

        [Header("Context (optional)")]
        public MoveRateStatusRuntime status;
        public MonoBehaviour stickySource;
        // ===== Context =====
        [SerializeField] private UnitRuntimeContext _ctx;

        private TGD.CoreV2.UnitRuntimeContext ResolveCtx()
        {
            if (_ctx != null) return _ctx;
            // 自身或父层拿一次并缓存
            _ctx = GetComponent<TGD.CoreV2.UnitRuntimeContext>() ??
                   GetComponentInParent<TGD.CoreV2.UnitRuntimeContext>(true);
            return _ctx;
        }

        // 如果你已有 Init/Bind(UnitRuntimeContext ctx) 之类的方法，也可以在那里显式赋值：_ctx = ctx;

        [Header("Turn Manager Binding")]
        [SerializeField, HideInInspector]
        public TurnManagerV2 turnManager;
        TurnManagerV2 _boundTurnManager;

        [Header("Runtime Behaviour")]
        [SerializeField]
        bool assumePrepaid = true;

        [Header("Animation")]
        public float stepSeconds = 0.12f;
        public float minStepSeconds = 0.06f;
        public float y = 0.01f;

        [Header("Visuals")]
        public Color previewColor = new(1f, 0.9f, 0.2f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Debug")]
        public bool debugLog = true;
        public bool suppressInternalLogs = false;

        TargetSelectionCursor _cursor;
        TargetSelectionCursor Cursor => _cursor;
        public string CooldownKey => ResolveSkillId();

        string ResolveSkillId()
        {
            if (string.IsNullOrEmpty(skillId))
                return AttackProfileRules.DefaultSkillId;
            return skillId.Trim();
        }
        public void SetCursorHighlighter(IHexHighlighter highlighter)
        {
            _cursor = highlighter != null ? new TargetSelectionCursor(highlighter) : null;
        }
        HexOccupancy _occ;
        IActorOccupancyBridge _bridge;
        PlayerOccupancyBridge _playerBridge;
        PlayerOccupancyBridge _boundPlayerBridge;
        bool _previewDirty = true;
        int _previewAnchorVersion = -1;
        int _planAnchorVersion = -1;
        IStickyMoveSource _sticky;

        float MoveRateMin => ctx != null ? ctx.MoveRateMin : MoveRateRules.DefaultMin;
        float MoveRateMax => ctx != null ? ctx.MoveRateMax : MoveRateRules.DefaultMax;
        int MoveRateMinInt => ctx != null ? ctx.MoveRateMin : MoveRateRules.DefaultMinInt;
        int MoveRateMaxInt => ctx != null ? ctx.MoveRateMax : MoveRateRules.DefaultMaxInt;

        float ClampMoveRate(float value) => Mathf.Clamp(value, MoveRateMin, MoveRateMax);
        int ClampMoveRateInt(int value) => Mathf.Clamp(value, MoveRateMinInt, MoveRateMaxInt);

        public int ResolveAttackSeconds()
        {
            if (ctx != null)
                return Mathf.Max(0, ctx.AttackSeconds);
            return AttackProfileRules.DefaultSeconds;
        }

        public int ResolveAttackEnergyCost()
        {
            if (ctx != null)
                return Mathf.Max(0, ctx.AttackEnergyCost);
            return AttackProfileRules.DefaultEnergyCost;
        }

        public int ResolveMoveEnergyPerSecond()
            => ctx != null ? ctx.MoveEnergyPerSecond : MoveProfileRules.DefaultEnergyPerSecond;

        public int ResolveMoveBudgetSeconds()
        {
            if (ctx != null)
                return ctx.MoveBaseSecondsCeil;
            return Mathf.Max(1, Mathf.CeilToInt(MoveProfileRules.DefaultSeconds));
        }

        public float ResolveMoveRefundThreshold()
            => ctx != null ? ctx.MoveRefundThresholdSeconds : MoveProfileRules.DefaultRefundThresholdSeconds;

        public int ResolveMeleeRange()
            => ctx != null ? ctx.AttackMeleeRange : AttackProfileRules.DefaultMeleeRange;

        public float ResolveAttackRefundThreshold()
            => ctx != null ? ctx.AttackRefundThresholdSeconds : AttackProfileRules.DefaultRefundThresholdSeconds;

        public float ResolveAttackFreeMoveCutoff()
            => ctx != null ? ctx.AttackFreeMoveCutoffSeconds : AttackProfileRules.DefaultFreeMoveCutoffSeconds;

        public float ResolveAttackKeepDeg()
            => ctx != null ? ctx.AttackKeepDeg : AttackProfileRules.DefaultKeepDeg;

        public float ResolveAttackTurnDeg()
            => ctx != null ? ctx.AttackTurnDeg : AttackProfileRules.DefaultTurnDeg;

        public float ResolveAttackTurnSpeed()
            => ctx != null ? ctx.AttackTurnSpeedDegPerSec : AttackProfileRules.DefaultTurnSpeedDegPerSec;

        TargetingSpec _attackSpec;

        bool _aiming;
        bool _moving;
        PreviewData _currentPreview;
        Hex? _hover;

        int _attacksThisTurn;
        int _reportUsedSeconds;
        int _reportRefundedSeconds;
        int _reportMoveUsedSeconds;
        int _reportMoveRefundSeconds;
        int _reportAttackUsedSeconds;
        int _reportAttackRefundSeconds;
        int _reportEnergyMoveNet;
        int _reportEnergyAtkNet;
        bool _reportFreeMove;
        bool _reportPending;
        int _reportComboBaseCount;
        int _pendingComboBaseCount;
        string _reportRefundTag;
        bool _reportAttackExecuted;
        readonly HashSet<Hex> _tempReservedThisAction = new();

        struct PendingAttack
        {
            public bool active;
            public bool strikeProcessed;
            public Unit unit;
            public Hex target;
            public int comboIndex;
        }

        PendingAttack _pendingAttack;

        Unit ResolveSelfUnit() => OwnerUnit;

        Transform ResolveSelfView()
            => UnitRuntimeBindingUtil.ResolveUnitView(this, ctx, viewOverride);

        void ClearExecReport()
        {
            _reportUsedSeconds = 0;
            _reportRefundedSeconds = 0;
            _reportMoveUsedSeconds = 0;
            _reportMoveRefundSeconds = 0;
            _reportAttackUsedSeconds = 0;
            _reportAttackRefundSeconds = 0;
            _reportEnergyMoveNet = 0;
            _reportEnergyAtkNet = 0;
            _reportFreeMove = false;
            _reportPending = false;
            _reportComboBaseCount = 0;
            _pendingComboBaseCount = 0;
            _reportRefundTag = null;
            _reportAttackExecuted = false;
        }

        void SetExecReport(
            int movePlannedSeconds,
            int attackPlannedSeconds,
            int moveRefundSeconds,
            int attackRefundSeconds,
            int energyMoveNet,
            int energyAtkNet,
            bool attackExecuted,
            bool freeMove,
            string refundTag)
        {
            _reportMoveUsedSeconds = Mathf.Max(0, movePlannedSeconds);
            _reportAttackUsedSeconds = Mathf.Max(0, attackPlannedSeconds);
            _reportMoveRefundSeconds = Mathf.Max(0, moveRefundSeconds);
            _reportAttackRefundSeconds = Mathf.Max(0, attackRefundSeconds);
            _reportUsedSeconds = Mathf.Max(0, _reportMoveUsedSeconds + _reportAttackUsedSeconds);
            _reportRefundedSeconds = Mathf.Max(0, _reportMoveRefundSeconds + _reportAttackRefundSeconds);
            _reportEnergyMoveNet = energyMoveNet;
            _reportEnergyAtkNet = energyAtkNet;
            _reportFreeMove = freeMove;
            _reportComboBaseCount = attackExecuted ? Mathf.Max(0, _pendingComboBaseCount) : 0;
            _reportPending = true;
            _pendingComboBaseCount = 0;
            _reportRefundTag = refundTag;
            _reportAttackExecuted = attackExecuted;
        }

        internal bool HasPendingExecReport => _reportPending;
        void LogInternal(string message)
        {
            if (!suppressInternalLogs && debugLog)
                Debug.Log(message, this);
        }

        public void AttachTurnManager(TurnManagerV2 manager)
        {
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager.SideEnded -= OnSideEnded;
            }

            turnManager = manager;
            _boundTurnManager = null;

            if (turnManager == null || !isActiveAndEnabled)
                return;

            turnManager.TurnStarted += OnTurnStarted;
            turnManager.SideEnded += OnSideEnded;
            _boundTurnManager = turnManager;
        }

        public override void BindContext(UnitRuntimeContext context, TurnManagerV2 manager)
        {
            base.BindContext(context, manager);
            AttachTurnManager(manager);
        }

        public struct PlannedAttackCost
        {
            public int moveSecs;
            public int moveEnergy;
            public int atkSecs;
            public int atkEnergy;
            public bool valid;
        }
        int ComputeAttackEnergyCost(bool treatAsEnemy)
        {
            if (!treatAsEnemy)
                return 0;

            int comboIndex = Mathf.Max(0, _attacksThisTurn);
            int baseCost = ResolveAttackEnergyCost();
            float scale = 1f + 0.5f * comboIndex;

            if (ctx != null)
            {
                var set = ctx.Rules;
                if (set != null)
                {
                    var ruleCtx = RulesAdapter.BuildContext(
                        ctx,
                        skillId: ResolveSkillId(),
                        kind: Kind,
                        chainDepth: 0,
                        comboIndex: comboIndex,
                        planSecs: ResolveAttackSeconds(),
                        planEnergy: baseCost
                    );
                    RuleEngineV2.Instance.ModifyComboFactor(set, in ruleCtx, ref scale);
                }
            }

            return Mathf.Max(0, Mathf.CeilToInt(baseCost * scale));
        }

        public PlannedAttackCost PeekPlannedCost(Hex target)
        {
            int fallbackMoveSecs = ResolveMoveBudgetSeconds();
            int moveEnergyRate = ResolveMoveEnergyPerSecond();
            int fallbackAtkSecs = ResolveAttackSeconds();
            int fallbackAtkEnergy = ComputeAttackEnergyCost(true);

            var result = new PlannedAttackCost
            {
                moveSecs = fallbackMoveSecs,
                moveEnergy = moveEnergyRate * fallbackMoveSecs,
                atkSecs = fallbackAtkSecs,
                atkEnergy = fallbackAtkEnergy,
                valid = false
            };

            PreviewData preview = null;
            if (_currentPreview != null && _currentPreview.valid && _currentPreview.targetHex.Equals(target))
            {
                preview = _currentPreview;
            }
            else
            {
                preview = BuildPreview(target, true);
            }

            if (preview != null && preview.valid)
            {
                result.valid = true;
                result.moveSecs = Mathf.Max(0, preview.moveSecsCharge);
                result.moveEnergy = Mathf.Max(0, preview.moveEnergyCost);
                if (preview.targetIsEnemy)
                {
                    result.atkSecs = Mathf.Max(0, preview.attackSecsCharge);
                    result.atkEnergy = Mathf.Max(0, preview.attackEnergyCost);
                }
                else
                {
                    result.atkSecs = 0;
                    result.atkEnergy = 0;
                }
            }

            return result;
        }

        public PlannedAttackCost GetBaselineCost()
        {
            int moveSecs = ResolveMoveBudgetSeconds();
            int moveEnergyRate = ResolveMoveEnergyPerSecond();
            int atkSecs = ResolveAttackSeconds();
            int atkEnergy = ResolveAttackEnergyCost();

            return new PlannedAttackCost
            {
                moveSecs = moveSecs,
                moveEnergy = moveEnergyRate * moveSecs,
                atkSecs = atkSecs,
                atkEnergy = atkEnergy,
                valid = true
            };
        }

        public (int moveSecs, int atkSecs, int energyMove, int energyAtk) GetPlannedCost()
        {
            int moveSecs = ResolveMoveBudgetSeconds();
            int moveEnergy = moveSecs * ResolveMoveEnergyPerSecond();
            int atkSecs = ResolveAttackSeconds();
            int atkEnergy = ResolveAttackEnergyCost();
            return (moveSecs, atkSecs, moveEnergy, atkEnergy);
        }

        public int ReportUsedSeconds => _reportPending ? _reportUsedSeconds : 0;
        public int ReportRefundedSeconds => _reportPending ? _reportRefundedSeconds : 0;
        public int ReportEnergyMoveNet => _reportPending ? _reportEnergyMoveNet : 0;
        public int ReportEnergyAtkNet => _reportPending ? _reportEnergyAtkNet : 0;
        public int ReportMoveEnergyNet => ReportEnergyMoveNet;
        public int ReportAttackEnergyNet => ReportEnergyAtkNet;
        public bool ReportFreeMoveApplied => _reportPending && _reportFreeMove;
        struct MoveRatesSnapshot
        {
            public int baseRate;
            public float buffMult;
            public float stickyMult;
            public int flatAfter;
            public float startEnvMult;
            public float mrNoEnv;
            public float mrClick;
            public bool startIsSticky;
        }

        sealed class PreviewData
        {
            public bool valid;
            public bool targetIsEnemy;
            public Hex targetHex;
            public Hex landingHex;
            public List<Hex> path;
            public int steps;
            public int moveSecsPred;
            public int moveSecsCharge;
            public int attackSecsCharge;
            public int moveEnergyCost;
            public int attackEnergyCost;
            public float mrClick;
            public float mrNoEnv;
            public MoveRatesSnapshot rates;
            public PlanKind plan;
            public TargetCheckResult targetCheck;
            public AttackRejectReasonV2 rejectReason;
            public string rejectMessage;
        }

        IGridActor SelfActor
        {
            get
            {
                var unit = ResolveSelfUnit();
                return UnitRuntimeBindingUtil.ResolveGridActor(unit, _occ, _bridge);
            }
        }

        Hex CurrentAnchor
        {
            get
            {
                var unit = ResolveSelfUnit();
                return UnitRuntimeBindingUtil.ResolveAnchor(unit, _occ, _bridge);
            }
        }

        void Awake()
        {
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            if (!status) status = GetComponentInParent<MoveRateStatusRuntime>(true);
            if (!turnManager) turnManager = GetComponentInParent<TurnManagerV2>(true);
            RefreshSticky(markPreviewDirty: false);
            TryResolveBridge();

            if (!targetValidator)
                targetValidator = GetComponent<DefaultTargetValidator>() ?? GetComponentInParent<DefaultTargetValidator>(true);

            if (!occupancyService)
                occupancyService = GetComponent<HexOccupancyService>() ?? GetComponentInParent<HexOccupancyService>(true);

            if (occupancyService == null && _bridge is PlayerOccupancyBridge concreteBridge && concreteBridge.occupancyService)
                occupancyService = concreteBridge.occupancyService;

            RefreshOccupancy();

            _attackSpec = new TargetingSpec
            {
                occupant = TargetOccupantMask.Enemy | TargetOccupantMask.Empty,
                terrain = TargetTerrainMask.Any,
                allowSelf = false,
                requireOccupied = false,
                requireEmpty = false,
                maxRangeHexes = -1
            };
            EnsureBound();
        }

        void RefreshSticky(bool markPreviewDirty)
        {
            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);

            if (markPreviewDirty)
            {
                _previewDirty = true;
                _previewAnchorVersion = -1;
                _planAnchorVersion = -1;
                _hover = null;
                _currentPreview = null;
                _cursor?.Clear();
            }
        }

        public void RefreshFactoryInjection()
        {
            RefreshSticky(markPreviewDirty: true);
        }

        protected override void HookEvents(bool bind)
        {
            if (bind)
            {
                AttackEventsV2.AttackStrikeFired += OnAttackStrikeFired;
                AttackEventsV2.AttackAnimationEnded += OnAttackAnimationEnded;
                AttackEventsV2.AttackMoveFinished += OnAttackMoveFinished;
            }
            else
            {
                AttackEventsV2.AttackStrikeFired -= OnAttackStrikeFired;
                AttackEventsV2.AttackAnimationEnded -= OnAttackAnimationEnded;
                AttackEventsV2.AttackMoveFinished -= OnAttackMoveFinished;
            }
        }

        protected override void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            base.OnEnable();
            TryResolveBridge();
            EnsureBound();
            ClearPendingAttack();
            AttachTurnManager(turnManager);
            RefreshOccupancy();
        }

        void Start()
        {
            tiler?.EnsureBuilt();

            if (authoring?.Layout == null)
            {
                enabled = false;
                return;
            }

            if (_bridge == null) _bridge = GetComponentInParent<IActorOccupancyBridge>(true);
            RefreshOccupancy();
        }

        protected override void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            base.OnDisable();
            UpdateBridgeSubscription(null);
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager.SideEnded -= OnSideEnded;
                _boundTurnManager = null;
            }
            ClearPendingAttack();
            ResetComboCounters();
            ClearTempReservations("OnDisable");
            Cursor?.Clear();
            _currentPreview = null;
            _hover = null;
            _occ = null;
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
        }

        protected override void OnDestroy()
        {
            if (!Application.isPlaying)
            {
                base.OnDestroy();
                return;
            }

            UpdateBridgeSubscription(null);
            if (_boundTurnManager != null)
            {
                _boundTurnManager.TurnStarted -= OnTurnStarted;
                _boundTurnManager.SideEnded -= OnSideEnded;
                _boundTurnManager = null;
            }
            base.OnDestroy();
        }

        void HandleAnchorChanged(Hex anchor, int version)
        {
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
            _currentPreview = null;
            _hover = null;
            Cursor?.Clear();
        }

        void OnTurnStarted(Unit unit)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            if (tm == null || ResolveSelfUnit() != unit)
                return;

            ResetComboCounters();
        }

        void ResetComboCounters()
        {
            _attacksThisTurn = 0;
            _pendingComboBaseCount = 0;
            _reportComboBaseCount = 0;
        }

        bool IsReady => EnsureBound();

        void DumpReadiness()
        {
#if UNITY_EDITOR
            if (IsReady)
                return;

            var missing = new List<string>();
            if (!authoring)
                missing.Add(nameof(authoring));
            else if (authoring.Layout == null)
                missing.Add("authoring.Layout");
            if (!tiler)
                missing.Add(nameof(tiler));
            if (_bridge == null)
                missing.Add("bridge");
            if (_playerBridge == null)
                missing.Add("playerBridge");
            if (occupancyService == null)
                missing.Add(nameof(occupancyService));
            if (_occ == null)
                missing.Add("_occ");
            if (SelfActor == null)
                missing.Add("SelfActor");

            if (missing.Count > 0)
                Debug.LogWarning($"[AttackControllerV2] Not ready. Missing: {string.Join(", ", missing)}", this);
#endif
        }

        void RaiseRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            AttackEventsV2.RaiseRejected(unit, reason, message);
        }

        public bool TryPrecheckAim(out string reason, bool raiseHud = true)
        {
            if (!EnsureBound())
            {
                DumpReadiness(); reason = "(not-ready)";
                if (raiseHud) RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotReady, "Not ready.");
                return false;
            }
            if (!IsReady)
            {
                DumpReadiness();
                if (raiseHud)
                    RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotReady, "Not ready.");
                reason = "(not-ready)";
                return false;
            }

            if (ctx != null && ctx.Entangled)
            {
                if (raiseHud)
                    RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                reason = "(entangled)";
                return false;
            }

            var selfUnit = ResolveSelfUnit();
            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            if (tm == null || selfUnit == null)
            {
                if (raiseHud)
                    RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotReady, "Not ready.");
                reason = "(no-turn-manager)";
                return false;
            }

            if (!assumePrepaid)
            {
                const int needSec = 1;
                var budget = tm.GetBudget(selfUnit);
                if (budget == null || !budget.HasTime(needSec))
                {
                    if (raiseHud)
                        RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.CantMove, "No more time.");
                    reason = "(no-time)";
                    return false;
                }

                int moveEnergyCost = MoveEnergyPerSecond();
                var resources = tm.GetResources(selfUnit);
                if (resources != null && moveEnergyCost > 0 && !resources.Has("Energy", moveEnergyCost))
                {
                    if (raiseHud)
                        RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                    reason = "(no-energy)";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        public void OnEnterAim()
        {
            if (!TryPrecheckAim(out _))
                return;

            _aiming = true;
            _hover = null;
            _currentPreview = null;
            Cursor?.Clear();
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
            AttackEventsV2.RaiseAimShown(ResolveSelfUnit(), System.Array.Empty<Hex>());
            var v = ResolveSelfView();
            var u = ResolveSelfUnit();
        }

        public void OnExitAim()
        {
            _aiming = false;
            _hover = null;
            _currentPreview = null;
            Cursor?.Clear();
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
            AttackEventsV2.RaiseAimHidden();
        }

        public void OnHover(Hex hex)
        {
            if (!_aiming || _moving) return;
            if (!IsReady) return;
            if (_hover.HasValue && _hover.Value.Equals(hex) && _currentPreview != null && !_previewDirty) return;
            _hover = hex;
            _currentPreview = BuildPreview(hex, false);
            RenderPreview(_currentPreview);
        }

        public IEnumerator OnConfirm(Hex hex)
        {
            ClearExecReport();
            if (!IsReady)
            {
                RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotReady, "Not ready.");
                yield break;
            }
            if (_moving)
            {
                RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.Busy, "Attack in progress.");
                yield break;
            }

            RefreshOccupancy();

            var unit = ResolveSelfUnit();
            var targetCheck = ValidateAttackTarget(unit, hex);
            // Phase logging handled by CombatActionManagerV2.

            if (!targetCheck.ok)
            {
                RaiseTargetRejected(unit, targetCheck.reason);
                yield break;
            }

            if (targetCheck.plan != PlanKind.MoveOnly && targetCheck.plan != PlanKind.MoveAndAttack)
            {
                RaiseTargetRejected(unit, TargetInvalidReason.Unknown);
                yield break;
            }

            var preview = BuildPreview(hex, true, targetCheck);

            if (preview == null || !preview.valid)
            {
                RaiseRejected(ResolveSelfUnit(),
                    preview != null ? preview.rejectReason : AttackRejectReasonV2.NoPath,
                    preview != null ? preview.rejectMessage : "Invalid target.");
                yield break;
            }

            if (_playerBridge != null && _previewAnchorVersion != _playerBridge.AnchorVersion)
            {
                Debug.LogWarning($"[Guard] Attack preview stale (previewV={_previewAnchorVersion} nowV={_playerBridge.AnchorVersion}). Rebuild.", this);
                preview = BuildPreview(hex, true, targetCheck);
                if (preview == null || !preview.valid)
                {
                    RaiseRejected(ResolveSelfUnit(),
                        preview != null ? preview.rejectReason : AttackRejectReasonV2.NoPath,
                        preview != null ? preview.rejectMessage : "Invalid target.");
                    yield break;
                }
            }

            _planAnchorVersion = _playerBridge != null ? _playerBridge.AnchorVersion : -1;

            if (ctx != null && ctx.Entangled && (preview.path == null || preview.path.Count > 1))
            {
                RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.CantMove, "Can't move while entangled.");
                yield break;
            }

            preview.moveEnergyCost = Mathf.Max(0, preview.moveSecsCharge) * MoveEnergyPerSecond();
            preview.attackEnergyCost = ComputeAttackEnergyCost(preview.targetIsEnemy);
            if (!preview.targetIsEnemy)
                preview.attackSecsCharge = 0;
            bool attackPlanned = preview.targetIsEnemy;
            _pendingComboBaseCount = attackPlanned ? Mathf.Max(0, _attacksThisTurn) : 0;
            int moveSecsCharge = Mathf.Max(0, preview.moveSecsCharge);
            int attackSecsCharge = preview.targetIsEnemy ? Mathf.Max(0, preview.attackSecsCharge) : 0;
            int moveEnergyCost = Mathf.Max(0, preview.moveEnergyCost);
            int attackEnergyCost = preview.targetIsEnemy ? Mathf.Max(0, preview.attackEnergyCost) : 0;

            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            if (tm == null)
            {
                RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotReady, "Turn manager missing.");
                yield break;
            }

            if (!assumePrepaid)
            {
                var budget = tm.GetBudget(unit);
                if (budget == null || !budget.HasTime(moveSecsCharge))
                {
                    RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.CantMove, "No more time.");
                    yield break;
                }

                if (attackPlanned && !budget.HasTime(moveSecsCharge + attackSecsCharge))
                {
                    attackPlanned = false;
                    attackSecsCharge = 0;
                    attackEnergyCost = 0;
                    _pendingComboBaseCount = 0;
                }

                var resources = tm.GetResources(unit);
                if (resources != null)
                {
                    int totalEnergy = moveEnergyCost + (attackPlanned ? attackEnergyCost : 0);
                    if (totalEnergy > 0 && !resources.Has("Energy", totalEnergy))
                    {
                        RaiseRejected(ResolveSelfUnit(), AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                        yield break;
                    }
                }
            }
            int attackEnergySpent = 0;
            if (attackPlanned)
            {
                attackEnergySpent = Mathf.Max(0, attackEnergyCost);
                _attacksThisTurn = Mathf.Max(0, _attacksThisTurn + 1);
            }

            _currentPreview = null;
            _hover = null;
            Cursor?.Clear();
            AttackEventsV2.RaiseAimHidden();

            _moving = true;
            yield return RunAttack(preview, attackPlanned, moveSecsCharge, moveEnergyCost, attackSecsCharge, attackEnergySpent);
            _moving = false;
        }

        void RenderPreview(PreviewData preview)
        {
            Cursor?.Clear();
            if (preview == null)
            {
                AttackEventsV2.RaiseAimShown(ResolveSelfUnit(), System.Array.Empty<Hex>());
                return;
            }

            if (!preview.valid || preview.path == null)
            {
                if (_hover.HasValue)
                    Cursor?.ShowSingle(_hover.Value, invalidColor);
                AttackEventsV2.RaiseAimShown(ResolveSelfUnit(), System.Array.Empty<Hex>());
                return;
            }

            Cursor?.ShowPath(preview.path, previewColor);
            AttackEventsV2.RaiseAimShown(ResolveSelfUnit(), preview.path);
        }

        TargetCheckResult ValidateAttackTarget(Unit unit, Hex hex)
        {
            if (targetValidator == null || _attackSpec == null || unit == null)
                return new TargetCheckResult { ok = false, reason = TargetInvalidReason.Unknown, hit = HitKind.None, plan = PlanKind.None };
            return targetValidator.Check(unit, hex, _attackSpec);
        }

        void RaiseTargetRejected(Unit unit, TargetInvalidReason reason)
        {
            var (mapped, message) = MapAttackReject(reason);
            if (debugLog)
                // Phase logging handled by CombatActionManagerV2.
                RaiseRejected(unit, mapped, message);
        }

        internal void HandleConfirmAbort(Unit unit, string reason)
        {
            unit ??= ResolveSelfUnit();
            switch (reason)
            {
                case "targetInvalid":
                    RaiseRejected(unit, AttackRejectReasonV2.NoPath, "Invalid target.");
                    break;
                case "lackTime":
                    RaiseRejected(unit, AttackRejectReasonV2.CantMove, "No more time.");
                    break;
                case "lackEnergy":
                    RaiseRejected(unit, AttackRejectReasonV2.NotEnoughResource, "Not enough energy.");
                    break;
                case "cooldown":
                    RaiseRejected(unit, AttackRejectReasonV2.OnCooldown, "Attack is on cooldown.");
                    break;
                case "notReady":
                    RaiseRejected(unit, AttackRejectReasonV2.NotReady, "Not ready.");
                    break;
                default:
                    RaiseRejected(unit, AttackRejectReasonV2.NotReady, "Action aborted.");
                    break;
            }
        }

        static (AttackRejectReasonV2 reason, string message) MapAttackReject(TargetInvalidReason reason)
        {
            switch (reason)
            {
                case TargetInvalidReason.Self:
                    return (AttackRejectReasonV2.NoPath, "Self target not allowed.");
                case TargetInvalidReason.Friendly:
                    return (AttackRejectReasonV2.NoPath, "Cannot attack ally.");
                case TargetInvalidReason.EnemyNotAllowed:
                    return (AttackRejectReasonV2.NoPath, "Enemy not allowed.");
                case TargetInvalidReason.EmptyNotAllowed:
                    return (AttackRejectReasonV2.NoPath, "Target must be occupied.");
                case TargetInvalidReason.Blocked:
                    return (AttackRejectReasonV2.NoPath, "Blocked by obstacle.");
                case TargetInvalidReason.OutOfRange:
                    return (AttackRejectReasonV2.CantMove, "Out of range.");
                case TargetInvalidReason.None:
                    return (AttackRejectReasonV2.NoPath, "Invalid target.");
                default:
                    return (AttackRejectReasonV2.NoPath, "Invalid target.");
            }
        }

        PreviewData BuildPreview(Hex target, bool logInvalid, TargetCheckResult? overrideCheck = null)
        {
            if (!IsReady)
            {
                return new PreviewData
                {
                    valid = false,
                    rejectReason = AttackRejectReasonV2.NotReady,
                    rejectMessage = "Not ready."
                };
            }

            var layout = authoring.Layout;
            var unit = ResolveSelfUnit();

            RefreshOccupancy();

            var start = CurrentAnchor;
            _previewAnchorVersion = _playerBridge != null ? _playerBridge.AnchorVersion : -1;
            _previewDirty = false;
            var rates = BuildMoveRates(start);
            var preview = new PreviewData
            {
                targetHex = target,
                rates = rates,
                mrClick = rates.mrClick,
                mrNoEnv = rates.mrNoEnv,
                attackSecsCharge = ResolveAttackSeconds()
            };

            if (layout != null && !layout.Contains(target))
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Target out of board.";
                return preview;
            }

            if (env != null && env.IsPit(target))
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Target is pit.";
                return preview;
            }

            var check = overrideCheck ?? ValidateAttackTarget(unit, target);
            preview.targetCheck = check;
            preview.plan = check.plan;

            if (!check.ok)
            {
                var (reject, message) = MapAttackReject(check.reason);
                preview.valid = false;
                preview.rejectReason = reject;
                preview.rejectMessage = message;
                if (logInvalid && debugLog)
                {
                    // Phase logging handled by CombatActionManagerV2.
                }
                return preview;
            }

            bool treatAsEnemy = check.plan == PlanKind.MoveAndAttack;
            bool treatAsMoveOnly = check.plan == PlanKind.MoveOnly;
            if (!treatAsEnemy && !treatAsMoveOnly)
            {
                preview.valid = false;
                preview.rejectReason = AttackRejectReasonV2.NoPath;
                preview.rejectMessage = "Unsupported plan.";
                return preview;
            }

            preview.targetIsEnemy = treatAsEnemy;

            var passability = PassabilityFactory.ForApproach(_occ, SelfActor, CurrentAnchor);
            List<Hex> path = null;
            Hex landing = target;

            if (treatAsEnemy)
            {
                int range = Mathf.Max(1, ResolveMeleeRange());
                if (!TryFindMeleePath(start, target, range, passability, out landing, out path))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No landing.";
                    return preview;
                }
            }
            else
            {
                if ((passability != null && passability.IsBlocked(target)) || (passability == null && _occ != null && _occ.IsBlocked(target, SelfActor)))
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "Cell occupied.";
                    return preview;
                }

                path = ShortestPath(start, target, cell => IsBlockedForMove(cell, start, target, passability));
                if (path == null)
                {
                    preview.valid = false;
                    preview.rejectReason = AttackRejectReasonV2.NoPath;
                    preview.rejectMessage = "No path.";
                    return preview;
                }
            }

            if (!treatAsEnemy)
            {
                preview.attackSecsCharge = 0;
                preview.attackEnergyCost = 0;
            }

            preview.landingHex = landing;
            preview.path = path;
            preview.steps = Mathf.Max(0, (path?.Count ?? 1) - 1);

            float mrClick = ClampMoveRate(rates.mrClick);
            int predSecs = preview.steps > 0 ? Mathf.CeilToInt(preview.steps / Mathf.Max(0.01f, mrClick)) : 0;
            int chargeSecs = predSecs;
            if (treatAsEnemy)
            {
                float charge = Mathf.Max(0f, predSecs - 0.2f);
                chargeSecs = Mathf.CeilToInt(charge);
            }

            preview.moveSecsPred = predSecs;
            preview.moveSecsCharge = chargeSecs;
            preview.moveEnergyCost = Mathf.Max(0, chargeSecs) * MoveEnergyPerSecond();
            preview.attackEnergyCost = ComputeAttackEnergyCost(treatAsEnemy);
            preview.valid = true;
            return preview;
        }

        PreviewData ClonePreview(PreviewData src)
        {
            if (src == null) return null;
            return new PreviewData
            {
                valid = src.valid,
                targetIsEnemy = src.targetIsEnemy,
                targetHex = src.targetHex,
                landingHex = src.landingHex,
                path = src.path != null ? new List<Hex>(src.path) : null,
                steps = src.steps,
                moveSecsPred = src.moveSecsPred,
                moveSecsCharge = src.moveSecsCharge,
                attackSecsCharge = src.attackSecsCharge,
                moveEnergyCost = src.moveEnergyCost,
                attackEnergyCost = src.attackEnergyCost,
                mrClick = src.mrClick,
                mrNoEnv = src.mrNoEnv,
                rates = src.rates,
                plan = src.plan,
                targetCheck = src.targetCheck,
                rejectReason = src.rejectReason,
                rejectMessage = src.rejectMessage
            };
        }

        IEnumerator RunAttack(
            PreviewData preview,
            bool attackPlanned,
            int moveSecsCharge,
            int moveEnergyPaid,
            int attackSecsCharge,
            int attackEnergyPaid)
        {
            if (preview == null)
                yield break;

            ClearTempReservations("PreAction");

            if (_playerBridge != null && _planAnchorVersion != _playerBridge.AnchorVersion)
            {
                Debug.LogWarning($"[Guard] Anchor changed before attack execute (planV={_planAnchorVersion} nowV={_playerBridge.AnchorVersion}). Rebuild plan.", this);
                preview = BuildPreview(preview.targetHex, true, preview.targetCheck);
                if (preview == null || !preview.valid)
                {
                    HandleApproachAbort();
                    yield break;
                }
                _planAnchorVersion = _playerBridge.AnchorVersion;
            }

            var layout = authoring.Layout;
            var hexSpace = HexSpace.Instance;
            if (hexSpace == null)
            {
                Debug.LogWarning("[AttackControllerV2] HexSpace instance is missing.", this);
                yield break;
            }
            var unit = ResolveSelfUnit();
            var view = ResolveSelfView();
            RefreshOccupancy();

            var startAnchor = CurrentAnchor;
            Facing4 finalFacing = unit != null ? unit.Facing : Facing4.PlusQ;
            var passability = PassabilityFactory.ForApproach(_occ, SelfActor, startAnchor);

            List<Hex> executionPath = null;
            if (preview.targetIsEnemy)
            {
                int range = Mathf.Max(1, ResolveMeleeRange());
                if (!TryFindMeleePath(startAnchor, preview.targetHex, range, passability, out _, out executionPath))
                {
                    HandleApproachAbort();
                    yield break;
                }
            }
            else
            {
                if ((passability != null && passability.IsBlocked(preview.targetHex)) ||
                    (passability == null && _occ != null && _occ.IsBlocked(preview.targetHex, SelfActor)))
                {
                    HandleApproachAbort();
                    yield break;
                }

                executionPath = ShortestPath(startAnchor, preview.targetHex,
                    cell => IsBlockedForMove(cell, startAnchor, preview.targetHex, passability));
                if (executionPath == null || executionPath.Count == 0)
                {
                    HandleApproachAbort();
                    yield break;
                }
            }

            if (executionPath == null || executionPath.Count == 0)
            {
                HandleApproachAbort();
                yield break;
            }

            if (view != null && executionPath.Count >= 2)
            {
                var fromW = hexSpace.HexToWorld(executionPath[0], y);
                var toW = hexSpace.HexToWorld(executionPath[^1], y);
                float keep = ResolveAttackKeepDeg();
                float turn = ResolveAttackTurnDeg();
                float speed = ResolveAttackTurnSpeed();
                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(finalFacing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(view, yaw, speed);
                finalFacing = nf;
            }

            float mrNoEnv = preview.mrNoEnv;
            float refundThreshold = Mathf.Max(0.01f, ResolveAttackRefundThreshold());

            var sim = MoveSimulator.Run(
                executionPath,
                mrNoEnv,
                preview.mrClick,
                moveSecsCharge,
                SampleStepModifier,
                refundThreshold,
                debugLog,
                MoveRateMin,
                MoveRateMax);

            var reached = sim.ReachedPath ?? new List<Hex>();
            int refundedSeconds = Mathf.Max(0, sim.RefundedSeconds);
            float usedSeconds = Mathf.Max(0f, sim.UsedSeconds);
            var stepRates = sim.StepEffectiveRates;
            int moveEnergyRate = MoveEnergyPerSecond();

            try
            {
                if (reached.Count > 1)
                {
                    for (int i = 1; i < reached.Count; i++)
                        ReserveTemp(reached[i]);
                }

                AttackEventsV2.RaiseAttackMoveStarted(unit, reached);

                if (reached.Count <= 1)
                {
                    if (attackPlanned && authoring?.Layout != null && view != null)
                    {
                        var fromW = hexSpace.HexToWorld(startAnchor, y);
                        var toW = hexSpace.HexToWorld(preview.targetHex, y);
                        float keep = ResolveAttackKeepDeg();
                        float turn = ResolveAttackTurnDeg();
                        float speed = ResolveAttackTurnSpeed();
                        var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(finalFacing, fromW, toW, keep, turn);
                        yield return HexFacingUtil.RotateToYaw(view, yaw, speed);
                        finalFacing = nf;
                    }

                    _bridge?.MoveCommit(startAnchor, finalFacing);
                    UnitRuntimeBindingUtil.SyncUnit(unit, startAnchor, finalFacing);
                    AttackEventsV2.RaiseAttackMoveFinished(unit, unit != null ? unit.Position : startAnchor);

                    if (attackPlanned)
                        TriggerAttackAnimation(unit, preview.targetHex);

                    int meleeMoveUsedSeconds = Mathf.Max(0, moveSecsCharge);
                    int meleeAttackUsedSeconds = attackPlanned ? Mathf.Max(0, attackSecsCharge) : 0;
                    int meleeMoveRefundSeconds = meleeMoveUsedSeconds;
                    int meleeAttackRefundSeconds = 0;
                    int meleeMoveEnergyNet = 0;
                    int meleeAttackEnergyNet = attackPlanned ? Mathf.Max(0, attackEnergyPaid) : 0;
                    string meleeRefundTag = meleeMoveRefundSeconds > 0 ? "Speed_Adjust" : null;
                    SetExecReport(
                        meleeMoveUsedSeconds,
                        meleeAttackUsedSeconds,
                        meleeMoveRefundSeconds,
                        meleeAttackRefundSeconds,
                        meleeMoveEnergyNet,
                        meleeAttackEnergyNet,
                        attackPlanned,
                        false,
                        meleeRefundTag);
                    yield break;
                }

                bool truncated = reached.Count < executionPath.Count;
                bool stoppedByExternal = false;
                bool attackRolledBack = false;
                Hex lastPosition = startAnchor;

                for (int i = 1; i < reached.Count; i++)
                {
                    if (ctx != null && ctx.Entangled)
                    {
                        stoppedByExternal = true;
                        break;
                    }

                    var from = reached[i - 1];
                    var to = reached[i];

                    if (passability != null && passability.IsBlocked(to))
                    {
                        stoppedByExternal = true;
                        break;
                    }
                    if (passability == null && _occ != null && _occ.IsBlocked(to, SelfActor))
                    {
                        stoppedByExternal = true;
                        break;
                    }
                    if (env != null && env.IsPit(to))
                    {
                        stoppedByExternal = true;
                        break;
                    }

                    AttackEventsV2.RaiseAttackMoveStep(unit, from, to, i, reached.Count - 1);

                    float effMR = (stepRates != null && (i - 1) < stepRates.Count)
                        ? stepRates[i - 1]
                        : ClampMoveRate(mrNoEnv);
                    float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(MoveRateMin, effMR));

                    if (attackPlanned && !attackRolledBack && effMR + 1e-4f < preview.mrClick)
                    {
                        attackRolledBack = true;
                        _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                        var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
                        var cancelCtx = ctx != null ? ctx : (tm != null && unit != null ? tm.GetContext(unit) : null);
                        CAM.RaiseActionCancelled(cancelCtx, ResolveSkillId(), "RolledBack");
                        AttackEventsV2.RaiseMiss(unit, "Attack cancelled (slowed).");
                    }

                    var fromW = hexSpace.HexToWorld(from, y);
                    var toW = hexSpace.HexToWorld(to, y);
                    string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                    float t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime / stepDuration;
                        if (view != null)
                            view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                        yield return null;
                    }

                    lastPosition = to;
                    _tempReservedThisAction.Add(to);

                    if (_sticky != null && status != null &&
                        _sticky.TryGetSticky(to, out var mult, out var turns, out var tag) &&
                        turns > 0 && !Mathf.Approximately(mult, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, mult, turns, to.ToString());
                        LogInternal($"[Sticky] Apply U={unitLabel} tag={tag}@{to} mult={mult:F2} turns={turns}");
                    }
                }

                bool reachedDestination = executionPath.Count > 0 && lastPosition.Equals(executionPath[^1]);
                if (!reachedDestination)
                    truncated = true;
                // ……for 循环完成后，准备最终提交：
                _bridge?.MoveCommit(lastPosition, finalFacing);
                UnitRuntimeBindingUtil.SyncUnit(unit, lastPosition, finalFacing);

                AttackEventsV2.RaiseAttackMoveFinished(unit, unit != null ? unit.Position : lastPosition);

                bool attackSuccess = attackPlanned && !attackRolledBack && !truncated && !stoppedByExternal;
                if (attackSuccess)
                {
                    TriggerAttackAnimation(unit, preview.targetHex);
                }
                else if (attackPlanned)
                {
                    if (!attackRolledBack)
                    {
                        _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                        AttackEventsV2.RaiseMiss(unit, "Out of reach.");
                    }
                }
                float cutoff = Mathf.Max(0f, ResolveAttackFreeMoveCutoff());
                bool isMelee = attackPlanned;
                bool canFree = isMelee && moveSecsCharge >= 1;
                bool freeMoveApplied = false;

                if (canFree && reached != null && reached.Count >= 2 && usedSeconds < cutoff)
                {
                    freeMoveApplied = true;

#if !USE_TMV2
                    if (debugLog)
                    {
                        string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                        LogInternal($"[Attack] FreeMove1s U={unitLabel} used={usedSeconds:F2}s (<{cutoff:F2})");
                    }
#endif
                }

                int moveUsedSeconds = Mathf.Max(0, moveSecsCharge);
                int moveRefundSeconds = Mathf.Max(0, refundedSeconds);
                if (freeMoveApplied)
                    moveRefundSeconds += 1;
                int attackUsedSeconds = attackPlanned ? Mathf.Max(0, attackSecsCharge) : 0;
                int attackRefundSeconds = (attackPlanned && !attackSuccess) ? Mathf.Max(0, attackSecsCharge) : 0;
                int netMoveSeconds = Mathf.Max(0, moveSecsCharge - moveRefundSeconds);
                int moveEnergyNet = netMoveSeconds * moveEnergyRate;
                int attackEnergyNet = attackSuccess ? Mathf.Max(0, attackEnergyPaid) : 0;

                string refundTag = null;
                if (freeMoveApplied)
                    refundTag = "FreeMove";
                else if (attackPlanned && !attackSuccess && attackRolledBack)
                    refundTag = "Attack_Adjust";
                else if (moveRefundSeconds > 0)
                    refundTag = "Speed_Adjust";

                SetExecReport(
                    moveUsedSeconds,
                    attackUsedSeconds,
                    moveRefundSeconds,
                    attackRefundSeconds,
                    moveEnergyNet,
                    attackEnergyNet,
                    attackSuccess,
                    freeMoveApplied,
                    refundTag);
            }
            finally
            {
                ClearTempReservations("ActionEnd");
                _planAnchorVersion = -1;
            }

            void HandleApproachAbort()
            {
                if (attackPlanned)
                    _attacksThisTurn = Mathf.Max(0, _attacksThisTurn - 1);
                var abortUnit = ResolveSelfUnit();
                var abortAnchor = CurrentAnchor;
                var abortFacing = abortUnit != null ? abortUnit.Facing : Facing4.PlusQ;

                _bridge?.MoveCommit(abortAnchor, abortFacing);
                UnitRuntimeBindingUtil.SyncUnit(abortUnit, abortAnchor, abortFacing);
                AttackEventsV2.RaiseAttackMoveFinished(abortUnit, abortUnit != null ? abortUnit.Position : abortAnchor);
                int plannedMove = Mathf.Max(0, moveSecsCharge);
                int plannedAttack = attackPlanned ? Mathf.Max(0, attackSecsCharge) : 0;
                SetExecReport(
                    plannedMove,
                    plannedAttack,
                    plannedMove,
                    plannedAttack,
                    0,
                    0,
                    false,
                    false,
                    null);
            }
        }
        int IActionExecReportV2.UsedSeconds => ReportUsedSeconds;
        int IActionExecReportV2.RefundedSeconds => ReportRefundedSeconds;

        void IActionExecReportV2.Consume()
        {
            ClearExecReport();
        }

        public bool IsBusy => _moving;
        public int ReportComboBaseCount => _reportComboBaseCount;

        public int ReportMoveUsedSeconds => _reportPending ? _reportMoveUsedSeconds : 0;
        public int ReportMoveRefundSeconds => _reportPending ? _reportMoveRefundSeconds : 0;
        public int ReportAttackUsedSeconds => _reportPending ? _reportAttackUsedSeconds : 0;
        public int ReportAttackRefundSeconds => _reportPending ? _reportAttackRefundSeconds : 0;
        public string ReportRefundTag => _reportPending ? _reportRefundTag : null;
        public bool ReportAttackExecuted => _reportPending && _reportAttackExecuted;


        MoveRatesSnapshot BuildMoveRates(Hex start)
        {
            int baseRate = ctx != null ? ctx.BaseMoveRate : GetFallbackBaseRate();
            baseRate = ClampMoveRateInt(baseRate);

            float buffMult = 1f;
            int flatAfter = 0;
            if (ctx != null)
            {
                buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
                flatAfter = ctx.MoveRateFlatAdd;
            }

            float stickyMult = status != null ? status.GetProduct() : 1f;
            buffMult = Mathf.Clamp(buffMult, 0.01f, 100f);
            stickyMult = Mathf.Clamp(stickyMult, 0.01f, 100f);

            float combined = Mathf.Clamp(buffMult * stickyMult, 0.01f, 100f);

            var startSample = SampleStepModifier(start);
            float startEnv = Mathf.Clamp(startSample.Multiplier <= 0f ? 1f : startSample.Multiplier, ENV_MIN, ENV_MAX);
            bool startIsSticky = startSample.Sticky;

            float mrNoEnv = StatsMathV2.MR_MultiThenFlat(
                baseRate,
                new[] { combined },
                flatAfter,
                MoveRateMin,
                MoveRateMax);
            mrNoEnv = ClampMoveRate(mrNoEnv);

            float startUse = startIsSticky ? 1f : startEnv;
            startUse = Mathf.Clamp(startUse, ENV_MIN, ENV_MAX);
            float mrClick = ClampMoveRate(mrNoEnv * startUse);

            return new MoveRatesSnapshot
            {
                baseRate = baseRate,
                buffMult = buffMult,
                stickyMult = stickyMult,
                flatAfter = flatAfter,
                startEnvMult = startEnv,
                mrNoEnv = mrNoEnv,
                mrClick = mrClick,
                startIsSticky = startIsSticky
            };
        }


        MoveSimulator.StickySample SampleStepModifier(Hex hex)
        {
            float mult = 1f;
            bool sticky = false;
            bool hasStickySource = false;

            if (_sticky != null && _sticky.TryGetSticky(hex, out var stickM, out var stickTurns, out var tag))
            {
                if (!Mathf.Approximately(stickM, 1f))
                {
                    mult *= stickM;
                    hasStickySource = true;
                    sticky = stickTurns > 0;
                }
            }

            if (!hasStickySource && env != null)
            {
                float envMult = Mathf.Clamp(env.GetSpeedMult(hex), ENV_MIN, ENV_MAX);
                if (!Mathf.Approximately(envMult, 1f))
                {
                    mult *= envMult;
                }
            }
            bool isSticky = sticky && !Mathf.Approximately(mult, 1f);
            return new MoveSimulator.StickySample(mult, isSticky);
        }

        int GetFallbackBaseRate()
        {
            if (ctx != null) return ctx.BaseMoveRate;
            return ClampMoveRateInt(3);
        }

        int MoveEnergyPerSecond() => Mathf.Max(0, ResolveMoveEnergyPerSecond());

        bool IsEnemyHex(Hex hex)
        {
            if (targetValidator == null || _attackSpec == null)
                return false;

            var self = ResolveSelfUnit();
            var check = targetValidator.Check(self, hex, _attackSpec);
            return check.hit == HitKind.Enemy;
        }

        void DebugEnemyProbe(Hex hex)
        {
            RefreshOccupancy();
            var self = ResolveSelfUnit();

            IGridActor occActor = null;
            bool hasActor = _occ != null && _occ.TryGetActor(hex, out occActor) && occActor != null;
            string actorName = hasActor ? occActor.GetType().Name : "NULL";
            string unitAt = "NO-UNIT";
            if (hasActor && occActor is UnitGridAdapter grid && grid.Unit != null)
                unitAt = grid.Unit.Id;

            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            bool selfPlayer = tm?.IsPlayerUnit(self) == true;
            bool selfEnemy = tm?.IsEnemyUnit(self) == true;

            TargetCheckResult validatorResult = default;
            if (targetValidator != null && _attackSpec != null)
                validatorResult = targetValidator.Check(self, hex, _attackSpec);

            Debug.Log($"[Probe] occ={_occ != null} hasActor={hasActor} actor={actorName} unitAt={unitAt} self={self?.Id} selfP={selfPlayer} selfE={selfEnemy} validatorHit={validatorResult.hit} validatorPlan={validatorResult.plan}", this);
        }

        bool IsEnemyUnit(Unit candidate)
        {
            if (candidate == null)
                return false;

            var self = ResolveSelfUnit();
            if (self == null)
                return false;

            if (ReferenceEquals(candidate, self))
                return false;

            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            if (tm == null)
                return false;

            bool selfPlayer = tm.IsPlayerUnit(self);
            bool selfEnemy = tm.IsEnemyUnit(self);

            if (selfPlayer)
                return tm.IsEnemyUnit(candidate);

            if (selfEnemy)
                return tm.IsPlayerUnit(candidate);

            return tm.IsEnemyUnit(candidate);
        }

        bool IsBlockedForMove(Hex cell, Hex start, Hex landing, IPassability passability = null)
        {
            if (authoring?.Layout == null) return true;
            if (!authoring.Layout.Contains(cell)) return true;
            if (env != null && env.IsPit(cell)) return true;
            if (cell.Equals(start)) return false;
            if (cell.Equals(landing)) return false;
            if (_tempReservedThisAction.Contains(cell)) return true;
            if (passability != null && passability.IsBlocked(cell)) return true;
            if (_occ == null)
                return false;

            var actor = SelfActor;
            if (passability == null)
            {
                if (actor != null)
                    return !_occ.CanPlaceIgnoringTemp(actor, cell, actor.Facing, ignore: actor);
                return _occ.IsBlocked(cell);
            }

            return false;
        }

        bool TryFindMeleePath(Hex start, Hex target, int range, IPassability passability, out Hex landing, out List<Hex> bestPath)
        {
            landing = target;
            bestPath = null;

            var enemyCells = GetEnemyCellsForTarget(target);
            int meleeRange = Mathf.Max(1, range);

            foreach (var cell in enemyCells)
            {
                if (Hex.Distance(start, cell) <= meleeRange)
                {
                    landing = start;
                    bestPath = new List<Hex> { start };
                    return true;
                }
            }

            var candidates = new HashSet<Hex>();
            int bestEnemyDist = int.MaxValue;
            int bestLen = int.MaxValue;
            Hex bestLanding = target;
            foreach (var cell in enemyCells)
            {
                for (int dist = 1; dist <= meleeRange; dist++)
                {
                    foreach (var candidate in Hex.Ring(cell, dist))
                    {
                        if (!candidates.Add(candidate)) continue;
                        if (!authoring.Layout.Contains(candidate)) continue;
                        if (env != null && env.IsPit(candidate)) continue;
                        if (passability != null && passability.IsBlocked(candidate)) continue;
                        if (passability == null && _occ != null && SelfActor != null && !_occ.CanPlaceIgnoringTemp(SelfActor, candidate, SelfActor.Facing, ignore: SelfActor)) continue;

                        var path = ShortestPath(start, candidate, c => IsBlockedForMove(c, start, candidate, passability));
                        if (path == null) continue;

                        int enemyDist = DistanceToEnemy(candidate, enemyCells);
                        if (enemyDist > meleeRange) continue;

                        int len = path.Count;
                        if (enemyDist < bestEnemyDist || (enemyDist == bestEnemyDist && len < bestLen))
                        {
                            bestEnemyDist = enemyDist;
                            bestLen = len;
                            bestPath = path;
                            bestLanding = candidate;
                        }
                    }
                }
            }

            if (bestPath != null)
            {
                landing = bestLanding;
                return true;
            }

            landing = target;
            bestPath = null;
            return false;
        }

        static int DistanceToEnemy(Hex candidate, IReadOnlyList<Hex> enemyCells)
        {
            int best = int.MaxValue;
            if (enemyCells == null) return best;
            for (int i = 0; i < enemyCells.Count; i++)
            {
                int dist = Hex.Distance(candidate, enemyCells[i]);
                if (dist < best)
                    best = dist;
            }

            return best;
        }

        IReadOnlyList<Hex> GetEnemyCellsForTarget(Hex target)
        {
            if (_occ == null)
                return new[] { target };

            var enemy = _occ.Get(target);
            if (enemy == null || enemy == SelfActor)
                return new[] { target };

            var cells = _occ.CellsOf(enemy);
            if (cells != null && cells.Count > 0)
                return cells;

            return new[] { target };
        }

        static readonly Hex[] Neigh =
        {
            new Hex(+1, 0),
            new Hex(+1, -1),
            new Hex(0, -1),
            new Hex(-1, 0),
            new Hex(-1, +1),
            new Hex(0, +1)
        };

        List<Hex> ShortestPath(Hex start, Hex goal, System.Func<Hex, bool> isBlocked)
        {
            var came = new Dictionary<Hex, Hex>();
            var q = new Queue<Hex>();
            q.Enqueue(start);
            came[start] = start;

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(goal)) break;
                for (int i = 0; i < Neigh.Length; i++)
                {
                    var nb = new Hex(cur.q + Neigh[i].q, cur.r + Neigh[i].r);
                    if (came.ContainsKey(nb)) continue;
                    if (isBlocked != null && isBlocked(nb)) continue;
                    came[nb] = cur;
                    q.Enqueue(nb);
                }
            }

            if (!came.ContainsKey(goal)) return null;
            var path = new List<Hex> { goal };
            var c = goal;
            while (!c.Equals(start))
            {
                c = came[c];
                path.Add(c);
            }
            path.Reverse();
            return path;
        }

        void OnAttackStrikeFired(Unit unit, int comboIndex)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            if (!_pendingAttack.active || _pendingAttack.unit != unit)
                return;
            if (_pendingAttack.strikeProcessed)
                return;
            if (_pendingAttack.comboIndex > 0 && comboIndex > 0 && comboIndex != _pendingAttack.comboIndex)
                return;
            if (!IsEnemyHex(_pendingAttack.target))
            {
                ClearPendingAttack();
                return;
            }

            AttackEventsV2.RaiseHit(unit, _pendingAttack.target);
            _pendingAttack.strikeProcessed = true;
        }

        void OnAttackAnimationEnded(Unit unit, int comboIndex)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            if (_pendingAttack.unit != unit)
                return;
            if (_pendingAttack.comboIndex > 0 && comboIndex > 0 && comboIndex != _pendingAttack.comboIndex)
                return;

            ClearPendingAttack();
        }
        void OnAttackMoveFinished(Unit unit, Hex end)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            if (!IsMyUnit(unit))
                return;
            ClearTempReservations("AttackMoveFinished", true);
        }

        void OnSideEnded(bool isPlayerSide)
        {
            if (!Application.isPlaying || Dead(this) || !isActiveAndEnabled)
                return;
            var tm = _boundTurnManager != null ? _boundTurnManager : turnManager;
            if (tm == null)
                return;
            var unit = ResolveSelfUnit();
            if (unit == null)
                return;

            bool belongs = isPlayerSide ? tm.IsPlayerUnit(unit) : tm.IsEnemyUnit(unit);
            if (!belongs)
                return;

            ResetComboCounters();
            ClearTempReservations("SideEnd", true);
        }
        void ClearPendingAttack()
        {
            _pendingAttack.active = false;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = null;
            _pendingAttack.target = default;
            _pendingAttack.comboIndex = 0;
        }
        bool IsMyUnit(Unit unit)
        {
            var self = ResolveSelfUnit();
            return self != null && self == unit;
        }

        void ReserveTemp(TGD.CoreV2.Hex cell)
        {
            if (_tempReservedThisAction.Contains(cell))
                return;

            var ctx = ResolveCtx();
            if (ctx == null) return;
            if (!TGD.HexBoard.OccTempOps.Reserve(ctx, cell))
                return;

            _tempReservedThisAction.Add(cell);
            string unitLabel = TurnManagerV2.FormatUnitLabel(ResolveSelfUnit());
            if (debugLog) LogInternal($"[Occ] TempReserve U={unitLabel} @{cell}");
        }

        void ClearTempReservations(string reason, bool logAlways = false)
        {
            int tracked = _tempReservedThisAction.Count;
            int occCleared = 0;

            var ctx = ResolveCtx();
            if (ctx != null)
                occCleared = TGD.HexBoard.OccTempOps.ClearFor(ctx);

            int count = Mathf.Max(tracked, occCleared);
            _tempReservedThisAction.Clear();

            if (logAlways || (debugLog && count > 0))
            {
                string unitLabel = TurnManagerV2.FormatUnitLabel(ResolveSelfUnit());
                LogInternal($"[Occ] TempClear U={unitLabel} count={count} ({reason})");
            }
        }


        int ResolveComboIndex()
        {
            return Mathf.Clamp(Mathf.Max(1, _attacksThisTurn), 1, 4);
        }


        void TriggerAttackAnimation(Unit unit, Hex target)
        {
            if (unit == null)
                return;

            int comboIndex = ResolveComboIndex();
            ClearPendingAttack();
            _pendingAttack.active = true;
            _pendingAttack.strikeProcessed = false;
            _pendingAttack.unit = unit;
            _pendingAttack.target = target;
            _pendingAttack.comboIndex = comboIndex;

            AttackEventsV2.RaiseAttackAnimation(unit, comboIndex);
        }

        void RefreshOccupancy()
        {
            if (_bridge is PlayerOccupancyBridge playerBridge && playerBridge != null)
            {
                if (!OccRuntimeSwitch.UseIOccWrites)
                    playerBridge.EnsurePlacedNow();
                var bridgeService = playerBridge.occupancyService;
                if (bridgeService != null)
                {
                    if (occupancyService == null)
                        occupancyService = bridgeService;
                    _occ = bridgeService.Get();
                    return;
                }
            }
            else
            {
                if (!OccRuntimeSwitch.UseIOccWrites)
                    _bridge?.EnsurePlacedNow();
            }

            if (occupancyService != null)
                _occ = occupancyService.Get();
        }

        PlayerOccupancyBridge ResolvePlayerBridge()
            => UnitRuntimeBindingUtil.ResolvePlayerBridge(this, ctx, bridgeOverride, _playerBridge);

        void TryResolveBridge()
        {
            var desired = ResolvePlayerBridge();
            if (!ReferenceEquals(_playerBridge, desired))
                _playerBridge = desired;

            if (bridgeOverride != null && !ReferenceEquals(_bridge, bridgeOverride))
                _bridge = bridgeOverride;

            if (_bridge == null && desired != null)
                _bridge = desired;

            if (_bridge == null)
            {
                var local = GetComponent<PlayerOccupancyBridge>();
                if (local != null)
                    _bridge = local;
            }

            if (_bridge == null)
            {
                var parentBridge = GetComponentInParent<PlayerOccupancyBridge>(true);
                if (parentBridge != null)
                    _bridge = parentBridge;
            }

            if (_bridge == null && ctx != null)
                _bridge = ctx.GetComponentInParent<IActorOccupancyBridge>(true);

            if (_bridge == null)
                _bridge = GetComponentInParent<IActorOccupancyBridge>(true);

            if (_playerBridge == null)
                _playerBridge = _bridge as PlayerOccupancyBridge;
        }

        bool EnsureBound()
        {
            TryResolveBridge();
            // 1) 桥优先：bridgeOverride → ctx 体系 → 父链
            var desiredBridge = ResolvePlayerBridge();
            if (!ReferenceEquals(_playerBridge, desiredBridge))
                _playerBridge = desiredBridge;

            UpdateBridgeSubscription(desiredBridge);

            if (desiredBridge != null)
                _bridge = desiredBridge;
            else if (_bridge == null)
            {
                if (ctx != null) _bridge = ctx.GetComponentInParent<IActorOccupancyBridge>(true);
                if (_bridge == null) _bridge = GetComponentInParent<IActorOccupancyBridge>(true);
            }

            // 2) 占位服务：优先 turnManager.occupancyService → 桥里的 → 已挂的
            if (occupancyService == null)
                occupancyService = (turnManager?.occupancyService)
                                   ?? (_playerBridge?.occupancyService)
                                   ?? occupancyService;

            RefreshOccupancy();

            return authoring?.Layout != null
                && ResolveSelfUnit() != null
                && _occ != null
                && SelfActor != null;
        }

        void UpdateBridgeSubscription(PlayerOccupancyBridge desired)
        {
            if (_boundPlayerBridge == desired)
                return;

            if (_boundPlayerBridge != null)
                _boundPlayerBridge.AnchorChanged -= HandleAnchorChanged;

            _boundPlayerBridge = null;

            if (!isActiveAndEnabled)
                return;

            if (desired != null)
            {
                desired.AnchorChanged += HandleAnchorChanged;
                _boundPlayerBridge = desired;
            }
        }

    }
}
