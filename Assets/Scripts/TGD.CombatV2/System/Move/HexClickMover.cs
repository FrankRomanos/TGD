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
    public sealed class HexClickMover : MonoBehaviour, IActionToolV2
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
        // —— 每次进入/确认前，刷新一次“起点状态”（以后也可挂接技能/buff 刷新）——
        void RefreshStateForAim()
        {
            if (debugLog)
            {
                var start = (driver && driver.UnitRef != null) ? driver.UnitRef.Position : Hex.Zero;
                float m = (env != null) ? Mathf.Clamp(env.GetSpeedMult(start), 0.1f, 5f) : 1f;
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
        float GetBaseMoveRate()
        {
            // 只返回“基础+（与基础相关的）加成”的有效 MR，不写入 ctx
            if (ctx != null)
            {
                int b = Mathf.Max(1, ctx.BaseMoveRate);
                // 把“与基础相关的百分比加成”转成 multiplier；注意这里不含环境，环境在调用处按“起点/每步 from”处理
                float buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
                int mr = StatsMathV2.EffectiveMoveRateFromBase(b, new[] { buffMult }, ctx.MoveRateFlatAdd);
                return Mathf.Max(0.01f, mr);
            }
            int timeSec = Mathf.Max(1, Mathf.CeilToInt(config ? config.timeCostSeconds : 1f));
            int steps = Mathf.Max(1, (config != null ? Mathf.Clamp(config.fallbackSteps, 1, 9999) : 3));
            return (float)steps / timeSec; // 无 Stats 时用 fallback 估算
        }

        void Awake()
        {
            _cost = costProvider as IMoveCostService;
            _painter = new HexAreaPainter(tiler);
            // ★ 统一解析：优先 ctx.stats，其次向上找
            if (!ctx) ctx = GetComponentInParent<UnitRuntimeContext>(true);

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

        // ===== 外部 UI 调用 =====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady || _occ == null || _actor == null)
            { HexMoveEvents.RaiseRejected(driver ? driver.UnitRef : null, MoveBlockReason.NotReady, null); return; }

            _painter.Clear();
            _paths.Clear();

            var layout = authoring.Layout;
            var start = (driver && driver.UnitRef != null) ? driver.UnitRef.Position : Hex.Zero;

            // 缠绕/无配置兜底
            if (ctx != null && ctx.Entangled)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.Entangled, null);
                _showing = true;
                return;
            }

            int steps;
            if (ctx != null && config != null)
            {
                int timeSec = Mathf.Max(1, Mathf.CeilToInt(config.timeCostSeconds));
                int baseR = Mathf.Max(1, ctx.BaseMoveRate);
                float buffMult = 1f + Mathf.Max(-0.99f, ctx.MoveRatePctAdd);
                int flat = ctx.MoveRateFlatAdd;
                float startMult = (env != null) ? Mathf.Clamp(env.GetSpeedMult(start), 0.1f, 5f) : 1f;

                // 预览只看“当下”：基础 +（基于基础的 buff 增减）+（起点环境增减）
                int mr = StatsMathV2.EffectiveMoveRateFromBase(baseR, new[] { buffMult, startMult }, flat);
                steps = Mathf.Min(config.stepsCap, StatsMathV2.StepsAllowed(mr, timeSec));

                if (debugLog) Debug.Log($"[ClickMove] ShowRange baseR={baseR} buffMult={buffMult:F2} startMult={startMult:F2} flat={flat} → mr={mr} steps={steps}", this);
            }
            else
            {
                steps = (config != null) ? Mathf.Clamp(config.fallbackSteps, 0, config.stepsCap) : 3;
            }


            // ✅ 预览阶段：只有当 blockByPhysics 且 obstacleMask != 0 才启用物理阻挡
            var physicsBlocker =
                (blockByPhysics && obstacleMask != 0)
                ? HexAreaUtil.MakeDefaultBlocker(
                      authoring, driver?.Map, start,
                      blockByUnits: false,                  // 占位交给 _occ
                      blockByPhysics: true,
                      obstacleMask: obstacleMask,
                      physicsRadiusScale: physicsRadiusScale,
                      physicsProbeHeight: physicsProbeHeight,
                      includeTriggerColliders: false,      // 预览忽略触发器，避免特效误判
                      y: y)
                : null;

            bool Block(TGD.HexBoard.Hex cell)
            {
                if (layout != null && !layout.Contains(cell)) return true;

                // ✅ 用“能否整组落位”，并忽略自己
                if (blockByUnits && !_occ.CanPlace(_actor, cell, _actor.Facing, ignore: _actor))
                    return true;

                if (physicsBlocker != null && physicsBlocker(cell)) return true;

                // ✅ 坑洞直接当硬障碍，移动/预览都不显示
                if (env != null && env.IsPit(cell)) return true;

                return false;
            }

            var result = HexMovableRange.Compute(layout, driver.Map, start, steps, Block);
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

            // 再兜底：时间
            if (simulateTurnTime && _turnSecondsLeft < requiredSec)
            {
                HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NoBudget, "No More Time");
                LogTime("BeforePay/NO-TIME");
                yield break;
            }

            // 成本（先扣）
            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config))
                { HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.OnCooldown, null); yield break; }
                if (!_cost.HasEnough(driver.UnitRef, config))
                { HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NotEnoughResource, null); yield break; }

                _cost.Pay(driver.UnitRef, config);
                Debug.Log($"[ClickMove] Pay: {config.energyCost} energy for {requiredSec}s move.", this);
            }

            // —— 执行期：MoveSimulator 做环境结算 + 返还 —— //
            float baseMR = GetBaseMoveRate();
            float EnvMult(Hex h) => env != null ? env.GetSpeedMult(h) : 1f; // 模拟器内部会 clamp & 用 from

            var sim = MoveSimulator.Run(
                path,
                baseMR,
                requiredSec,
                EnvMult,
                Mathf.Max(0.01f, (config ? config.refundThresholdSeconds : 0.8f)),
                debugLog
            );
            var reached = sim.ReachedPath;


            // 返还换算：本次“净花费秒数”
            int refunded = Mathf.Max(0, sim.RefundedSeconds);
            int spentSec = Mathf.Max(0, requiredSec - refunded);

            if (reached == null || reached.Count < 2)
            {
                // 预算不足以前进一步：本次不动 -> 把刚才扣的能量和时间全还回去
                _cost?.RefundSeconds(driver.UnitRef, config, requiredSec);   // ← 用接口，不再写具体适配器类型
                if (simulateTurnTime) _turnSecondsLeft = Mathf.Max(0, _turnSecondsLeft + requiredSec);
                // 事件：整额返还
                HexMoveEvents.RaiseTimeRefunded(driver.UnitRef, requiredSec);
                Debug.Log($"[ClickMove] No step possible. Refund ALL: {requiredSec}s. TimeLeft={_turnSecondsLeft}s", this);
                yield break;
            }

            _moving = true;
            // 起步一次性转向（与旧逻辑一致）
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
            // sim / reached 已经算出来了
            bool truncatedByBudget = (reached.Count < path.Count); // ★ 少于原计划，表示预算截断
            bool stoppedByExternal = false;                         // ★ 外因打断标记（占位/坑/缠绕）

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

                // ★ 与 MoveSimulator 保持一致：按“起点格 from”的环境倍率
                float fromMult = Mathf.Clamp(EnvMult(from), 0.1f, 5f);
                float effMR = Mathf.Max(0.01f, baseMR) * fromMult;       // 本步有效移速（格/秒
                float stepDuration = Mathf.Max(minStepSeconds, 1f / Mathf.Max(0.01f, effMR));

                // 告诉动画当前步速（可选）
                HexMoveEvents.RaiseStepSpeed(unit, effMR, baseMR);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / stepDuration;                        // 真正决定速度的地方
                    if (view != null) view.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }


                _occ.TryMove(_actor, to);

                // 地形“学习一次”：只对加速写回基础，减速不写回，避免被困
                if (terrainLearnOnce && env != null && ctx != null)
                {
                    float multTo = Mathf.Clamp(env.GetSpeedMult(to), 0.1f, 5f);
                    if (multTo > 1.001f && !_learnedHaste)
                    {
                        int baseR = Mathf.Max(1, ctx.BaseMoveRate);
                        int add = TGD.CoreV2.StatsMathV2.EnvAddFromMultiplier(baseR, multTo);
                        if (add > 0)
                        {
                            ctx.BaseMoveRate = Mathf.Max(1, baseR + add);
                            _learnedHaste = true;
                            Debug.Log($"[ClickMove] Terrain haste learned once: Base {baseR} -> {ctx.BaseMoveRate} (mult={multTo})");
                        }
                    }
                    // 减速(multTo < 1)：仅执行期生效，不永久写回，避免“极慢地形把人困住”的情况
                }

                if (driver.Map != null)
                { if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to); }
                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;
            HexMoveEvents.RaiseMoveFinished(driver.UnitRef, driver.UnitRef.Position);
            // ★ 仅当“预算截断”且“不是外因打断”时，广播没时间
            if (truncatedByBudget && !stoppedByExternal)
            {
                HexMoveEvents.RaiseNoMoreTime(driver.UnitRef);
                Debug.Log("[ClickMove] No more time.");
            }

            // —— 扣除净时间 & 能量返还 —— //
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
            // 交互口径：若当前仍处于 Aim（由外部 Manager 控制），可选择性刷新
            if (_showing) ShowRange(); // 刷新可达
      
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