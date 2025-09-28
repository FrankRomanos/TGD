// File: TGD.HexBoard/HexClickMover.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public interface IMoveCostService
    {
        bool IsOnCooldown(Unit unit, MoveActionConfig cfg);
        bool HasEnough(Unit unit, MoveActionConfig cfg);
        void Pay(Unit unit, MoveActionConfig cfg);
    }

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
    }

    /// <summary>
    /// 点击移动（占位版）：BFS 可达 + 一次性转向 + 逐格 Tween + HexOccupancy 碰撞
    /// </summary>
    public sealed class HexClickMover : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;     // 提供 UnitRef/SyncView
        public HexBoardTiler tiler;      // 着色
        public FootprintShape footprintForActor; // ★ 这个单位的占位形状（SO）

        [Header("Action Config & Cost")]
        public MoveActionConfig config;
        public MonoBehaviour costProvider;
        IMoveCostService _cost;

        [Header("Picking")]
        public Camera pickCamera;
        public LayerMask pickMask = ~0;
        public float rayMaxDistance = 2000f;
        public float pickPlaneY = 0.01f;

        [Header("Motion")]
        public float stepSeconds = 0.12f;
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

        [Header("Stats V2 (optional)")]
        public CoreV2.StatsV2 statsV2;

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
        static int ComputeSteps(MoveActionConfig cfg, CoreV2.StatsV2 stats)
        {
            // 缠绕：禁止主动移动
            if (stats != null && stats.IsEntangled) return 0;

            if (stats == null)
            {
                // 兜底：用 Fallback
                int s = (cfg != null) ? Mathf.Max(0, cfg.fallbackSteps) : 3;
                if (cfg != null) s = Mathf.Min(s, Mathf.Max(0, cfg.stepsCap));
                return s;
            }

            // 接了 MoveRate：floor( MoveRate × TimeCostSeconds )
            float seconds = (cfg != null) ? Mathf.Max(0f, cfg.timeCostSeconds) : 1f;
            int steps = Mathf.FloorToInt(stats.MoveRate * seconds);  // “只舍不入”
            if (cfg != null) steps = Mathf.Clamp(steps, 0, Mathf.Max(0, cfg.stepsCap));
            return Mathf.Max(0, steps);
        }





        void Awake()
        {
            _cost = costProvider as IMoveCostService;
            _painter = new HexAreaPainter(tiler);
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
            if (authoring == null || driver == null || !driver.IsReady) return;

            if (Input.GetMouseButtonDown(0))
            {
                var h = PickHexUnderMouse();
                if (!h.HasValue) return;
                if (_paths.TryGetValue(h.Value, out var path) && path != null && path.Count >= 2)
                    StartCoroutine(RunPathTween(path));
                else
                    TGD.HexBoard.HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.PathBlocked, null);
            }
        }

        // ===== 外部 UI 调用 =====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady || _occ == null || _actor == null)
            { TGD.HexBoard.HexMoveEvents.RaiseRejected(driver ? driver.UnitRef : null, MoveBlockReason.NotReady, null); return; }

            _painter.Clear();
            _paths.Clear();

            int steps = ComputeSteps(config, statsV2);
            if (steps <= 0)
            {
                // 缠绕或本次步数为 0
                var reason = (statsV2 != null && statsV2.IsEntangled) ? MoveBlockReason.Entangled : MoveBlockReason.NoSteps;
                TGD.HexBoard.HexMoveEvents.RaiseRejected(driver.UnitRef, reason, null);
                _showing = true;
                return;
            }
            if (statsV2 != null && config != null)
            {
                int timeSec = Mathf.Max(1, Mathf.CeilToInt(config.timeCostSeconds)); // 时间按整数秒
                steps = Mathf.Min(config.stepsCap,
                    CoreV2.StatsMathV2.StepsAllowed(statsV2.EffectiveMoveRate, timeSec));
            }
            else
            {
                steps = (config != null) ? Mathf.Clamp(config.fallbackSteps, 0, config.stepsCap) : 3;
            }
            var layout = authoring.Layout;
            var start = _actor.Anchor;

            // 统一阻挡（含越界 + 物理 + 占位(忽略自己)）
            var physicsBlocker = HexAreaUtil.MakeDefaultBlocker(
                authoring, driver?.Map, start,
                blockByUnits: false, // 占位交给 _occ
                blockByPhysics,
                obstacleMask, physicsRadiusScale, physicsProbeHeight, includeTriggerColliders, y
            );
            bool Block(Hex cell)
            {
                if (layout != null && !layout.Contains(cell)) return true;
                if (blockByUnits && _occ.IsBlocked(cell, _actor)) return true;
                if (physicsBlocker != null && physicsBlocker(cell)) return true;
                return false;
            }

            var result = HexMovableRange.Compute(layout, driver.Map, start, steps, Block);
            foreach (var kv in result.Paths) _paths[kv.Key] = kv.Value;

            _painter.Paint(result.Paths.Keys, rangeColor);
            if (showBlockedAsRed) _painter.Paint(result.Blocked, invalidColor);

            // 让 UI 能显示范围（如果你想做范围提示）
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
        IEnumerator RunPathTween(List<Hex> path)
        {
            if (path == null || path.Count < 2) yield break;
            if (authoring == null || driver == null || !driver.IsReady || _occ == null || _actor == null) yield break;

            // 成本
            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config))
                { TGD.HexBoard.HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.OnCooldown, null); yield break; }
                if (!_cost.HasEnough(driver.UnitRef, config))
                { TGD.HexBoard.HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.NotEnoughResource, null); yield break; }
                _cost.Pay(driver.UnitRef, config);

            }

            _moving = true;
            HexMoveEvents.RaiseMoveStarted(driver.UnitRef, path);

            // 起步一次性转向（45°/135°）
            if (driver.unitView != null)
            {
                var fromW = authoring.Layout.World(path[0], y);
                var toW = authoring.Layout.World(path[^1], y);
                float keep = config ? config.keepDeg : 45f;
                float turn = config ? config.turnDeg : 135f;
                float speed = config ? config.turnSpeedDegPerSec : 720f;

                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                driver.UnitRef.Facing = nf;
                _actor.Facing = nf;
            }

            var layout = authoring.Layout;
            var unit = driver.UnitRef;

            for (int i = 1; i < path.Count; i++)
            {
                // RunPathTween(...) 循环内
                if (statsV2 != null && statsV2.IsEntangled)
                {
                    HexMoveEvents.RaiseRejected(driver.UnitRef, MoveBlockReason.Entangled, "Break Move!");
                    break;
                }
                var from = path[i - 1];
                var to = path[i];
                HexMoveEvents.RaiseMoveStep(unit, from, to, i, path.Count - 1);
                // 占位能否移动（整组）
                if (!_occ.CanPlace(_actor, to, _actor.Facing, ignore: _actor))
                    break;

                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.3f, stepSeconds);
                    if (driver.unitView != null)
                        driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                // 占位提交
                _occ.TryMove(_actor, to);

                // （可选）保持旧 Map 同步，便于其它旧逻辑过渡
                if (driver.Map != null)
                {
                    if (!driver.Map.Move(unit, to)) driver.Map.Set(unit, to);
                }

                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;
            // —— 结束后：抛出【完成移动】事件 ——  ★新增
            HexMoveEvents.RaiseMoveFinished(driver.UnitRef, driver.UnitRef.Position);

            if (_showing) ShowRange(); // 刷新
        }

        // ===== 拾取 =====
        Hex? PickHexUnderMouse()
        {
            var cam = pickCamera ? pickCamera : Camera.main;
            if (!cam) return null;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, rayMaxDistance, pickMask, QueryTriggerInteraction.Ignore))
                return authoring.Layout.HexAt(hit.point);

            var plane = new Plane(Vector3.up, new Vector3(0f, pickPlaneY, 0f));
            if (!plane.Raycast(ray, out float dist)) return null;
            return authoring.Layout.HexAt(ray.GetPoint(dist));
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
