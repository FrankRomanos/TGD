// File: TGD.HexBoard/HexClickMover.cs
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TGD.CombatV2.Integration;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using TGD.HexBoard;
using TGD.HexBoard.Path;
using UnityEngine;

namespace TGD.CombatV2
{
    /// <summary>
    /// 点击移动（占位版）：BFS 可达 + 一次性转向 + 逐格 Tween + HexOccupancy 碰撞
    /// </summary>
    [RequireComponent(typeof(PlayerOccupancyBridge))]
    public sealed class HexClickMover : MonoBehaviour, IActionToolV2, IActionExecReportV2, ICooldownKeyProvider, IBindContext
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;     // 提供 UnitRef/SyncView
        public HexBoardTiler tiler;      // 着色
        public DefaultTargetValidator targetValidator;
        public HexOccupancyService occupancyService;
        [Header("Context (optional)")]           // ★ 新增
        public UnitRuntimeContext ctx;            // ★ 新增
        public TurnManagerV2 turnManager;

        [Header("Debug")]                         // ★ 新增（若你已有 debugLog 就跳过）
        public bool debugLog = true;
        public bool suppressInternalLogs = false;

        // === 新增：临时回合时间（无 TurnManager 时自管理） ===
        [Header("Turn Manager Binding")]
        public bool UseTurnManager = true;
        public bool ManageEnergyLocally = false;
        public bool ManageTurnTimeLocally = false;

        [Header("Turn Time (TEMP no-TM)")]
        public bool simulateTurnTime = true;
        [Tooltip("基础回合时间（秒）")]
        public int baseTurnSeconds = 6;
        [SerializeField, Tooltip("当前剩余回合秒数（运行时）")]
        int _turnSecondsLeft = -1;
        int MaxTurnSeconds => Mathf.Max(0, baseTurnSeconds + (ctx ? ctx.Speed : 0));
        public string CooldownKey => ResolveMoveActionId();
        void EnsureTurnTimeInited()
        {
            if (!ManageTurnTimeLocally) return;
            if (_turnSecondsLeft < 0)
            {
                _turnSecondsLeft = MaxTurnSeconds;
            }
        }

        public void AttachTurnManager(TurnManagerV2 tm)
        {
            turnManager = tm;
            UseTurnManager = tm != null;
            simulateTurnTime = !UseTurnManager;
            ManageTurnTimeLocally = !UseTurnManager;
            ManageEnergyLocally = !UseTurnManager;
            if (!ManageTurnTimeLocally)
                _turnSecondsLeft = -1;
            if (occupancyService == null && tm != null)
                occupancyService = tm.occupancyService;
            ReacquireOcc();
        }

        public void BindContext(UnitRuntimeContext context, TurnManagerV2 tm)
        {
            ctx = context;
            if (_cachedResolvedCtx != ctx)
            {
                _cachedResolvedCtx = null;
                _cachedResolvedUnit = null;
            }
            AttachTurnManager(tm);
        }

        public void Bind(UnitRuntimeContext boundCtx, TurnManagerV2 tm)
        {
            BindContext(boundCtx, tm);
        }
        [Header("Action Config & Cost")]
        public MonoBehaviour costProvider;
        IMoveCostService _cost;

        [Tooltip("精准移动：进入减速地形时永久降低 MoveRate（已禁用保留字段）")]
        public bool applyPermanentSlowInPreciseMove = false;

        [Header("Picking")]
        public Camera pickCamera;
        public LayerMask pickMask;
        public float rayMaxDistance = 2000f;
        public float pickPlaneY = 0.01f;

        [Header("Motion")]
        public float stepSeconds = 0.12f;      // 仍保留：作为“基准MR时”的大致节奏参考
        [Min(0.001f)] public float minStepSeconds = 0.06f;  // 新增：每步最小时长，避免视觉闪烁
        public float y = 0.01f;

        [Header("Blocking")]
        public bool blockByUnits = true;
        public bool blockByPhysics = true;
        public LayerMask obstacleMask = 0;
        [Range(0.2f, 1.2f)] public float physicsRadiusScale = 0.9f;
        public float physicsProbeHeight = 2f;
        public bool includeTriggerColliders = false;
        public bool showBlockedAsRed = false;

