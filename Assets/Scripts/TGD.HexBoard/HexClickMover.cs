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
    /// 正式版点击移动：无 OnGUI / 无 HUD。
    /// 需要显示可达范围时，外部 UI 调用 ShowRange()/HideRange()。
    /// </summary>
    public sealed class HexClickMover : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;   // 提供 Unit/Map/Sync
        public HexBoardTiler tiler;    // 瓦片着色

        [Header("Action Config & Cost")]
        public MoveActionConfig config;
        public MonoBehaviour costProvider;
        IMoveCostService _cost;

        [Header("Picking")]
        public Camera pickCamera;               // 可手动指定；为空则用 Camera.main
        public LayerMask pickMask = ~0;            // 地面/瓦片层
        public float rayMaxDistance = 2000f;
        public float pickPlaneY = 0.01f;       // 射线没命中时回退水平面

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

        // runtime
        bool _showing = false, _moving = false;
        readonly Dictionary<Hex, List<Hex>> _paths = new();
        HexAreaPainter _painter;

        void Awake()
        {
            _cost = costProvider as IMoveCostService;
            _painter = new HexAreaPainter(tiler);
        }

        void Start() { driver?.EnsureInit(); }
        void OnDisable() { _painter?.Clear(); _paths.Clear(); _showing = false; }

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
            }
        }

        // ===== 提供给外部 UI 调用 =====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady) return;

            _painter.Clear();
            _paths.Clear();

            int steps = (config != null) ? Mathf.Clamp(config.fallbackSteps, 0, config.stepsCap) : 3;

            var layout = authoring.Layout;
            var start = driver.UnitRef.Position;

            // 统一阻挡（含越界）
            var defaultBlocker = HexAreaUtil.MakeDefaultBlocker(
                authoring, driver?.Map, start,
                blockByUnits, blockByPhysics,
                obstacleMask, physicsRadiusScale, physicsProbeHeight, includeTriggerColliders, y
            );
            bool BlockOrOOB(Hex cell) => (layout != null && !layout.Contains(cell)) || defaultBlocker(cell);

            // BFS：可达 + 路径 + 被拦
            var result = HexMovableRange.Compute(layout, driver.Map, start, steps, BlockOrOOB);

            foreach (var kv in result.Paths) _paths[kv.Key] = kv.Value;

            _painter.Paint(result.Paths.Keys, rangeColor);
            if (showBlockedAsRed) _painter.Paint(result.Blocked, invalidColor);

            _showing = true;
        }

        public void HideRange()
        {
            _painter.Clear();
            _paths.Clear();
            _showing = false;
        }

        // ===== 逐格移动 + 起步一次性转向 =====
        IEnumerator RunPathTween(List<Hex> path)
        {
            if (path == null || path.Count < 2) yield break;
            if (authoring == null || driver == null || !driver.IsReady) yield break;

            // 成本
            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config) || !_cost.HasEnough(driver.UnitRef, config)) yield break;
                _cost.Pay(driver.UnitRef, config);
            }

            _moving = true;

            // 起步一次性转向（45°/135° 扇区）
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
            }

            var layout = authoring.Layout;
            var unit = driver.UnitRef;

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.01f, stepSeconds);
                    if (driver.unitView != null)
                        driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                // 逻辑位置 & 地图占位 —— Move 失败兜底 Set
                if (driver.Map != null)
                {
                    if (!driver.Map.Move(unit, to))
                        driver.Map.Set(unit, to);
                }

                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;

            if (_showing) ShowRange(); // 刷新
        }

        // ===== 拾取：优先 pickCamera -> Camera.main；失败回退水平面 =====
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
    }
}
