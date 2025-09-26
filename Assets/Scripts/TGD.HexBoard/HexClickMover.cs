using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    // ============ 内置：极简消耗接口（若工程已有同名接口，删掉这里这段） ============
    public interface IMoveCostService
    {
        bool IsOnCooldown(Unit unit, MoveActionConfig cfg);
        bool HasEnough(Unit unit, MoveActionConfig cfg);
        void Pay(Unit unit, MoveActionConfig cfg);
    }

    // ============ 内置：移动动作配置（若工程已有同名类，删掉这里这段） ============
    [CreateAssetMenu(menuName = "TGD/Combat/Move Action Config")]
    public class MoveActionConfig : ScriptableObject
    {
        [Header("Costs")]
        public int energyCost = 10;
        public float timeCostSeconds = 1f;
        public float cooldownSeconds = 0f;
        public string actionId = "Move";

        [Header("Distance")]
        public int fallbackSteps = 3;   // 先用它；等接入 Stats 后再改
        public int stepsCap = 12;

        [Header("Facing")]
        public float keepDeg = 45f;
        public float turnDeg = 135f;
        public float turnSpeedDegPerSec = 720f;
    }

    /// <summary>
    /// 点击移动（玩家驱动）——可达圆盘(BFS) + 一次性转向(45°扇区) + 逐格Tween。
    /// 与“强制位移”彻底解耦；强制位移请用 MovementSystem.ExecuteForced(...)
    /// </summary>
    public sealed class HexClickMover : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;

        [Header("Action Config & Cost")]
        public MoveActionConfig config;        // 直接在 Project 里建一个资源拖上来
        public MonoBehaviour costProvider;  // 可选：实现 IMoveCostService 的组件
        IMoveCostService _cost;

        [Header("Raycast")]
        public LayerMask groundMask = ~0;
        public float rayMaxDistance = 1000f;

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

        // 运行态
        bool _showing = false, _moving = false;
        readonly Dictionary<Hex, List<Hex>> _paths = new();
        readonly List<GameObject> _tinted = new();

        void Awake() { _cost = costProvider as IMoveCostService; }
        void Start() { driver?.EnsureInit(); }
        void OnDisable() { ClearVisuals(); _paths.Clear(); _showing = false; }

        void Update()
        {
            if (!_showing || _moving) return;
            if (authoring == null || driver == null || !driver.IsReady) return;

            if (Input.GetMouseButtonDown(0))
            {
                var h = PickHexUnderMouse();
                if (!h.HasValue) return;
                if (_paths.TryGetValue(h.Value, out var path))
                    StartCoroutine(RunPathTween(path));
            }
        }

        void OnGUI()
        {
            if (authoring == null || driver == null) return;
            var r = new Rect(10, 40, 240, 32);
            if (GUI.Button(r, _showing ? "Hide Movables" : "Show Movables"))
            {
                if (_showing) HideRange(); else ShowRange();
            }
        }

        // ===== 显示/隐藏可达 =====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady) return;

            ClearVisuals();
            _paths.Clear();

            // 先用配置步数；等你接入 Stats 再切换
            int steps = (config != null) ? Mathf.Clamp(config.fallbackSteps, 0, config.stepsCap) : 3;

            var layout = authoring.Layout;
            var start = driver.UnitRef.Position;

            var frontier = new Queue<Hex>();
            var cameFrom = new Dictionary<Hex, Hex>();
            var dist = new Dictionary<Hex, int>();
            frontier.Enqueue(start);
            cameFrom[start] = start;
            dist[start] = 0;

            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                int d = dist[cur];

                foreach (var nb in SixNeighbors(cur))
                {
                    if (dist.ContainsKey(nb)) continue;
                    if (d + 1 > steps) continue;

                    if (IsCellBlocked(nb, start))
                    {
                        if (showBlockedAsRed && tiler != null && tiler.TryGetTile(nb, out var blockedGo))
                            Tint(blockedGo, invalidColor);
                        continue;
                    }

                    dist[nb] = d + 1;
                    frontier.Enqueue(nb);
                    cameFrom[nb] = cur;
                }
            }

            foreach (var kv in dist)
            {
                var cell = kv.Key; int d = kv.Value;
                if (d == 0 || d > steps) continue;

                var path = new List<Hex> { cell };
                var cur = cell;
                while (!cur.Equals(start))
                {
                    cur = cameFrom[cur];
                    path.Add(cur);
                }
                path.Reverse();
                _paths[cell] = path;

                if (tiler != null && tiler.TryGetTile(cell, out var go))
                {
                    Tint(go, rangeColor);
                    _tinted.Add(go);
                }
            }

            _showing = true;
        }

        public void HideRange()
        {
            ClearVisuals();
            _paths.Clear();
            _showing = false;
        }

        void ClearVisuals()
        {
            foreach (var go in _tinted) if (go) Tint(go, Color.white);
            _tinted.Clear();
        }

        // ===== 逐格移动 + 起步一次性转向 =====
        IEnumerator RunPathTween(List<Hex> path)
        {
            if (path == null || path.Count < 2) yield break;
            if (authoring == null || driver == null || !driver.IsReady) yield break;

            // 成本/冷却（可选）
            if (_cost != null && config != null)
            {
                if (_cost.IsOnCooldown(driver.UnitRef, config) || !_cost.HasEnough(driver.UnitRef, config))
                    yield break;
                _cost.Pay(driver.UnitRef, config);
            }

            _moving = true;

            // 起步一次性转向（45°/135° 扇区）
            if (driver.unitView != null)
            {
                // 起步一次性转向（使用 HexFacingUtil）
                var fromW = authoring.Layout.World(path[0], y);
                var toW = authoring.Layout.World(path[^1], y);
                float keep = (config != null) ? config.keepDeg : 45f;
                float turn = (config != null) ? config.turnDeg : 135f;
                float speed = (config != null) ? config.turnSpeedDegPerSec : 720f;

                var (nf, yaw) = HexFacingUtil.ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW, keep, turn);
                yield return HexFacingUtil.RotateToYaw(driver.unitView, yaw, speed);
                driver.UnitRef.Facing = nf;
            }

            var layout = authoring.Layout;
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
                    driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                driver.UnitRef.Position = to;
                driver.SyncView();
            }

            _moving = false;
            if (_showing) ShowRange();
        }

        // ===== 阻挡 =====
        bool IsCellBlocked(Hex cell, Hex startCell)
        {
            var layout = authoring.Layout;

            if (!layout.Contains(cell)) return true;

            if (blockByUnits && driver?.Map != null)
            {
                if (!driver.Map.IsFree(cell) && !cell.Equals(startCell))
                    return true;
            }

            if (blockByPhysics && obstacleMask.value != 0)
            {
                float rin = authoring.cellSize * 0.8660254f * physicsRadiusScale;
                var qti = includeTriggerColliders ? QueryTriggerInteraction.Collide
                                                  : QueryTriggerInteraction.Ignore;

                Vector3 c = layout.World(cell, y);
                if (Physics.CheckSphere(c + Vector3.up * 0.5f, rin, obstacleMask, qti))
                    return true;

                Vector3 p1 = c + Vector3.up * 0.1f;
                Vector3 p2 = c + Vector3.up * physicsProbeHeight;
                if (Physics.CheckCapsule(p1, p2, rin, obstacleMask, qti))
                    return true;
            }

            return false;
        }

        // ===== 工具 =====
        Hex? PickHexUnderMouse()
        {
            var cam = Camera.main; if (!cam) return null;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, rayMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                return authoring.Layout.HexAt(hit.point);
            return null;
        }

        static IEnumerable<Hex> SixNeighbors(Hex h)
        {
            yield return new Hex(h.q + 1, h.r + 0);
            yield return new Hex(h.q + 1, h.r - 1);
            yield return new Hex(h.q + 0, h.r - 1);
            yield return new Hex(h.q - 1, h.r + 0);
            yield return new Hex(h.q - 1, h.r + 1);
            yield return new Hex(h.q + 0, h.r + 1);
        }

        void Tint(GameObject go, Color c)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", c);
                mpb.SetColor("_BaseColor", c);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