        [Header("Visuals")]
        public Color rangeColor = new(0.2f, 0.8f, 1f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        [Header("Environment (optional)")]
        public HexEnvironmentSystem env;                 // 可不挂，按1倍速

        [Tooltip("地形：进入后永久学习一次（已禁用保留字段）")]
        public bool terrainLearnOnce = false;

        [Header("Sticky Slow (optional)")]
        public MoveRateStatusRuntime status;        // 黏性修饰器运行时（可选）
        public MonoBehaviour stickySource;          // 任意实现了 IStickySlowSource 的组件
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;
        const float MULT_MIN = 0.01f;
        const float MULT_MAX = 100f;
        IStickyMoveSource _sticky;

        float MoveRateMin => ctx != null ? ctx.MoveRateMin : MoveRateRules.DefaultMin;
        float MoveRateMax => ctx != null ? ctx.MoveRateMax : MoveRateRules.DefaultMax;
        int MoveRateMinInt => ctx != null ? ctx.MoveRateMin : MoveRateRules.DefaultMinInt;
        int MoveRateMaxInt => ctx != null ? ctx.MoveRateMax : MoveRateRules.DefaultMaxInt;

        float ClampMoveRate(float value) => Mathf.Clamp(value, MoveRateMin, MoveRateMax);
        int ClampMoveRateInt(int value) => Mathf.Clamp(value, MoveRateMinInt, MoveRateMaxInt);

        public float ResolveMoveBaseSecondsRaw()
            => ctx != null ? ctx.MoveBaseSeconds : MoveProfileRules.DefaultSeconds;

        public int ResolveMoveBudgetSeconds()
        {
            if (ctx != null)
                return ctx.MoveBaseSecondsCeil;
            return Mathf.Max(1, Mathf.CeilToInt(MoveProfileRules.DefaultSeconds));
        }

        public int ResolveMoveEnergyPerSecond()
            => ctx != null ? ctx.MoveEnergyPerSecond : MoveProfileRules.DefaultEnergyPerSecond;

        public float ResolveMoveRefundThreshold()
            => ctx != null ? ctx.MoveRefundThresholdSeconds : MoveProfileRules.DefaultRefundThresholdSeconds;

        public int ResolveFallbackSteps()
            => ctx != null ? ctx.MoveFallbackSteps : MoveProfileRules.DefaultFallbackSteps;

        public int ResolveStepsCap()
            => ctx != null ? ctx.MoveStepsCap : MoveProfileRules.DefaultStepsCap;

        public float ResolveMoveKeepDeg()
            => ctx != null ? ctx.MoveKeepDeg : MoveProfileRules.DefaultKeepDeg;

        public float ResolveMoveTurnDeg()
            => ctx != null ? ctx.MoveTurnDeg : MoveProfileRules.DefaultTurnDeg;

        public float ResolveMoveTurnSpeed()
            => ctx != null ? ctx.MoveTurnSpeedDegPerSec : MoveProfileRules.DefaultTurnSpeedDegPerSec;

        public float ResolveMoveCooldownSeconds()
            => ctx != null ? ctx.MoveCooldownSeconds : MoveProfileRules.DefaultCooldownSeconds;

        public string ResolveMoveActionId()
            => ctx != null ? ctx.MoveActionId : MoveProfileRules.DefaultActionId;

        Unit ResolveUnitFromContext()
        {
            var context = ctx;
            if (context == null)
                return null;

            if (_cachedResolvedCtx == context && _cachedResolvedUnit != null)
                return _cachedResolvedUnit;

            Unit unit = null;

            if (!s_ctxBoundUnitResolved)
            {
                var type = typeof(UnitRuntimeContext);
                s_ctxBoundUnitProperty = type.GetProperty("BoundUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (s_ctxBoundUnitProperty == null)
                {
                    s_ctxBoundUnitField = type.GetField("BoundUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? type.GetField("_boundUnit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                s_ctxBoundUnitResolved = true;
            }

            if (s_ctxBoundUnitProperty != null)
            {
                try { unit = s_ctxBoundUnitProperty.GetValue(context) as Unit; }
                catch { unit = null; }
            }

            if (unit == null && s_ctxBoundUnitField != null)
            {
                try { unit = s_ctxBoundUnitField.GetValue(context) as Unit; }
                catch { unit = null; }
            }

            if (unit == null && turnManager != null)
            {
                if (!s_tmUnitByContextResolved)
                {
                    s_tmUnitByContextField = typeof(TurnManagerV2).GetField("_unitByContext", BindingFlags.Instance | BindingFlags.NonPublic);
                    s_tmUnitByContextResolved = true;
                }

                if (s_tmUnitByContextField != null)
                {
                    try
                    {
                        if (s_tmUnitByContextField.GetValue(turnManager) is Dictionary<UnitRuntimeContext, Unit> map &&
                            map.TryGetValue(context, out var mapped) && mapped != null)
                        {
                            unit = mapped;
                        }
                    }
                    catch { unit = null; }
                }
            }

            if (unit != null)
            {
                _cachedResolvedCtx = context;
                _cachedResolvedUnit = unit;
            }

            return unit;
        }

        static Unit ResolveUnitFromActor(object actor)
        {
            if (actor == null)
                return null;

            if (actor is UnitGridAdapter adapter)
                return adapter.Unit;

            var type = actor.GetType();
            if (!s_actorUnitMemberCache.TryGetValue(type, out var member))
            {
                member = type.GetProperty("Unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? (MemberInfo)type.GetField("Unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_actorUnitMemberCache[type] = member;
            }

            if (member is PropertyInfo prop)
            {
                try { return prop.GetValue(actor) as Unit; }
                catch { return null; }
            }

            if (member is FieldInfo field)
            {
                try { return field.GetValue(actor) as Unit; }
                catch { return null; }
            }

            return null;
        }

        Unit ResolveUnitFromBridge()
        {
            if (_bridge == null)
                return null;

            try { return ResolveUnitFromActor(_bridge.Actor); }
            catch { return null; }
        }

        private Unit ResolveUnit()
        {
            var unit = ResolveUnitFromContext();
            if (unit != null)
                return unit;

            unit = ResolveUnitFromBridge();
            if (unit != null)
                return unit;

            return driver != null ? driver.UnitRef : null;
        }

        public void ReacquireOcc()
        {
            _occ = null;
            if (occupancyService != null)
                _occ = occupancyService.Get();
            if (_occ == null)
            {
                var svc = UnityEngine.Object.FindFirstObjectByType<HexOccupancyService>(FindObjectsInactive.Include);
                if (svc != null)
                    _occ = svc.Get();
            }
        }

        MoveCostSpec BuildCostSpec()
        {
            return new MoveCostSpec
            {
                actionId = ResolveMoveActionId(),
                energyPerSecond = Mathf.Max(0, ResolveMoveEnergyPerSecond()),
                cooldownSeconds = Mathf.Max(0f, ResolveMoveCooldownSeconds())
            };
        }

        struct MoveRateSnapshot
        {
            public int baseRate;
            public float buffMult;
            public float stickyMult;
            public int flatAfter;
            public float baseNoEnv;
            public float startEnvMult;
            public float mrClick;
            public bool startIsSticky;
        }

        // runtime
        bool _showing = false, _moving = false;
        bool _isAiming = false;
        bool _isExecuting = false;
        readonly Dictionary<Hex, List<Hex>> _paths = new();
        HexAreaPainter _painter;

        TargetingSpec _moveSpec;

        // 占位
        IActorOccupancyBridge _bridge;
        PlayerOccupancyBridge _playerBridge;
        HexOccupancy _occ;
        Unit _cachedResolvedUnit;
        UnitRuntimeContext _cachedResolvedCtx;
        static PropertyInfo s_ctxBoundUnitProperty;
        static FieldInfo s_ctxBoundUnitField;
        static bool s_ctxBoundUnitResolved;
        static FieldInfo s_tmUnitByContextField;
        static bool s_tmUnitByContextResolved;
        static readonly Dictionary<System.Type, MemberInfo> s_actorUnitMemberCache = new();
        bool _previewDirty = true;
        int _previewAnchorVersion = -1;
        int _planAnchorVersion = -1;
                             // === HUD 提示（可选）===
        [Header("HUD")]
        public bool showHudMessage = true;
        public float hudSeconds = 1.6f;

        string _hudMsg;
        float _hudMsgUntil;
        public string Id => "Move";
        public ActionKind Kind => ActionKind.Standard;
        int _reportUsedSeconds;
        int _reportRefundedSeconds;
        int _reportEnergyMoveNet;
        int _reportEnergyAtkNet;
        bool _reportFreeMove;
        bool _reportPending;
        string _reportRefundTag;
        public (int timeSec, int energy) PeekPlannedCost()
        {
            int seconds = ResolveMoveBudgetSeconds();
            int energyRate = ResolveMoveEnergyPerSecond();
            return (seconds, energyRate * seconds);
        }

        public struct PlannedMoveCost
        {
            public int moveSecs;
            public int moveEnergy;
            public bool valid;
        }

        public PlannedMoveCost PeekPlannedCost(Hex target)
        {
            int moveSecs = ResolveMoveBudgetSeconds();
            int moveEnergy = moveSecs * ResolveMoveEnergyPerSecond();

            var result = new PlannedMoveCost
            {
                moveSecs = moveSecs,
                moveEnergy = moveEnergy,
                valid = false
            };

            EnsureTurnTimeInited();
            RefreshStateForAim();
            _bridge?.EnsurePlacedNow();
            ReacquireOcc();

            if (!HasReadyDependencies(out _))
                return result;

            if (ctx != null && ctx.Entangled)
                return result;

            var unit = ResolveUnit();
            if (unit == null)
                return result;

            var targetCheck = ValidateMoveTarget(unit, target);
            if (!targetCheck.ok || targetCheck.plan != PlanKind.MoveOnly)
                return result;

            if (_playerBridge != null && _previewAnchorVersion != _playerBridge.AnchorVersion)
            {
                _previewDirty = true;
            }

            if (_previewDirty || !_paths.TryGetValue(target, out var path) || path == null || path.Count < 2)
            {
                if (!TryRebuildPathCache(out _))
                    return result;
            }

            if (_paths.TryGetValue(target, out var cached) && cached != null && cached.Count >= 2)
                result.valid = true;

            return result;
        }

        public (int moveSecs, int energyMove) GetPlannedCost()
        {
            var (timeSec, energy) = PeekPlannedCost();
            return (timeSec, energy);
        }

        public int ReportUsedSeconds => _reportPending ? _reportUsedSeconds : 0;
        public int ReportRefundedSeconds => _reportPending ? _reportRefundedSeconds : 0;
        public int ReportEnergyMoveNet => _reportPending ? _reportEnergyMoveNet : 0;
        public int ReportEnergyAtkNet => 0;
        public int ReportMoveEnergyNet => ReportEnergyMoveNet;
        public int ReportAttackEnergyNet => ReportEnergyAtkNet;
        public bool ReportFreeMoveApplied => _reportPending && _reportFreeMove;
        public string ReportRefundTag => _reportPending ? _reportRefundTag : null;
        public bool IsBusy => _moving;


        void ClearExecReport()
        {
            _reportUsedSeconds = 0;
            _reportRefundedSeconds = 0;
            _reportEnergyMoveNet = 0;
            _reportEnergyAtkNet = 0;
            _reportFreeMove = false;
            _reportPending = false;
            _reportRefundTag = null;
        }

        void SetExecReport(int plannedSeconds, int refundedSeconds, int energyMoveNet, bool freeMove, string refundTag)
        {
            _reportUsedSeconds = Mathf.Max(0, plannedSeconds);
            _reportRefundedSeconds = Mathf.Max(0, refundedSeconds);
            _reportEnergyMoveNet = energyMoveNet;
            _reportEnergyAtkNet = 0;
            _reportFreeMove = freeMove;
            _reportPending = true;
            _reportRefundTag = refundTag;
        }

        internal bool HasPendingExecReport => _reportPending;

        void LogInternal(string message)
        {
            if (!suppressInternalLogs && debugLog)
                Debug.Log(message, this);
        }
        // —— 每次进入/确认前，刷新一次“起点状态”（以后也可挂接技能/buff 刷新）——
        void RefreshStateForAim() { }

        void DumpReadiness(string reason)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[HexClickMover] Not ready. reason={reason}", this);
#endif
        }

        bool HasReadyDependencies(out string reason)
        {
            if (_occ == null)
                ReacquireOcc();
            reason = null;
            if (ctx == null) { reason = "noCtx"; return false; }
            if (ResolveUnit() == null) { reason = "noUnit"; return false; }
            if (UseTurnManager && turnManager == null) { reason = "noTM"; return false; }
            if (authoring == null) { reason = "noAuthoring"; return false; }
            if (authoring.Layout == null) { reason = "noLayout"; return false; }
            if (_occ == null) { reason = "noOccupancy"; return false; }
            if (_bridge == null) { reason = "noBridge"; return false; }
            if (SelfActor == null) { reason = "noActor"; return false; }
            if (pickCamera == null) { reason = "noPickCamera"; return false; }
            return true;
        }

        bool TryPrecheckAimInternal(bool raiseHud, out string reason)
        {
            EnsureTurnTimeInited();
            RefreshStateForAim();
            _bridge?.EnsurePlacedNow();
            if (!HasReadyDependencies(out reason))
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Move.NotReady] {name} reason={reason}", this);
#endif
                if (raiseHud)
                    HexMoveEvents.RaiseRejected(ResolveUnit(), MoveBlockReason.NotReady, null);
                return false;
            }

            var unit = ResolveUnit();

            if (ctx != null && ctx.Entangled)
            {
                if (raiseHud)
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.Entangled, null);
                reason = "(entangled)";
                return false;
            }

            int needSec = ResolveMoveBudgetSeconds();
            var costSpec = BuildCostSpec();
            if (UseTurnManager)
            {
                var budget = (turnManager != null && unit != null)
                    ? turnManager.GetBudget(unit)
                    : null;
                if (budget == null || !budget.HasTime(needSec))
                {
                    if (raiseHud)
                        HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                    reason = "(no-time)";
                    return false;
                }
            }
            else if (ManageTurnTimeLocally && _turnSecondsLeft < needSec)
            {
                if (raiseHud)
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                reason = "(no-time)";
                return false;
            }

            if (_cost != null)
            {
                if (UseTurnManager)
                {
                    int energyRate = Mathf.Max(0, costSpec.energyPerSecond);
                    if (energyRate > 0 && turnManager != null && unit != null)
                    {
                        var pool = turnManager.GetResources(unit);
                        if (pool != null && !pool.Has("Energy", energyRate))
                        {
                            if (raiseHud)
                                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                            reason = "(no-energy)";
                            return false;
                        }
                    }
                }
                else if (!_cost.HasEnough(unit, costSpec))
                {
                    if (raiseHud)
                        HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                    reason = "(no-energy)";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public bool TryPrecheckAim(out string reason, bool raiseHud = true)
        {
            return TryPrecheckAimInternal(raiseHud, out reason);
        }

        public void OnEnterAim()
        {
            if (!TryPrecheckAim(out _))
                return;
            _isAiming = true;
            _isExecuting = false;
            ShowRange();
        }
        public void OnExitAim()
        {
            _isAiming = false;
            HideRange();
        }
        public void OnHover(Hex hex) { /* 可选：做 hover 高亮 */ }

        public IEnumerator OnConfirm(Hex hex)
        {
            ClearExecReport();
            EnsureTurnTimeInited();
            RefreshStateForAim();
            _bridge?.EnsurePlacedNow();
            ReacquireOcc();

            if (!HasReadyDependencies(out var reason))
            {
                HexMoveEvents.RaiseRejected(ResolveUnit(), MoveBlockReason.NotReady, reason);
                yield break;
            }

            var unit = ResolveUnit();
            var targetCheck = ValidateMoveTarget(unit, hex);
            // Phase logging handled by CombatActionManagerV2.

            if (!targetCheck.ok || targetCheck.plan != PlanKind.MoveOnly)
            {
                RaiseTargetRejected(unit, targetCheck.reason);
                yield break;
            }

            int needSec = ResolveMoveBudgetSeconds();

            // 再做一次兜底预检查（避免竞态）
            if (!UseTurnManager && ManageTurnTimeLocally && _turnSecondsLeft < needSec)
            {
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                yield break;
            }
            var costSpec = BuildCostSpec();
            if (_cost != null && !_cost.HasEnough(unit, costSpec))
            {
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                yield break;
            }

            if (_playerBridge != null && _previewAnchorVersion != _playerBridge.AnchorVersion)
            {
                Debug.LogWarning($"[Guard] Move plan stale (previewV={_previewAnchorVersion} nowV={_playerBridge.AnchorVersion}). Rebuild.", this);
                ShowRange();
            }

            _planAnchorVersion = _playerBridge != null ? _playerBridge.AnchorVersion : -1;

            if (_paths.TryGetValue(hex, out var path) && path != null && path.Count >= 2)
            {
                yield return RunPathTween_WithTime(path, _planAnchorVersion);   // ★ 改：走带结算版本
            }
            else
            {
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.PathBlocked, null);
                yield break;
            }
        }

        IGridActor SelfActor => _bridge?.Actor as IGridActor;

        Hex CurrentAnchor
        {
            get
            {
                if (_bridge != null)
                    return _bridge.CurrentAnchor;
                return SelfActor != null ? SelfActor.Anchor : Hex.Zero;
            }
        }

        void Awake()
        {
            _cost = costProvider as IMoveCostService;
            _painter = new HexAreaPainter(tiler);
            // ★ 统一解析：优先 ctx.stats，其次向上找
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);

            _bridge = GetComponentInParent<IActorOccupancyBridge>(true);
            _playerBridge = _bridge as PlayerOccupancyBridge ?? GetComponentInParent<PlayerOccupancyBridge>(true);
            if (_bridge == null && _playerBridge != null)
                _bridge = _playerBridge;
            if (driver == null)
                driver = GetComponentInParent<HexBoardTestDriver>(true);
            if (!occupancyService)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);
            if (!targetValidator)
                targetValidator = GetComponentInParent<DefaultTargetValidator>(true);

#if UNITY_EDITOR
            var bridges = GetComponentsInParent<PlayerOccupancyBridge>(true);
            if (bridges != null && bridges.Length > 1)
                Debug.LogError($"[Guard] Multiple PlayerOccupancyBridge in parents: {bridges.Length}. Keep ONE.", this);
#endif

            if (!occupancyService && driver != null)
            {
                occupancyService = driver.GetComponentInParent<HexOccupancyService>(true);
                if (!occupancyService && driver.authoring != null)
                    occupancyService = driver.authoring.GetComponent<HexOccupancyService>() ?? driver.authoring.GetComponentInParent<HexOccupancyService>(true);
            }

            if (occupancyService == null && _playerBridge != null && _playerBridge.occupancyService)
                occupancyService = _playerBridge.occupancyService;

            _moveSpec = new TargetingSpec
            {
                occupant = TargetOccupantMask.Empty,
                terrain = TargetTerrainMask.NonObstacle,
                allowSelf = false,
                requireOccupied = false,
                requireEmpty = true,
                maxRangeHexes = -1
            };
        }

        void Start()
        {
            tiler?.EnsureBuilt();

            driver?.EnsureInit();

            if (_playerBridge == null)
                _playerBridge = GetComponentInParent<PlayerOccupancyBridge>(true);
            if (_bridge == null)
                _bridge = GetComponentInParent<IActorOccupancyBridge>(true) ?? _playerBridge;

            if (occupancyService)
                _occ = occupancyService.Get();
            else if (_playerBridge != null && _playerBridge.occupancyService)
            {
                occupancyService = _playerBridge.occupancyService;
                _occ = occupancyService ? occupancyService.Get() : null;
            }

            if (occupancyService == null && driver != null)
            {
                occupancyService = driver.GetComponentInParent<HexOccupancyService>(true);
                if (!occupancyService && driver.authoring != null)
                    occupancyService = driver.authoring.GetComponent<HexOccupancyService>() ?? driver.authoring.GetComponentInParent<HexOccupancyService>(true);
                if (occupancyService)
                    _occ = occupancyService.Get();
            }

            if (_occ == null)
                ReacquireOcc();
            _bridge?.EnsurePlacedNow();
        }

        void OnEnable()
        {
            if (_playerBridge == null)
                _playerBridge = GetComponentInParent<PlayerOccupancyBridge>(true);
            if (_bridge == null)
                _bridge = GetComponentInParent<IActorOccupancyBridge>(true) ?? _playerBridge;
            if (_playerBridge != null)
                _playerBridge.AnchorChanged += HandleAnchorChanged;
        }

        void OnDisable()
        {
            if (_playerBridge != null)
                _playerBridge.AnchorChanged -= HandleAnchorChanged;
            _painter?.Clear();
            _paths.Clear();
            _showing = false;
            _occ = null;
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
        }

        void OnDestroy()
        {
            if (_playerBridge != null)
                _playerBridge.AnchorChanged -= HandleAnchorChanged;
        }

        void HandleAnchorChanged(Hex anchor, int version)
        {
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
            if (_showing)
            {
                _painter.Clear();
                _paths.Clear();
            }
        }

        void Update()
        {
            if (!_showing || _moving) return;
            if (authoring == null || authoring.Layout == null) return;
            if (_bridge == null || SelfActor == null) return;
        }

        MoveRateSnapshot BuildMoveRates(Hex start)
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
            buffMult = Mathf.Clamp(buffMult, MULT_MIN, MULT_MAX);
            stickyMult = Mathf.Clamp(stickyMult, MULT_MIN, MULT_MAX);

            float combined = Mathf.Clamp(buffMult * stickyMult, MULT_MIN, MULT_MAX);
            float baseNoEnv = StatsMathV2.MR_MultiThenFlat(
                baseRate,
                new[] { combined },
                flatAfter,
                MoveRateMin,
                MoveRateMax);
            baseNoEnv = ClampMoveRate(baseNoEnv);

            var startSample = SampleStepModifier(start);
            float startEnv = Mathf.Clamp(startSample.Multiplier <= 0f ? 1f : startSample.Multiplier, ENV_MIN, ENV_MAX);
            float startUse = startSample.Sticky ? 1f : startEnv;
            startUse = Mathf.Clamp(startUse, ENV_MIN, ENV_MAX);
            float mrClick = ClampMoveRate(baseNoEnv * startUse);

            return new MoveRateSnapshot
            {
                baseRate = baseRate,
                buffMult = buffMult,
                stickyMult = stickyMult,
                flatAfter = flatAfter,
                baseNoEnv = baseNoEnv,
                startEnvMult = startEnv,
                mrClick = mrClick,
                startIsSticky = startSample.Sticky
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

            int steps = Mathf.Max(1, ResolveFallbackSteps());
            float seconds = Mathf.Max(0.1f, ResolveMoveBaseSecondsRaw());
            float mr = steps / Mathf.Max(0.1f, seconds);
            return ClampMoveRateInt(Mathf.RoundToInt(mr));
        }

        bool TryRebuildPathCache(out MovableRangeResult result)
        {
            result = null;

            _bridge?.EnsurePlacedNow();
            ReacquireOcc();

            if (authoring?.Layout == null || _bridge == null || SelfActor == null)
                return false;

            if (_occ == null)
                return false;

            var layout = authoring.Layout;
            var startHex = CurrentAnchor;

            var unit = ResolveUnit();

            var rates = BuildMoveRates(startHex);

            bool startGivesSticky = rates.startIsSticky;
            float mrNoEnv = ClampMoveRate(rates.baseNoEnv);
            float startMultUse = startGivesSticky ? 1f : Mathf.Clamp(rates.startEnvMult, ENV_MIN, ENV_MAX);
            float mrPreview = ClampMoveRate(mrNoEnv * startMultUse);

            int timeSec = ResolveMoveBudgetSeconds();
            int cap = ResolveStepsCap();
            int steps = Mathf.Min(cap, StatsMathV2.StepsAllowedF32(mrPreview, timeSec));

            var passability = PassabilityFactory.ForMove(_occ, SelfActor, startHex);

            var physicsBlocker =
                (blockByPhysics && obstacleMask != 0)
                    ? HexAreaUtil.MakeDefaultBlocker(
                        authoring,
                        driver?.Map,
                        startHex,
                        blockByUnits: false,
                        blockByPhysics: true,
                        obstacleMask: obstacleMask,
                        physicsRadiusScale: physicsRadiusScale,
                        physicsProbeHeight: physicsProbeHeight,
                        includeTriggerColliders: false,
                        y: y)
                    : null;

            bool Block(Hex cell)
            {
                if (layout != null && !layout.Contains(cell)) return true;

                if (blockByUnits)
                {
                    if (passability != null && passability.IsBlocked(cell))
                        return true;

                    if (passability == null && (_occ == null || !_occ.CanPlaceIgnoringTemp(SelfActor, cell, SelfActor.Facing, ignore: SelfActor)))
                        return true;
                }

                if (physicsBlocker != null && physicsBlocker(cell)) return true;

                if (env != null && env.IsPit(cell)) return true;

                return false;
            }

            var previewMap = new HexBoardMap<Unit>(layout);
            if (unit != null)
                previewMap.Set(unit, startHex);

            result = HexMovableRange.Compute(layout, previewMap, startHex, steps, Block);

            _paths.Clear();
            foreach (var kv in result.Paths)
                _paths[kv.Key] = kv.Value;

            _previewDirty = false;
            _previewAnchorVersion = _playerBridge != null ? _playerBridge.AnchorVersion : -1;

            return true;
        }

        // ===== 外部 UI 调用 =====
        public void ShowRange()
        {
            if (!_isAiming || _isExecuting)
                return;
#if UNITY_EDITOR
            var unit = ResolveUnit();
            var unitPos = unit != null ? unit.Position : Hex.Zero;
            var anchor = CurrentAnchor;
            var occOk = _bridge != null;
            var label = TurnManagerV2.FormatUnitLabel(unit);
            LogInternal($"[Probe][MoveAim] unit={label} pos={unitPos} anchor={anchor} occReady={occOk} bridge={_bridge?.GetType().Name}");
#endif
            _bridge?.EnsurePlacedNow();
            ReacquireOcc();

            var resolvedUnit = ResolveUnit();
            if (!HasReadyDependencies(out var reason))
            {
                HexMoveEvents.RaiseRejected(resolvedUnit, MoveBlockReason.NotReady, reason);
                return;
            }

            _painter.Clear();

            if (ctx != null && ctx.Entangled)
            {
                _paths.Clear();
                _previewDirty = true;
                HexMoveEvents.RaiseRejected(resolvedUnit, MoveBlockReason.Entangled, null);
                _showing = true;
                return;
            }

            if (!TryRebuildPathCache(out var result) || result == null)
            {
                _paths.Clear();
                _previewDirty = true;
                HexMoveEvents.RaiseRejected(resolvedUnit, MoveBlockReason.PathBlocked, null);
                return;
            }

            _painter.Paint(result.Paths.Keys, rangeColor);
            if (showBlockedAsRed)
                _painter.Paint(result.Blocked, invalidColor);

            HexMoveEvents.RaiseRangeShown(resolvedUnit, result.Paths.Keys);
            _showing = true;
        }

        public void HideRange()
        {
            _painter.Clear();
            _showing = false;
            _previewDirty = true;
            _previewAnchorVersion = -1;
            _planAnchorVersion = -1;
            HexMoveEvents.RaiseRangeHidden();
        }

        TargetCheckResult ValidateMoveTarget(Unit unit, Hex hex)
        {
            if (targetValidator == null || _moveSpec == null || unit == null)
                return new TargetCheckResult { ok = false, reason = TargetInvalidReason.Unknown, hit = HitKind.None, plan = PlanKind.None };
            return targetValidator.Check(unit, hex, _moveSpec);
        }

        void RaiseTargetRejected(Unit unit, TargetInvalidReason reason)
        {
            var (mapped, message) = MapMoveReject(reason);
            // Phase logging handled by CombatActionManagerV2.
            HexMoveEvents.RaiseRejected(unit, mapped, message);
        }

        internal void HandleConfirmAbort(Unit unit, string reason)
        {
            unit ??= ResolveUnit();
            (MoveBlockReason mapped, string message) = reason switch
            {
                "lackTime" => (MoveBlockReason.NoBudget, "No More Time"),
                "lackEnergy" => (MoveBlockReason.NotEnoughResource, "Not enough energy."),
                "cooldown" => (MoveBlockReason.OnCooldown, "Move is on cooldown."),
                "targetInvalid" => (MoveBlockReason.PathBlocked, "Invalid target."),
                "notReady" => (MoveBlockReason.NotReady, "Not ready."),
                _ => (MoveBlockReason.NotReady, "Action aborted.")
            };
            HexMoveEvents.RaiseRejected(unit, mapped, message);
        }

        static (MoveBlockReason reason, string message) MapMoveReject(TargetInvalidReason reason)
        {
            switch (reason)
            {
                case TargetInvalidReason.Self:
                    return (MoveBlockReason.PathBlocked, "Self not allowed.");
                case TargetInvalidReason.Friendly:
                    return (MoveBlockReason.PathBlocked, "Cannot move onto ally.");
                case TargetInvalidReason.EnemyNotAllowed:
                    return (MoveBlockReason.PathBlocked, "Cannot move onto enemy.");
                case TargetInvalidReason.EmptyNotAllowed:
                    return (MoveBlockReason.PathBlocked, "Target must be occupied.");
                case TargetInvalidReason.Blocked:
                    return (MoveBlockReason.PathBlocked, "Blocked by obstacle.");
                case TargetInvalidReason.OutOfRange:
                    return (MoveBlockReason.NoSteps, "Out of range.");
                case TargetInvalidReason.None:
                    return (MoveBlockReason.PathBlocked, "Invalid target.");
                default:
                    return (MoveBlockReason.PathBlocked, "Invalid target.");
            }
        }

        // ===== 逐格移动 + 起步一次性转向 + 占位提交 =====
        IEnumerator RunPathTween_WithTime(List<Hex> path, int plannedAnchorVersion)
        {
            if (path == null || path.Count < 2) yield break;

            _isExecuting = true;
            var unit = ResolveUnit();

            try
            {
                if (_playerBridge != null && plannedAnchorVersion >= 0 && plannedAnchorVersion != _playerBridge.AnchorVersion)
                {
                    Debug.LogWarning($"[Guard] Anchor changed before execute (planV={plannedAnchorVersion} nowV={_playerBridge.AnchorVersion}). Abort.", this);
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.PathBlocked, "Anchor changed.");
                    yield break;
                }

                _bridge?.EnsurePlacedNow();
                ReacquireOcc();
                if (!HasReadyDependencies(out var reason))
                {
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotReady, reason);
                    yield break;
                }

                int requiredSec = ResolveMoveBudgetSeconds();

                if (!UseTurnManager && ManageTurnTimeLocally && _turnSecondsLeft < requiredSec)
                {
                    HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                    yield break;
                }

                var costSpec = BuildCostSpec();
                if (_cost != null)
                {
                    if (_cost.IsOnCooldown(unit, costSpec))
                    { HexMoveEvents.RaiseRejected(unit, MoveBlockReason.OnCooldown, null); yield break; }
                    if (!_cost.HasEnough(unit, costSpec))
                    { HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null); yield break; }

                    if (!UseTurnManager && ManageEnergyLocally)
                        _cost.Pay(unit, costSpec);
                }

