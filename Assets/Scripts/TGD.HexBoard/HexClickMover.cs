using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public enum RotationMode { None, OnceAtStart, PerStep } // 默认 OnceAtStart

    public sealed class HexClickMover : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;         // 有就直接给铺好的 tile 上色

        [Header("Range / Path")]
        [Range(1, 12)] public int maxSteps = 3;    // 移动点数（格）
        public LayerMask groundMask = ~0;          // 可点击地面层（要有Collider）
        public float rayMaxDistance = 1000f;

        [Header("Motion")]
        public RotationMode rotationMode = RotationMode.OnceAtStart;
        public bool rotationSixWay = false;      // false=四向(0/90/180/270)，true=六向(每60°)
        public float turnSpeedDegPerSec = 720f;   // 旋转速度
        public float stepSeconds = 0.12f;         // 走一格耗时（匀速）
        public float y = 0.01f;                   // 视觉高度

        [Header("Visuals")]
        public Color rangeColor = new(0.2f, 0.8f, 1f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        // ---------- 运行态 ----------
        bool _showing = false;
        bool _moving = false;

        [Header("Blocking")]
        public bool blockByUnits = true;            // 单位占位会挡路
        public bool blockByPhysics = true;          // 物理障碍会挡路
        public LayerMask obstacleMask = 0;          // 障碍物层（需有 Collider）
        [Range(0.2f, 1.2f)] public float physicsRadiusScale = 0.8f; // 探测半径比例(相对内切半径)
        public float physicsProbeHeight = 2f;       // 探测胶囊高度
        public bool showBlockedAsRed = false;       // 是否把被挡的格子染红（默认不显示）


        // 可达表：目标格 -> 路径（含起点/终点）
        readonly Dictionary<Hex, List<Hex>> _paths = new();
        readonly List<GameObject> _tinted = new();

        void Start() { driver?.EnsureInit(); }
        void OnDisable() { ClearVisuals(); }

        void Update()
        {
            if (!_showing || _moving) return;
            if (authoring == null || driver == null || !driver.IsReady) return;

            if (Input.GetMouseButtonDown(0))
            {
                var h = PickHexUnderMouse();
                if (!h.HasValue) return;
                if (_paths.TryGetValue(h.Value, out var path))
                {
                    StartCoroutine(RunPathTween(path));
                }
            }
        }

        // ==== UI（只有显示/隐藏按钮） ====
        void OnGUI()
        {
            if (authoring == null || driver == null) return;
            var pos = new Vector2(10, 40);
            var size = new Vector2(220, 32);

            if (GUI.Button(new Rect(pos.x, pos.y, size.x, size.y), _showing ? $"Hide Movables (≤{maxSteps})" : $"Show Movables (≤{maxSteps})"))
            {
                if (_showing) HideRange(); else ShowRange();
            }
        }

        // ==== 生成 / 清除 范围 ====
        public void ShowRange()
        {
            if (authoring == null || driver == null || !driver.IsReady) return;
            ClearVisuals();
            _paths.Clear();

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
                    if (dist.ContainsKey(nb)) continue;          // 访问过
                    if (d + 1 > maxSteps) continue;              // 超步数
                    if (IsCellBlocked(nb, start))                // ★ 阻挡过滤
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
                if (d == 0 || d > maxSteps) continue;

                // 回溯路径
                var path = new List<Hex> { cell };
                var cur = cell;
                while (!cur.Equals(start))
                {
                    cur = cameFrom[cur];
                    path.Add(cur);
                }
                path.Reverse();
                _paths[cell] = path;

                // 可视化上色
                if (tiler != null && tiler.TryGetTile(cell, out var go))
                {
                    Tint(go, rangeColor);
                    _tinted.Add(go);
                }
            }

            _showing = true;
        }
        // ==== 放到 HexClickMover 里（替换之前的 ChooseFacingByAngle / Yaw/Facing 工具）====

        // 4 向朝向 <-> Yaw（0=r+, 90=q+, 180=r-, 270=q-）
        static float YawFromFacing(Facing4 f) => f switch
        {
            Facing4.PlusR => 0f,
            Facing4.PlusQ => 90f,
            Facing4.MinusR => 180f,
            _ => 270f, // Facing4.MinusQ
        };

        static Facing4 OppositeOf(Facing4 f) => f switch
        {
            Facing4.PlusR => Facing4.MinusR,
            Facing4.MinusR => Facing4.PlusR,
            Facing4.PlusQ => Facing4.MinusQ,
            _ => Facing4.PlusQ,
        };

        static Facing4 LeftOf(Facing4 f) => f switch  // 逆时针 90°
        {
            Facing4.PlusR => Facing4.MinusQ, // r+ -> q-
            Facing4.MinusQ => Facing4.MinusR, // q- -> r-
            Facing4.MinusR => Facing4.PlusQ,  // r- -> q+
            _ => Facing4.PlusR,  // q+ -> r+
        };

        static Facing4 RightOf(Facing4 f) => f switch // 顺时针 90°
        {
            Facing4.PlusR => Facing4.PlusQ,  // r+ -> q+
            Facing4.PlusQ => Facing4.MinusR, // q+ -> r-
            Facing4.MinusR => Facing4.MinusQ, // r- -> q-
            _ => Facing4.PlusR,  // q- -> r+
        };

        // 当前 Facing 的世界基向量 (x,z)
        static Vector2 BasisXZ(Facing4 f) => f switch
        {
            Facing4.PlusR => new Vector2(0f, 1f),
            Facing4.MinusR => new Vector2(0f, -1f),
            Facing4.PlusQ => new Vector2(1f, 0f),
            _ => new Vector2(-1f, 0f), // MinusQ
        };

        // ★ 按“45° 扇区规则”选择新 Facing（只在点击移动的起步使用）
        static (Facing4 facing, float yaw) ChooseFacingByAngle45(Facing4 curFacing, Vector3 fromW, Vector3 toW)
        {
            Vector3 v3 = toW - fromW; v3.y = 0f;
            if (v3.sqrMagnitude < 1e-6f) return (curFacing, YawFromFacing(curFacing));

            Vector2 v = new Vector2(v3.x, v3.z).normalized;
            Vector2 f = BasisXZ(curFacing);

            // 带符号夹角 δ：左正右负
            float dot = f.x * v.x + f.y * v.y;
            float cross = f.x * v.y - f.y * v.x;
            float delta = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg; // (-180,180]

            float a = Mathf.Abs(delta);

            if (a <= 45f)
            {
                // 保持
                var yaw = YawFromFacing(curFacing);
                return (curFacing, yaw);
            }
            else if (a <= 135f)
            {
                // 旋到相邻 90° 轴：左半平面选 LeftOf，右半平面选 RightOf
                var nf = (delta > 0f) ? LeftOf(curFacing) : RightOf(curFacing);
                return (nf, YawFromFacing(nf));
            }
            else
            {
                // 反向 180°
                var nf = OppositeOf(curFacing);
                return (nf, YawFromFacing(nf));
            }
        }

        // ★按你的“扇区”规则选择新朝向：给定当前Facing与 ‘到目标’ 的世界向量，产出新 Facing + 目标 yaw
     
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

        // ==== 逐格Tween（一次性旋转） ====
        IEnumerator RunPathTween(List<Hex> path)
        {
            if (path == null || path.Count < 2) yield break;
            if (driver == null || !driver.IsReady) yield break;

            _moving = true;

            var layout = authoring.Layout;
            var unit = driver.UnitRef;

            if (rotationMode == RotationMode.OnceAtStart && driver.unitView != null)
            {
                var fromW = authoring.Layout.World(path[0], y);
                var toW = authoring.Layout.World(path[^1], y); // 直接用终点来定扇区
                var (nf, yaw) = ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW);

                yield return RotateToYaw(driver.unitView, yaw);
                driver.UnitRef.Facing = nf; // 锁定为我们选定的四向；行进过程中不再修正
            }

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                // 若你想“每步也转”，把模式切到 PerStep
                if (rotationMode == RotationMode.PerStep && driver.unitView != null)
                {
                    float yaw = YawFromStep(from, to, rotationSixWay);
                    yield return RotateToYaw(driver.unitView, yaw);
                    unit.Facing = FacingFromYaw4(driver.unitView.eulerAngles.y);
                }

                // 位置 tween
                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.01f, stepSeconds);
                    driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                // 到点：更新逻辑位置（占位图如果需要可加 map.Move）
                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;

            // 走完后（若仍显示范围）刷新一次
            if (_showing) ShowRange();
        }

        static Facing4 FacingFromYaw4(float yawDegrees)
        {
            // 把任意角度量化到 0/90/180/270
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            switch (a % 360)
            {
                case 0: return Facing4.PlusR;   // r+（0°）
                case 90: return Facing4.PlusQ;   // q+（90°）
                case 180: return Facing4.MinusR;  // r-（180°）
                case 270: return Facing4.MinusQ;  // q-（270°）
                default: return Facing4.PlusR;
            }
        }


        IEnumerator RotateToYaw(Transform t, float targetYaw)
        {
            float Normalize(float a) => (a % 360f + 360f) % 360f;

            var cur = Normalize(t.eulerAngles.y);
            var dst = Normalize(targetYaw);
            float delta = Mathf.DeltaAngle(cur, dst);

            while (Mathf.Abs(delta) > 0.5f)
            {
                float step = Mathf.Sign(delta) * turnSpeedDegPerSec * Time.deltaTime;
                if (Mathf.Abs(step) > Mathf.Abs(delta)) step = delta;
                t.Rotate(0f, step, 0f, Space.World);
                cur = Normalize(t.eulerAngles.y);
                delta = Mathf.DeltaAngle(cur, dst);
                yield return null;
            }
            var e = t.eulerAngles;
            t.eulerAngles = new Vector3(e.x, dst, e.z);
        }

        // ==== 工具 ====
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

        static float YawFromStep(Hex from, Hex to, bool sixWay)
        {
            int dq = to.q - from.q;
            int dr = to.r - from.r;

            // 先按 Flat-Top 六向给出“基准角”（Unity：0°=+Z；我们改成 +R=0°、-R=180°）
            float yaw6;
            if (dq == +1 && dr == 0) yaw6 = 90f;  // +Q
            else if (dq == +1 && dr == -1) yaw6 = 30f;  // +Q - R
            else if (dq == 0 && dr == -1) yaw6 = 180f;  // -R  （↑）※修正：以前是 0°
            else if (dq == -1 && dr == 0) yaw6 = 270f;  // -Q
            else if (dq == -1 && dr == +1) yaw6 = 210f;  // -Q + R
            else if (dq == 0 && dr == +1) yaw6 = 0f;  // +R  （↓）※修正：以前是 180°
            else yaw6 = 0f;

            if (sixWay) return yaw6;

            // 四向：把 yaw6 量化到最近的 {0,90,180,270}
            float best = 0f, bestAbs = float.MaxValue;
            foreach (var cand in new float[] { 0f, 90f, 180f, 270f })
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(yaw6, cand));
                if (d < bestAbs) { bestAbs = d; best = cand; }
            }
            return best;
        }
        bool IsCellBlocked(Hex cell, Hex startCell)
        {
            // 1) 边界
            var layout = authoring.Layout;
            if (!layout.Contains(cell)) return true;

            // 2) 单位占位
            if (blockByUnits && driver?.Map != null)
            {
                // 起点允许自己占着；其它格若被占用则阻挡
                if (!driver.Map.IsFree(cell) && !cell.Equals(startCell))
                    return true;
            }

            // 3) 物理障碍（按内切圆近似），需要 Obstacles 图层上有 Collider
            if (blockByPhysics && obstacleMask.value != 0)
            {
                // 内切半径 = cellSize * cos30 = 0.866 * cellSize
                float rin = authoring.cellSize * 0.8660254f * physicsRadiusScale;
                Vector3 c = layout.World(cell, y);
                Vector3 p1 = c + Vector3.up * 0.1f;
                Vector3 p2 = c + Vector3.up * physicsProbeHeight;

                if (Physics.CheckCapsule(p1, p2, rin, obstacleMask, QueryTriggerInteraction.Ignore))
                    return true;
            }

            return false;
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
