// File: TGD.HexBoard/HexClickMover.cs
using System.Collections;
using System.Collections.Generic;
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
    [DisallowMultipleComponent]
    public sealed class HexClickMover : ActionToolBase, IActionToolV2, IActionExecReportV2, ICooldownKeyProvider, IBindContext
    {
        [SerializeField]
        [Tooltip("Action identifier used when registering with CombatActionManagerV2.")]
        string skillId = MoveProfileRules.DefaultSkillId;

        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTiler tiler;      // 着色
        public DefaultTargetValidator targetValidator;
        public HexOccupancyService occupancyService;

        [SerializeField] UnitRuntimeContext _ctx;
        UnitGridAdapter _selfActor;

        [Header("View (optional)")]
        [HideInInspector]
        public Transform viewOverride;                 // ★ 新增
        [Header("Debug")]                         // ★ 新增（若你已有 debugLog 就跳过）
        public bool debugLog = true;
        public bool suppressInternalLogs = false;

        TurnManagerV2 _turnManager;
        public string CooldownKey => ResolveMoveSkillId();

        Transform ResolveSelfView()
        {
            // 1) 先用你的通用解析（UnitViewHandle / UnitLocator / 显式 override）
            var t = UnitRuntimeBindingUtil.ResolveUnitView(this, ctx, viewOverride);
            if (t) return t;

            // 2) 兜底：_selfActor 本身就是 UnitGridAdapter（组件）——直接用它的 transform
            if (_selfActor is Component comp && comp) return comp.transform;

            return null;
        }
        // 替换整个属性
        Hex CurrentAnchor =>
            (_selfActor != null) ? _selfActor.Anchor
            : (OwnerUnit != null ? OwnerUnit.Position : Hex.Zero);

        public void AttachTurnManager(TurnManagerV2 tm)
        {
            _turnManager = tm;
        }

        public override void BindContext(UnitRuntimeContext context, TurnManagerV2 tm)
        {
            base.BindContext(context, tm);
            AttachTurnManager(tm);
        }
        [Tooltip("精准移动：进入减速地形时永久降低 MoveRate（已禁用保留字段）")]
        public bool applyPermanentSlowInPreciseMove = false;

        [Header("Picking")]
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
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

        string ResolveConfiguredSkillId()
            => string.IsNullOrEmpty(skillId) ? MoveProfileRules.DefaultSkillId : skillId.Trim();

        public string ResolveMoveSkillId()
            => ctx != null ? ctx.MoveSkillId : ResolveConfiguredSkillId();

        MoveCostSpec BuildCostSpec()
        {
            return new MoveCostSpec
            {
                skillId = ResolveMoveSkillId(),
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
        OccToken _previewToken;
        Hex _previewReservedTarget;
        bool _previewFallbackActive;
        HexAreaPainter _painter;

        TargetingSpec _moveSpec;

        // 占位
        HexOccupancy _occ;
        bool _previewDirty = true;
        Hex _previewAnchorSnapshot;
        bool _hasPreviewAnchorSnapshot;
        Hex _planAnchorSnapshot;
        bool _hasPlanAnchorSnapshot;
        // === HUD 提示（可选）===
        [Header("HUD")]
        public bool showHudMessage = true;
        public float hudSeconds = 1.6f;

        string _hudMsg;
        float _hudMsgUntil;
        public string Id => ResolveMoveSkillId();
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

            RefreshStateForAim();

            if (occupancyService) _occ = occupancyService.Get();

            if (ctx != null && ctx.Entangled) return result;

            var unit = OwnerUnit;
            if (authoring?.Layout == null || unit == null || _selfActor == null)
                return result;
            if (occupancyService != null && _occ == null)
                return result;

            var targetCheck = ValidateMoveTarget(unit, target);
            if (!targetCheck.ok || targetCheck.plan != PlanKind.MoveOnly)
                return result;

            if (_hasPreviewAnchorSnapshot && !_previewAnchorSnapshot.Equals(CurrentAnchor))
                _previewDirty = true;

            if (_previewDirty || !_paths.TryGetValue(target, out var path) || path == null || path.Count < 2)
            {
                if (!TryRebuildPathCache(out _))
                {
                    ClearPreviewReservations("NoPath");
                    return result;
                }

                _paths.TryGetValue(target, out path);
            }

            if (path != null && path.Count >= 1)
            {
                result.valid = true;
                TryReservePreviewPath(path, target);
            }
            else
            {
                ClearPreviewReservations("NoPath");
            }

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

        void DumpReadiness()
        {
#if UNITY_EDITOR
            var missing = new List<string>();
            if (!authoring) missing.Add(nameof(authoring));
            else if (authoring.Layout == null) missing.Add("authoring.Layout");
            if (OwnerUnit == null) missing.Add("OwnerUnit");
            if (occupancyService == null) missing.Add(nameof(occupancyService));
            if (_occ == null) missing.Add("_occ");
            if (_selfActor == null) missing.Add("_selfActor"); // <- 用 _selfActor 取代 bridge
            if (missing.Count > 0)
                Debug.LogWarning($"[HexClickMover] Not ready. Missing: {string.Join(", ", missing)}", this);
#endif
        }



        public bool TryPrecheckAim(out string reason, bool raiseHud = true)
        {
            RefreshStateForAim();
            if (occupancyService)
                _occ = occupancyService.Get();

            var unit = OwnerUnit;

            // ✅ 放宽“就绪”条件：不再要求额外测试脚手架
            if (authoring == null || authoring.Layout == null || unit == null || _selfActor == null || _occ == null)
            {
                DumpReadiness();
                if (raiseHud) HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotReady, null);
                reason = "(not-ready)";
                return false;
            }

            if (ctx != null && ctx.Entangled)
            {
                if (raiseHud) HexMoveEvents.RaiseRejected(unit, MoveBlockReason.Entangled, null);
                reason = "(entangled)";
                return false;
            }

            if (_turnManager == null)
            {
                DumpReadiness();
                if (raiseHud) HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotReady, null);
                reason = "(no-turn-manager)";
                return false;
            }

            int needSec = ResolveMoveBudgetSeconds();
            var costSpec = BuildCostSpec();

            var budget = _turnManager.GetBudget(unit);
            if (budget == null || !budget.HasTime(needSec))
            {
                if (raiseHud) HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NoBudget, "No More Time");
                reason = "(no-time)";
                return false;
            }

            int plannedEnergy = Mathf.Max(0, costSpec.energyPerSecond) * Mathf.Max(0, needSec);
            var resources = _turnManager.GetResources(unit);
            if (resources != null && plannedEnergy > 0 && !resources.Has("Energy", plannedEnergy))
            {
                if (raiseHud) HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotEnoughResource, null);
                reason = "(no-energy)";
                return false;
            }

            reason = null;
            return true;
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
            ClearPreviewReservations("ExitAim");
            HideRange();
        }
        public void OnHover(Hex hex) { /* 可选：做 hover 高亮 */ }

        public IEnumerator OnConfirm(Hex hex)
        {
            ClearExecReport();
            RefreshStateForAim();

            if (occupancyService) _occ = occupancyService.Get();

            var unit = OwnerUnit;
            var targetCheck = ValidateMoveTarget(unit, hex);
            if (!targetCheck.ok || targetCheck.plan != PlanKind.MoveOnly)
            {
                ClearPreviewReservations("InvalidTarget");
                RaiseTargetRejected(unit, targetCheck.reason);
                yield break;
            }

            if (_hasPreviewAnchorSnapshot && !_previewAnchorSnapshot.Equals(CurrentAnchor))
            {
                Debug.LogWarning($"[Guard] Move plan stale (previewAnchor={_previewAnchorSnapshot} now={CurrentAnchor}). Rebuild.", this);
                ShowRange();
            }

            _planAnchorSnapshot = CurrentAnchor;
            _hasPlanAnchorSnapshot = true;

            if (_paths.TryGetValue(hex, out var path) && path != null && path.Count >= 2)
            {
                yield return RunPathTween_WithTime(path);
            }
            else
            {
                ClearPreviewReservations("NoPath");
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.PathBlocked, null);
                yield break;
            }
        }

        IGridActor SelfActor => _selfActor;


        void Awake()
        {
            RefreshPainterAndSticky(markPreviewDirty: false);
            // ★ 统一解析：优先 ctx.stats，其次向上找
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            OccControllerReady.Ensure(ctx, this, out _selfActor, placeIfMissing: true);
            if (!occupancyService)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);
            if (!targetValidator)
                targetValidator = GetComponentInParent<DefaultTargetValidator>(true);

            _moveSpec = new TargetingSpec
            {
                occupant = TargetOccupantMask.Empty,
                terrain = TargetTerrainMask.NonObstacle,
                allowSelf = false,
                requireOccupied = false,
                requireEmpty = true,
                maxRangeHexes = -1
            };
            EnsureBound();
        }

        void RefreshPainterAndSticky(bool markPreviewDirty)
        {
            if (tiler != null)
            {
                _painter?.Clear();
                _painter = new HexAreaPainter(tiler);
            }
            else if (_painter != null)
            {
                _painter.Clear();
                _painter = null;
            }

            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);

            if (markPreviewDirty)
            {
                _paths.Clear();
                _previewDirty = true;
                _hasPreviewAnchorSnapshot = false;
                _hasPlanAnchorSnapshot = false;
                _showing = false;
            }
        }

        public void RefreshFactoryInjection()
        {
            RefreshPainterAndSticky(markPreviewDirty: true);
        }

        void Start()
        {
            tiler?.EnsureBuilt();

            if (authoring?.Layout == null)
                return;

            EnsureBound();

            if (occupancyService != null)
                _occ = occupancyService.Get();
            _hasPreviewAnchorSnapshot = false;
            _hasPlanAnchorSnapshot = false;

        }

        protected override void HookEvents(bool bind)
        {

        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureBound();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ClearPreviewReservations("Disable");
            _painter?.Clear();
            _paths.Clear();
            _showing = false;
            _occ = null;
            _previewDirty = true;
            _hasPreviewAnchorSnapshot = false;
            _hasPlanAnchorSnapshot = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        void Update()
        {
            if (authoring == null) return;
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

            if (occupancyService) _occ = occupancyService.Get();

            var unit = OwnerUnit;
            if (authoring?.Layout == null || unit == null || _selfActor == null)
                return false;
            if (occupancyService != null && _occ == null)
                return false;

            var layout = authoring.Layout;
            var startHex = CurrentAnchor;

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
                        null,
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

                    if (passability == null && (_occ == null || !_occ.CanPlaceIgnoringTemp(SelfActor, cell, SelfActor != null ? SelfActor.Facing : unit.Facing, ignore: SelfActor)))
                        return true;
                }

                if (physicsBlocker != null && physicsBlocker(cell)) return true;
                if (env != null && env.IsPit(cell)) return true;

                return false;
            }

            var previewMap = new HexBoardMap<Unit>(layout);
            previewMap.Set(unit, startHex);

            result = HexMovableRange.Compute(layout, previewMap, startHex, steps, Block);

            _paths.Clear();
            foreach (var kv in result.Paths)
                _paths[kv.Key] = kv.Value;

            _previewDirty = false;
            _previewAnchorSnapshot = startHex;
            _hasPreviewAnchorSnapshot = true;

            return true;
        }

        void ClearPreviewReservations(string reason)
        {
            var occ = ctx != null ? ctx.occService : null;
            if (_previewToken.IsValid && occ != null)
                occ.Cancel(ctx, _previewToken, reason);
            _previewToken = default;
            _previewReservedTarget = Hex.Zero;

            if (_previewFallbackActive)
            {
                if (ctx != null)
                    TGD.HexBoard.OccTempOps.ClearFor(ctx);
                _previewFallbackActive = false;
            }
        }

        void ReservePreviewFallback(List<Hex> path)
        {
            if (ctx == null)
                return;

            TGD.HexBoard.OccTempOps.ClearFor(ctx);
            bool any = false;
            if (path != null)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    if (TGD.HexBoard.OccTempOps.Reserve(ctx, path[i]))
                        any = true;
                }
            }

            _previewFallbackActive = any;
        }

        bool TryReservePreviewPath(List<Hex> path, Hex target)
        {
            var occ = ctx != null ? ctx.occService : null;
            if (occ == null)
            {
                ReservePreviewFallback(path);
                return false;
            }

            if (_previewToken.IsValid)
            {
                if (target.Equals(_previewReservedTarget))
                    return true;
                occ.Cancel(ctx, _previewToken, "Update");
                _previewToken = default;
            }

            OccToken token;
            OccReserveResult reserveResult;
            if (occ.ReservePath(ctx, path, out token, out reserveResult, OccReserveMode.SoftPath))
            {
                _previewToken = token;
                _previewReservedTarget = target;
                if (_previewFallbackActive && ctx != null)
                {
                    TGD.HexBoard.OccTempOps.ClearFor(ctx);
                    _previewFallbackActive = false;
                }
                return true;
            }

            if (reserveResult == OccReserveResult.Blocked)
                ReservePreviewFallback(path);

            if (reserveResult == OccReserveResult.AlreadyReserved)
                return true;

            return false;
        }


        // ===== 外部 UI 调用 =====
        public void ShowRange()
        {
            if (!_isAiming || _isExecuting) return;

            var unit = OwnerUnit;
#if UNITY_EDITOR
            var unitPos = unit != null ? unit.Position : Hex.Zero;
            var anchor = CurrentAnchor;
            var occOk = (ctx != null && ctx.occService != null && _selfActor != null);
            var label = TurnManagerV2.FormatUnitLabel(unit);
            LogInternal($"[Probe][MoveAim] unit={label} unitPos={unitPos} anchor={anchor} occReady={occOk} actor={_selfActor?.GetInstanceID()}");
#endif

            if (occupancyService) _occ = occupancyService.Get();

            if (authoring == null || authoring.Layout == null || unit == null || _occ == null || _selfActor == null)
            { ClearPreviewReservations("NotReady"); HexMoveEvents.RaiseRejected(unit, MoveBlockReason.NotReady, null); return; }

            _painter.Clear();

            if (ctx != null && ctx.Entangled)
            {
                _paths.Clear();
                _previewDirty = true;
                ClearPreviewReservations("Entangled");
                HexMoveEvents.RaiseRejected(unit, MoveBlockReason.Entangled, null);
                _showing = true;
                return;
            }

            if (!TryRebuildPathCache(out var result))
            {
                _paths.Clear();
                _previewDirty = true;
                ClearPreviewReservations("NoPath");
                return;
            }

            _painter.Paint(result.Paths.Keys, rangeColor);
            if (showBlockedAsRed) _painter.Paint(result.Blocked, invalidColor);

            HexMoveEvents.RaiseRangeShown(unit, result.Paths.Keys);
            _showing = true;
        }


        public void HideRange()
        {
            ClearPreviewReservations("HideRange");
            _painter.Clear();
            _showing = false;
            _previewDirty = true;
            _hasPreviewAnchorSnapshot = false;
            _hasPlanAnchorSnapshot = false;
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
            unit ??= OwnerUnit;
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
        IEnumerator RunPathTween_WithTime(List<Hex> path)
        {
            Debug.Log($"Ready? L={authoring?.Layout != null} U={OwnerUnit != null} occ={_occ != null} actor={_selfActor != null}");
            if (path == null || path.Count < 2) yield break;

            _isExecuting = true;

            try
            {
                if (_hasPlanAnchorSnapshot && !_planAnchorSnapshot.Equals(CurrentAnchor))
                {
                    Debug.LogWarning($"[Guard] Anchor changed before execute (planAnchor={_planAnchorSnapshot} now={CurrentAnchor}). Abort.", this);
                    ClearPreviewReservations("AnchorChanged");
                    HexMoveEvents.RaiseRejected(OwnerUnit, MoveBlockReason.PathBlocked, "Anchor changed.");
                    yield break;
                }

                if (occupancyService) _occ = occupancyService.Get();
                if (authoring == null || authoring.Layout == null || OwnerUnit == null || _occ == null || _selfActor == null)
                {
                    ClearPreviewReservations("NotReady(no-actor-or-occ)");
                    yield break;
                }

                int requiredSec = ResolveMoveBudgetSeconds();
                var costSpec = BuildCostSpec();

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
                    ClearPreviewReservations("NoPath");
                    HexMoveEvents.RaiseTimeRefunded(OwnerUnit, requiredSec);
                    yield break;
                }

                string refundTag = refunded > 0 ? "Speed_Adjust" : null;
                SetExecReport(requiredSec, refunded, energyNet, false, refundTag);

                var hexSpace = HexSpace.Instance;
                if (hexSpace == null)
                {
                    ClearPreviewReservations("NoSpace");
                    Debug.LogWarning("[HexClickMover] HexSpace instance is missing.", this);
                    yield break;
                }

                var view = ResolveSelfView();
                _moving = true;

                // 起步朝向
                {
                    var fromW = hexSpace.HexToWorld(reached[0], y);
                    var toW = hexSpace.HexToWorld(reached[^1], y);
                    float keep = ResolveMoveKeepDeg();
                    float turn = ResolveMoveTurnDeg();
                    float speed = ResolveMoveTurnSpeed();

                    var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(OwnerUnit.Facing, fromW, toW, keep, turn);
                    if (view != null) yield return HexFacingUtil.RotateToYaw(view, yaw, speed);
                    OwnerUnit.Facing = nf;
                    if (SelfActor != null) SelfActor.Facing = nf;
                }

                HexMoveEvents.RaiseMoveStarted(OwnerUnit, reached);

                var layout = authoring.Layout;
                var unit = OwnerUnit;
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
                    { stoppedByExternal = true; break; }

                    if (passability == null && _occ != null && _occ.IsBlocked(to, SelfActor))
                    { stoppedByExternal = true; break; }

                    if (env != null && env.IsPit(to))
                    {
                        HexMoveEvents.RaiseRejected(unit, MoveBlockReason.PathBlocked, "Pit");
                        stoppedByExternal = true; break;
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
                        var p = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));

                        if (view != null) view.position = p;
                        else if (_selfActor is Component comp && comp) comp.transform.position = p;

                        yield return null;
                    }

                    if (_sticky != null && status != null &&
                        _sticky.TryGetSticky(to, out var stickM, out var stickTurns, out var tag) &&
                        stickTurns > 0 && !Mathf.Approximately(stickM, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, stickM, stickTurns, to.ToString());
                        LogInternal($"[Sticky] Apply U={unitLabel} tag={tag}@{to} mult={stickM:F2} turns={stickTurns}");
                    }

                    unit.Position = to;
                }

                var tokenForCommit = _previewToken;
                if (OwnerUnit != null)
                {
                    var finalAnchor = reached[reached.Count - 1];
                    var finalFacing = (_selfActor != null) ? _selfActor.Facing : OwnerUnit.Facing;

                    var occ = ctx != null ? ctx.occService : null;
                    if (occ != null)
                    {
                        bool committed;
                        OccTxnId txn; OccFailReason reason;

                        if (tokenForCommit.IsValid)
                            committed = occ.Commit(ctx, tokenForCommit, finalAnchor, finalFacing, out txn, out reason);
                        else
                            committed = occ.TryMove(ctx, finalAnchor, finalFacing, out txn, out reason);

                        if (!committed && tokenForCommit.IsValid)
                            HexMoveEvents.RaiseRejected(OwnerUnit, MoveBlockReason.PathBlocked, "CommitFailed");
                    }

                    UnitRuntimeBindingUtil.SyncUnit(OwnerUnit, finalAnchor, finalFacing);
                }



                _previewToken = default;
                _previewReservedTarget = Hex.Zero;
                if (_previewFallbackActive && ctx != null)
                {
                    TGD.HexBoard.OccTempOps.ClearFor(ctx);
                    _previewFallbackActive = false;
                }

                _moving = false;
                HexMoveEvents.RaiseMoveFinished(OwnerUnit, CurrentAnchor);

                if (truncatedByBudget && !stoppedByExternal)
                {
                    HexMoveEvents.RaiseNoMoreTime(OwnerUnit);
                    LogInternal("[Move] No more time.");
                }

                if (refunded > 0)
                {
                    HexMoveEvents.RaiseTimeRefunded(OwnerUnit, refunded);
                }
            }
            finally
            {
                _isExecuting = false;
                _hasPlanAnchorSnapshot = false;
            }
        }

        int IActionExecReportV2.UsedSeconds => ReportUsedSeconds;
        int IActionExecReportV2.RefundedSeconds => ReportRefundedSeconds;

        void IActionExecReportV2.Consume()
        {
            ClearExecReport();
        }
        // 替换整个 EnsureBound 方法体
        bool EnsureBound()
        {
            // 统一通过 IOcc 解析/放置（必要时首放）
            OccControllerReady.Ensure(ctx, this, out _selfActor, placeIfMissing: true);

            if (occupancyService == null)
                occupancyService = GetComponentInParent<HexOccupancyService>(true);

            if (_occ == null && occupancyService != null)
                _occ = occupancyService.Get();

            var unit = OwnerUnit;
            return authoring?.Layout != null
                && unit != null
                && _occ != null
                && _selfActor != null;
        }


    }
}