                var rates = BuildMoveRates(path[0]);
                var startAnchor = CurrentAnchor;
                var passability = PassabilityFactory.ForMove(_occ, SelfActor, startAnchor);

                float refundThreshold = Mathf.Max(0.01f, ResolveMoveRefundThreshold());

                var sim = MoveSimulator.Run(
                    path,
                    rates.baseNoEnv,
                    rates.mrClick,
                    requiredSec,
                    SampleStepModifier,
                    refundThreshold,
                    debugLog,
                    MoveRateMin,
                    MoveRateMax
                );
                var reached = sim.ReachedPath;

                int refunded = Mathf.Max(0, sim.RefundedSeconds);
                int spentSec = Mathf.Max(0, requiredSec - refunded);
                var stepRates = sim.StepEffectiveRates;
                int energyNet = (requiredSec - refunded) * Mathf.Max(0, costSpec.energyPerSecond);

                if (reached == null || reached.Count < 2)
                {
                    if (!UseTurnManager && ManageEnergyLocally)
                        _cost?.RefundSeconds(unit, costSpec, requiredSec);
                    if (!UseTurnManager && ManageTurnTimeLocally)
                        _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft + requiredSec);
                    HexMoveEvents.RaiseTimeRefunded(unit, requiredSec);
                    yield break;
                }

                string refundTag = refunded > 0 ? "Speed_Adjust" : null;
                SetExecReport(requiredSec, refunded, energyNet, false, refundTag);
                var hexSpace = HexSpace.Instance;
                if (hexSpace == null)
                {
                    Debug.LogWarning("[HexClickMover] HexSpace instance is missing.", this);
                    yield break;
                }
                _moving = true;
                Transform view = (driver != null && driver.unitView != null) ? driver.unitView : transform;
                if (driver != null && driver.unitView != null && unit != null)
                {
                    var fromW = hexSpace.HexToWorld(reached[0], y);
                    var toW = hexSpace.HexToWorld(reached[^1], y);
                    float keep = ResolveMoveKeepDeg();
                    float turn = ResolveMoveTurnDeg();
                    float speed = ResolveMoveTurnSpeed();

                    var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(unit.Facing, fromW, toW, keep, turn);
                    yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                    unit.Facing = nf;
                    if (SelfActor != null) SelfActor.Facing = nf;
                }

                HexMoveEvents.RaiseMoveStarted(unit, reached);

                string unitLabel = TurnManagerV2.FormatUnitLabel(unit);
                bool truncatedByBudget = (reached.Count < path.Count);
                bool stoppedByExternal = false;

                for (int i = 1; i < reached.Count; i++)
                {
                    if (ctx != null && ctx.Entangled)
                    {
                        HexMoveEvents.RaiseRejected(unit, MoveBlockReason.Entangled, "Break Move!");
                        stoppedByExternal = true;
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
                        HexMoveEvents.RaiseRejected(unit, MoveBlockReason.PathBlocked, "Pit");
                        stoppedByExternal = true;
                        break;
                    }

                    HexMoveEvents.RaiseMoveStep(unit, from, to, i, reached.Count - 1);

                    var fromW = hexSpace.HexToWorld(from, y);
                    var toW = hexSpace.HexToWorld(to, y);

                    float effMR = (stepRates != null && (i - 1) < stepRates.Count)
                        ? stepRates[i - 1]
                        : ClampMoveRate(rates.baseNoEnv);
                    float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(MoveRateMin, effMR));

                    float t = 0f;
                    while (t < 1f)
                    {
                        t += Time.deltaTime / stepDuration;
                        if (view != null) view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                        yield return null;
                    }

                    var commitFacing = SelfActor != null ? SelfActor.Facing : unit.Facing;
                    _bridge?.MoveCommit(to, commitFacing);
                    if (_sticky != null && status != null &&
                          _sticky.TryGetSticky(to, out var stickM, out var stickTurns, out var tag) &&
                          stickTurns > 0 && !Mathf.Approximately(stickM, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, stickM, stickTurns, to.ToString());
                        LogInternal($"[Sticky] Apply U={unitLabel} tag={tag}@{to} mult={stickM:F2} turns={stickTurns}");
                    }

                    if (driver != null && driver.Map != null)
                    { if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to); }
                    if (unit != null)
                        unit.Position = to;
                    if (driver != null)
                        driver.SyncView();
                }

                var finalAnchor = CurrentAnchor;
                var finalFacing = SelfActor != null ? SelfActor.Facing : unit?.Facing ?? Facing4.PlusQ;
                _bridge?.MoveCommit(finalAnchor, finalFacing);

                _moving = false;
                HexMoveEvents.RaiseMoveFinished(unit, CurrentAnchor);
                if (truncatedByBudget && !stoppedByExternal)
                {
                    HexMoveEvents.RaiseNoMoreTime(unit);
                    LogInternal("[Move] No more time.");
                }

                if (!UseTurnManager && ManageTurnTimeLocally)
                {
                    _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft - spentSec);
                }
                if (refunded > 0)
                {
                    if (!UseTurnManager && ManageEnergyLocally)
                        _cost?.RefundSeconds(unit, costSpec, refunded);
                    HexMoveEvents.RaiseTimeRefunded(unit, refunded);
                }
            }
            finally
            {
                _isExecuting = false;
                _planAnchorVersion = -1;
            }
        }
        int IActionExecReportV2.UsedSeconds => ReportUsedSeconds;
        int IActionExecReportV2.RefundedSeconds => ReportRefundedSeconds;

        void IActionExecReportV2.Consume()
        {
            ClearExecReport();
        }

    }
}
