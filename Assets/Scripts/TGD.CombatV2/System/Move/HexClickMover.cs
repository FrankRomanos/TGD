// File: TGD.HexBoard/HexClickMover.cs
using System.Collections;
using System.Collections.Generic;
using TGD.HexBoard;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.CombatV2
{
    [CreateAssetMenu(menuName = "TGD/Combat/Move Action Config")]
    public class MoveActionConfig : ScriptableObject
    {
        [Header("Costs")] public int energyCost = 10;
        public float timeCostSeconds = 1f;
        public float cooldownSeconds = 0f;
        public string actionId = "Move";

        [Header("Distance")] public int fallbackSteps = 3;
        public int stepsCap = 12;

        [Header("Facing")] public float keepDeg = 45f;
        public float turnDeg = 135f;
        public float turnSpeedDegPerSec = 720f;

        [Header("Refund")]
        [Tooltip("节省时间累计达到该阈值即返还 1 秒（及对应能量）")]
        [Min(0.01f)] public float refundThresholdSeconds = 0.8f;
    }

    /// <summary>
    /// 点击移动（占位版）：BFS 可达 + 一次性转向 + 逐格 Tween + HexOccupancy 碰撞
    /// </summary>
    public sealed class HexClickMover : MonoBehaviour, IActionToolV2, IActionExecReportV2
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;     // 提供 UnitRef/SyncView
        public HexBoardTiler tiler;      // 着色
        public FootprintShape footprintForActor; // ★ 这个单位的占位形状（SO）
        [Header("Context (optional)")]           // ★ 新增
        public UnitRuntimeContext ctx;            // ★ 新增

        [Header("Debug")]                         // ★ 新增（若你已有 debugLog 就跳过）
        public bool debugLog = true;

        // === 新增：临时回合时间（无 TurnManager 时自管理） ===
        [Header("Turn Time (TEMP no-TM)")]
        public bool simulateTurnTime = true;
        [Tooltip("基础回合时间（秒）")]
        public int baseTurnSeconds = 6;
        [SerializeField, Tooltip("当前剩余回合秒数（运行时）")]
        int _turnSecondsLeft = -1;
        int MaxTurnSeconds => Mathf.Max(0, baseTurnSeconds + (ctx ? ctx.Speed : 0));
        void EnsureTurnTimeInited()
        {
            if (!simulateTurnTime) return;
            if (_turnSecondsLeft < 0)
            {
                _turnSecondsLeft = MaxTurnSeconds;
                Debug.Log($"[ClickMove] Init TurnTime = {_turnSecondsLeft}s (base={baseTurnSeconds} + speed={(ctx ? ctx.Speed : 0)})", this);
            }
        }

        void LogTime(string tag) => Debug.Log($"[ClickMove] [{tag}] TimeLeft = {_turnSecondsLeft}s", this);

        [Header("Action Config & Cost")]
        public MoveActionConfig config;
        public MonoBehaviour costProvider;
        IMoveCostService _cost;

        [Tooltip("精准移动：进入减速地形时永久降低 MoveRate（无时间系统的临时方案）")]
        public bool applyPermanentSlowInPreciseMove = true;

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
        [Tooltip("地形：进入后永久学习一次（加速/减速各一次；Buff/Debuff不在此处学习）")]
        public bool terrainLearnOnce = true;

        bool _learnedHaste = false;  // 地形正向学习已发生？
        bool _learnedSlow = false;  // 地形负向学习已发生？
        [Header("Sticky Slow (optional)")]
        public MoveRateStatusRuntime status;        // 黏性修饰器运行时（可选）
        public MonoBehaviour stickySource;          // 任意实现了 IStickySlowSource 的组件
        const float MR_MIN = 1f;
        const float MR_MAX = 12f;
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;
        const float MULT_MIN = 0.01f;
        const float MULT_MAX = 100f;
        IStickyMoveSource _sticky;

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

        //occ
        public HexOccupancyService occService;

        // runtime
        bool _showing = false, _moving = false;
        readonly Dictionary<Hex, List<Hex>> _paths = new();
        HexAreaPainter _painter;

        // 占位
        HexOccupancy _occ;
        IGridActor _actor;   // 适配当前 UnitRef
                             // === HUD 提示（可选）===
        [Header("HUD")]
        public bool showHudMessage = true;
        public float hudSeconds = 1.6f;

        string _hudMsg;
        float _hudMsgUntil;
        public string Id => "Move";
        int _reportUsedSeconds;
        int _reportRefundedSeconds;
        bool _reportPending;

        void ClearExecReport()
        {
            _reportUsedSeconds = 0;
            _reportRefundedSeconds = 0;
            _reportPending = false;
        }

        void SetExecReport(int used, int refunded)
        {
            _reportUsedSeconds = Mathf.Max(0, used);
            _reportRefundedSeconds = Mathf.Max(0, refunded);
            _reportPending = true;
        }
        // —— 每次进入/确认前，刷新一次“起点状态”（以后也可挂接技能/buff 刷新）——
        void RefreshStateForAim()
        {
            if (debugLog)
            {
                var start = (driver && driver.UnitRef != null) ? driver.UnitRef.Position : Hex.Zero;
                float m = (env != null) ? Mathf.Clamp(env.GetSpeedMult(start), ENV_MIN, ENV_MAX) : 1f;
                Debug.Log($"[ClickMove] RefreshStateForAim start={start} envMult={m:F2} baseR={(ctx ? ctx.BaseMoveRate : -1)}", this);
            }
        }

        public void OnEnterAim()
        {
            EnsureTurnTimeInited();
            RefreshStateForAim();
            // 预检查：时间 + 能量，不满足就直接拒绝，不进入瞄准
            int needSec = Mathf.Max(1, Mathf.CeilToInt(config ? config.timeCostSeconds : 1f));
            if (simulateTurnTime && _turnSecondsLeft < needSec)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NoBudget, "No More Time");
                LogTime("EnterAim/NO-TIME");
                return;
            }
            if (_cost != null && config != null && !_cost.HasEnough(driver.UnitRef, config))
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NotEnoughResource, null);
                return;
            }
            ShowRange();
        }
        public void OnExitAim() { HideRange(); }
        public void OnHover(Hex hex) { /* 可选：做 hover 高亮 */ }

        public IEnumerator OnConfirm(Hex hex)
        {
            ClearExecReport();
            EnsureTurnTimeInited();
            RefreshStateForAim();
            int needSec = Mathf.Max(1, Mathf.CeilToInt(config ? config.timeCostSeconds : 1f));

            // 再做一次兜底预检查（避免竞态）
            if (simulateTurnTime && _turnSecondsLeft < needSec)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NoBudget, "No More Time");
                LogTime("Confirm/NO-TIME");
                yield break;
            }
            if (_cost != null && config != null && !_cost.HasEnough(driver.UnitRef, config))
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NotEnoughResource, null);
                yield break;
            }

            if (_paths.TryGetValue(hex, out var path) && path != null && path.Count >= 2)
            {
                yield return RunPathTween_WithTime(path);   // ★ 改：走带结算版本
            }
            else
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.PathBlocked, null);
                yield break;
            }
        }

        void Awake()
        {           
            _cost = costProvider as IMoveCostService;
            _painter = new HexAreaPainter(tiler);
            // ★ 统一解析：优先 ctx.stats，其次向上找
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);
            _sticky = (stickySource as IStickyMoveSource) ?? (env as IStickyMoveSource);
        }

        void Start()
        {
            tiler?.EnsureBuilt();

            driver?.EnsureInit();
            if (authoring?.Layout == null || driver == null || !driver.IsReady) return;

            // 先用共享；没有再自己 new
            _occ = (occService != null ? occService.Get() : null)
                   ?? new HexOccupancy(authoring.Layout);

            var fp = footprintForActor != null ? footprintForActor : CreateSingleFallback();
            _actor = new UnitGridAdapter(driver.UnitRef, fp);

            // 注册本单位的占位（失败就再试一次，通常是起始位置非法）
            if (!_occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing))
                _occ.TryPlace(_actor, driver.UnitRef.Position, driver.UnitRef.Facing);
            Debug.Log($"[ClickMover] unitView={(driver && driver.unitView ? driver.unitView.name : "NULL")}", this);
        }

        void OnDisable()
        {
            _painter?.Clear();
            _paths.Clear();
            _showing = false;
            _actor = null; _occ = null;
        }

        void Update()
        {
            if (!_showing || _moving) return;
            if (authoring == null || driver == null || !driver.IsReady) return;//短路返回
        }

        MoveRateSnapshot BuildMoveRates(Hex start)
        {
            int baseRate = ctx != null ? ctx.BaseMoveRate : GetFallbackBaseRate();
            baseRate = Mathf.Clamp(baseRate, (int)MR_MIN, (int)MR_MAX);

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
            float baseNoEnv = StatsMathV2.MR_MultiThenFlat(baseRate, new[] { combined }, flatAfter);
            baseNoEnv = Mathf.Clamp(baseNoEnv, MR_MIN, MR_MAX);

            var startSample = SampleStepModifier(start);
            float startEnv = Mathf.Clamp(startSample.Multiplier <= 0f ? 1f : startSample.Multiplier, ENV_MIN, ENV_MAX);
            float startUse = startSample.Sticky ? 1f : startEnv;
            startUse = Mathf.Clamp(startUse, ENV_MIN, ENV_MAX);
            float mrClick = Mathf.Clamp(baseNoEnv * startUse, MR_MIN, MR_MAX);

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
                if (stickTurns > 0 && !Mathf.Approximately(stickM, 1f))
                {
                    hasStickySource = true;
                    bool alreadyActive = status != null && status.HasActiveTag(tag);
                    if (!alreadyActive)
                    {
                        mult *= stickM;
                        sticky = true;
                    }
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

            return new MoveSimulator.StickySample(mult, sticky);
        }

        int GetFallbackBaseRate()
        {
            if (ctx != null) return ctx.BaseMoveRate;

            int steps = config != null ? Mathf.Max(1, config.fallbackSteps) : 3;
            float seconds = config != null ? Mathf.Max(0.1f, config.timeCostSeconds) : 1f;
            float mr = steps / Mathf.Max(0.1f, seconds);
            return Mathf.Clamp(Mathf.RoundToInt(mr), (int)MR_MIN, (int)MR_MAX);
        }

        // ===== 外部 UI 调用 =====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady || _occ == null || _actor == null)
            { HexMoveEvents.RaiseRejected(driver ? driver.UnitRef : null, MoveBlockReason.NotReady, null); return; }

            _painter.Clear();
            _paths.Clear();

            var layout = authoring.Layout;
            var startHex = (driver && driver.UnitRef != null) ? driver.UnitRef.Position : Hex.Zero;

            if (ctx != null && ctx.Entangled)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.Entangled, null);
                _showing = true;
                return;
            }

            var rates = BuildMoveRates(startHex);

            // ====== 修复：起点为“会贴附”的加速格时，预览不要把起点地形再乘一次 ======
            const float MR_MIN = 1f, MR_MAX = 12f;
            const float ENV_MIN = 0.1f, ENV_MAX = 5f;

            bool startGivesSticky = rates.startIsSticky;

            float mrNoEnv = Mathf.Clamp(rates.baseNoEnv, MR_MIN, MR_MAX);

            float startMultUse = startGivesSticky ? 1f : Mathf.Clamp(rates.startEnvMult, ENV_MIN, ENV_MAX);

            float mrPreview = Mathf.Clamp(mrNoEnv * startMultUse, MR_MIN, MR_MAX);

            // 计算步数
            int timeSec = Mathf.Max(1, Mathf.CeilToInt(config ? config.timeCostSeconds : 1f));
            int cap = config ? config.stepsCap : 12;
            int steps = Mathf.Min(cap, StatsMathV2.StepsAllowedF32(mrPreview, timeSec));

            if (debugLog)
            {
                Debug.Log(
                    $"[ClickMove/Preview] baseR={rates.baseRate} buff={rates.buffMult:F2} " +
                    $"stickyNow={rates.stickyMult:F2} flatAfter={rates.flatAfter} " +
                    $"startRaw={rates.startEnvMult:F2} startIsSticky={startGivesSticky} " +
                    $"=> MR_noEnv={mrNoEnv:F2} MR_preview={mrPreview:F2} steps={steps}",
                    this
                );
            }


            var physicsBlocker =
                (blockByPhysics && obstacleMask != 0)
                ? HexAreaUtil.MakeDefaultBlocker(
                      authoring, driver?.Map, startHex,
                      blockByUnits: false,
                      blockByPhysics: true,
                      obstacleMask: obstacleMask,
                      physicsRadiusScale: physicsRadiusScale,
                      physicsProbeHeight: physicsProbeHeight,
                      includeTriggerColliders: false,
                      y: y)
                : null;

            bool Block(TGD.HexBoard.Hex cell)
            {
                if (layout != null && !layout.Contains(cell)) return true;

                if (blockByUnits && !_occ.CanPlace(_actor, cell, _actor.Facing, ignore: _actor))
                    return true;

                if (physicsBlocker != null && physicsBlocker(cell)) return true;

                if (env != null && env.IsPit(cell)) return true;

                return false;
            }

            var result = HexMovableRange.Compute(layout, driver.Map, startHex, steps, Block);
            foreach (var kv in result.Paths) _paths[kv.Key] = kv.Value;

            _painter.Paint(result.Paths.Keys, rangeColor);
            if (showBlockedAsRed) _painter.Paint(result.Blocked, invalidColor);

            HexMoveEvents.RaiseRangeShown(driver.UnitRef, result.Paths.Keys);
            _showing = true;
        }

        public void HideRange()
        {
            _painter.Clear();
            _paths.Clear();
            _showing = false;
            HexMoveEvents.RaiseRangeHidden();
        }

        // ===== 逐格移动 + 起步一次性转向 + 占位提交 =====
        IEnumerator RunPathTween_WithTime(List<Hex> path)
        {

            if (path == null || path.Count < 2) yield break;
            if (authoring == null || driver == null || !driver.IsReady || _occ == null || _actor == null) yield break;

            int requiredSec = Mathf.Max(1, Mathf.CeilToInt(config ? config.timeCostSeconds : 1f));

            if (simulateTurnTime && _turnSecondsLeft < requiredSec)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NoBudget, "No More Time");
                LogTime("BeforePay/NO-TIME");
                yield break;
            }

            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config))
                { HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.OnCooldown, null); yield break; }
                if (!_cost.HasEnough(driver.UnitRef, config))
                { HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NotEnoughResource, null); yield break; }

                _cost.Pay(driver.UnitRef, config);
                Debug.Log($"[ClickMove] Pay: {config.energyCost} energy for {requiredSec}s move.", this);
            }

            var rates = BuildMoveRates(path[0]);

            float refundThreshold = Mathf.Max(0.01f, (config ? config.refundThresholdSeconds : 0.8f));

            var sim = MoveSimulator.Run(
                path,
                rates.baseNoEnv,
                rates.mrClick,
                requiredSec,
                SampleStepModifier,
                refundThreshold,
                debugLog
            );
            var reached = sim.ReachedPath;

            int refunded = Mathf.Max(0, sim.RefundedSeconds);
            int spentSec = Mathf.Max(0, requiredSec - refunded);
            int usedSeconds = Mathf.Max(0, Mathf.CeilToInt(sim.UsedSeconds));
            var stepRates = sim.StepEffectiveRates;

            if (reached == null || reached.Count < 2)
            {
                _cost?.RefundSeconds(driver.UnitRef, config, requiredSec);
                if (simulateTurnTime) _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft + requiredSec);
                HexMoveEvents.RaiseTimeRefunded(driver.UnitRef, requiredSec);
                Debug.Log($"[ClickMove] No step possible. Refund ALL: {requiredSec}s. TimeLeft={_turnSecondsLeft}s", this);
                yield break;
            }

            SetExecReport(usedSeconds, refunded);
            _moving = true;
            if (driver.unitView != null)
            {
                var fromW = authoring.Layout.World(reached[0], y);
                var toW = authoring.Layout.World(reached[^1], y);
                float keep = config ? config.keepDeg : 45f;
                float turn = config ? config.turnDeg : 135f;
                float speed = config ? config.turnSpeedDegPerSec : 720f;

                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                driver.UnitRef.Facing = nf;
                _actor.Facing = nf;
            }

            HexMoveEvents.RaiseMoveStarted(driver.UnitRef, reached);

            var layout = authoring.Layout;
            var unit = driver.UnitRef;
            Transform view = (driver.unitView != null) ? driver.unitView : this.transform;
            bool truncatedByBudget = (reached.Count < path.Count);
            bool stoppedByExternal = false;

            for (int i = 1; i < reached.Count; i++)
            {
                if (ctx != null && ctx.Entangled)
                {
                    HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.Entangled, "Break Move!");
                    stoppedByExternal = true;
                }

                var from = reached[i - 1];
                var to = reached[i];

                if (_occ.IsBlocked(to, _actor))
                {
                    stoppedByExternal = true;
                    break;
                }
                if (env != null && env.IsPit(to))
                {
                    HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.PathBlocked, "Pit");
                    stoppedByExternal = true;
                    break;
                }

                HexMoveEvents.RaiseMoveStep(unit, from, to, i, reached.Count - 1);


                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float effMR = (stepRates != null && (i - 1) < stepRates.Count)
                    ? stepRates[i - 1]
                    : Mathf.Clamp(rates.baseNoEnv, MR_MIN, MR_MAX);
                float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(0.01f, effMR));


                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / stepDuration;
                    if (view != null) view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }


                _occ.TryMove(_actor, to);
                if (_sticky != null && status != null && _sticky.TryGetSticky(to, out var stickM, out var stickTurns, out var tag))
                {
                    if (stickTurns > 0 && !Mathf.Approximately(stickM, 1f))
                    {
                        status.ApplyOrRefreshExclusive(tag, stickM, stickTurns);
                        if (debugLog) Debug.Log($"[Sticky] tag={tag} mult={stickM:F2} turns={stickTurns} (applied/refreshed) at={to}", this);
                    }
                }

                if (driver.Map != null)
                { if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to); }
                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;
            HexMoveEvents.RaiseMoveFinished(driver.UnitRef, driver.UnitRef.Position);
            if (truncatedByBudget && !stoppedByExternal)
            {
                HexMoveEvents.RaiseNoMoreTime(driver.UnitRef);
                Debug.Log("[ClickMove] No more time.");
            }

            if (simulateTurnTime)
            {
                _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft - spentSec);
            }
            if (refunded > 0)
            {
                _cost?.RefundSeconds(driver.UnitRef, config, refunded);
                HexMoveEvents.RaiseTimeRefunded(driver.UnitRef, refunded);
                Debug.Log($"[ClickMove] Refund: +{refunded}s (and energy). New TimeLeft={_turnSecondsLeft}s", this);
            }
            else
            {
                Debug.Log($"[ClickMove] Spent {spentSec}s, Refunded {refunded}s. TimeLeft={_turnSecondsLeft}s", this);
            }
            if (_showing) ShowRange();
            if (status != null && spentSec > 0)
            {
                status.ConsumeSeconds(spentSec);
            }

        }
        int IActionExecReportV2.UsedSeconds => _reportPending ? _reportUsedSeconds : 0;
        int IActionExecReportV2.RefundedSeconds => _reportPending ? _reportRefundedSeconds : 0;

        void IActionExecReportV2.Consume()
        {
            ClearExecReport();
        }

        // 若没指定占位，临时造一个“单格”占位
        static FootprintShape CreateSingleFallback()
        {
            var s = ScriptableObject.CreateInstance<FootprintShape>();
            s.name = "Footprint_Single_Runtime";
            s.offsets = new() { new L2(0, 0) };
            return s;
        }


    }
}