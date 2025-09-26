using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public enum RotationMode { None, OnceAtStart, PerStep } // Ĭ�� OnceAtStart

    public sealed class HexClickMover : MonoBehaviour
    {
        [Header("Refs")]
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;         // �о�ֱ�Ӹ��̺õ� tile ��ɫ

        [Header("Range / Path")]
        [Range(1, 12)] public int maxSteps = 3;    // �ƶ���������
        public LayerMask groundMask = ~0;          // �ɵ������㣨Ҫ��Collider��
        public float rayMaxDistance = 1000f;

        [Header("Motion")]
        public RotationMode rotationMode = RotationMode.OnceAtStart;
        public bool rotationSixWay = false;      // false=����(0/90/180/270)��true=����(ÿ60��)
        public float turnSpeedDegPerSec = 720f;   // ��ת�ٶ�
        public float stepSeconds = 0.12f;         // ��һ���ʱ�����٣�
        public float y = 0.01f;                   // �Ӿ��߶�

        [Header("Visuals")]
        public Color rangeColor = new(0.2f, 0.8f, 1f, 0.85f);
        public Color invalidColor = new(1f, 0.3f, 0.3f, 0.7f);

        // ---------- ����̬ ----------
        bool _showing = false;
        bool _moving = false;

        [Header("Blocking")]
        public bool blockByUnits = true;            // ��λռλ�ᵲ·
        public bool blockByPhysics = true;          // �����ϰ��ᵲ·
        public LayerMask obstacleMask = 0;          // �ϰ���㣨���� Collider��
        [Range(0.2f, 1.2f)] public float physicsRadiusScale = 0.8f; // ̽��뾶����(������а뾶)
        public float physicsProbeHeight = 2f;       // ̽�⽺�Ҹ߶�
        public bool showBlockedAsRed = false;       // �Ƿ�ѱ����ĸ���Ⱦ�죨Ĭ�ϲ���ʾ��


        // �ɴ��Ŀ��� -> ·���������/�յ㣩
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

        // ==== UI��ֻ����ʾ/���ذ�ť�� ====
        void OnGUI()
        {
            if (authoring == null || driver == null) return;
            var pos = new Vector2(10, 40);
            var size = new Vector2(220, 32);

            if (GUI.Button(new Rect(pos.x, pos.y, size.x, size.y), _showing ? $"Hide Movables (��{maxSteps})" : $"Show Movables (��{maxSteps})"))
            {
                if (_showing) HideRange(); else ShowRange();
            }
        }

        // ==== ���� / ��� ��Χ ====
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
                    if (dist.ContainsKey(nb)) continue;          // ���ʹ�
                    if (d + 1 > maxSteps) continue;              // ������
                    if (IsCellBlocked(nb, start))                // �� �赲����
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

                // ����·��
                var path = new List<Hex> { cell };
                var cur = cell;
                while (!cur.Equals(start))
                {
                    cur = cameFrom[cur];
                    path.Add(cur);
                }
                path.Reverse();
                _paths[cell] = path;

                // ���ӻ���ɫ
                if (tiler != null && tiler.TryGetTile(cell, out var go))
                {
                    Tint(go, rangeColor);
                    _tinted.Add(go);
                }
            }

            _showing = true;
        }
        // ==== �ŵ� HexClickMover ��滻֮ǰ�� ChooseFacingByAngle / Yaw/Facing ���ߣ�====

        // 4 ���� <-> Yaw��0=r+, 90=q+, 180=r-, 270=q-��
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

        static Facing4 LeftOf(Facing4 f) => f switch  // ��ʱ�� 90��
        {
            Facing4.PlusR => Facing4.MinusQ, // r+ -> q-
            Facing4.MinusQ => Facing4.MinusR, // q- -> r-
            Facing4.MinusR => Facing4.PlusQ,  // r- -> q+
            _ => Facing4.PlusR,  // q+ -> r+
        };

        static Facing4 RightOf(Facing4 f) => f switch // ˳ʱ�� 90��
        {
            Facing4.PlusR => Facing4.PlusQ,  // r+ -> q+
            Facing4.PlusQ => Facing4.MinusR, // q+ -> r-
            Facing4.MinusR => Facing4.MinusQ, // r- -> q-
            _ => Facing4.PlusR,  // q- -> r+
        };

        // ��ǰ Facing ����������� (x,z)
        static Vector2 BasisXZ(Facing4 f) => f switch
        {
            Facing4.PlusR => new Vector2(0f, 1f),
            Facing4.MinusR => new Vector2(0f, -1f),
            Facing4.PlusQ => new Vector2(1f, 0f),
            _ => new Vector2(-1f, 0f), // MinusQ
        };

        // �� ����45�� ��������ѡ���� Facing��ֻ�ڵ���ƶ�����ʹ�ã�
        static (Facing4 facing, float yaw) ChooseFacingByAngle45(Facing4 curFacing, Vector3 fromW, Vector3 toW)
        {
            Vector3 v3 = toW - fromW; v3.y = 0f;
            if (v3.sqrMagnitude < 1e-6f) return (curFacing, YawFromFacing(curFacing));

            Vector2 v = new Vector2(v3.x, v3.z).normalized;
            Vector2 f = BasisXZ(curFacing);

            // �����żн� �ģ������Ҹ�
            float dot = f.x * v.x + f.y * v.y;
            float cross = f.x * v.y - f.y * v.x;
            float delta = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg; // (-180,180]

            float a = Mathf.Abs(delta);

            if (a <= 45f)
            {
                // ����
                var yaw = YawFromFacing(curFacing);
                return (curFacing, yaw);
            }
            else if (a <= 135f)
            {
                // �������� 90�� �᣺���ƽ��ѡ LeftOf���Ұ�ƽ��ѡ RightOf
                var nf = (delta > 0f) ? LeftOf(curFacing) : RightOf(curFacing);
                return (nf, YawFromFacing(nf));
            }
            else
            {
                // ���� 180��
                var nf = OppositeOf(curFacing);
                return (nf, YawFromFacing(nf));
            }
        }

        // �ﰴ��ġ�����������ѡ���³��򣺸�����ǰFacing�� ����Ŀ�ꡯ ������������������ Facing + Ŀ�� yaw
     
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

        // ==== ���Tween��һ������ת�� ====
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
                var toW = authoring.Layout.World(path[^1], y); // ֱ�����յ���������
                var (nf, yaw) = ChooseFacingByAngle45(driver.UnitRef.Facing, fromW, toW);

                yield return RotateToYaw(driver.unitView, yaw);
                driver.UnitRef.Facing = nf; // ����Ϊ����ѡ���������н������в�������
            }

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                // �����롰ÿ��Ҳת������ģʽ�е� PerStep
                if (rotationMode == RotationMode.PerStep && driver.unitView != null)
                {
                    float yaw = YawFromStep(from, to, rotationSixWay);
                    yield return RotateToYaw(driver.unitView, yaw);
                    unit.Facing = FacingFromYaw4(driver.unitView.eulerAngles.y);
                }

                // λ�� tween
                var fromW = layout.World(from, y);
                var toW = layout.World(to, y);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / Mathf.Max(0.01f, stepSeconds);
                    driver.unitView.position = Vector3.Lerp(fromW, toW, Mathf.Clamp01(t));
                    yield return null;
                }

                // ���㣺�����߼�λ�ã�ռλͼ�����Ҫ�ɼ� map.Move��
                unit.Position = to;
                driver.SyncView();
            }

            _moving = false;

            // �����������ʾ��Χ��ˢ��һ��
            if (_showing) ShowRange();
        }

        static Facing4 FacingFromYaw4(float yawDegrees)
        {
            // ������Ƕ������� 0/90/180/270
            int a = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f) / 90f) * 90;
            switch (a % 360)
            {
                case 0: return Facing4.PlusR;   // r+��0�㣩
                case 90: return Facing4.PlusQ;   // q+��90�㣩
                case 180: return Facing4.MinusR;  // r-��180�㣩
                case 270: return Facing4.MinusQ;  // q-��270�㣩
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

        // ==== ���� ====
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

            // �Ȱ� Flat-Top �����������׼�ǡ���Unity��0��=+Z�����Ǹĳ� +R=0�㡢-R=180�㣩
            float yaw6;
            if (dq == +1 && dr == 0) yaw6 = 90f;  // +Q
            else if (dq == +1 && dr == -1) yaw6 = 30f;  // +Q - R
            else if (dq == 0 && dr == -1) yaw6 = 180f;  // -R  ����������������ǰ�� 0��
            else if (dq == -1 && dr == 0) yaw6 = 270f;  // -Q
            else if (dq == -1 && dr == +1) yaw6 = 210f;  // -Q + R
            else if (dq == 0 && dr == +1) yaw6 = 0f;  // +R  ����������������ǰ�� 180��
            else yaw6 = 0f;

            if (sixWay) return yaw6;

            // ���򣺰� yaw6 ����������� {0,90,180,270}
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
            // 1) �߽�
            var layout = authoring.Layout;
            if (!layout.Contains(cell)) return true;

            // 2) ��λռλ
            if (blockByUnits && driver?.Map != null)
            {
                // ��������Լ�ռ�ţ�����������ռ�����赲
                if (!driver.Map.IsFree(cell) && !cell.Equals(startCell))
                    return true;
            }

            // 3) �����ϰ���������Բ���ƣ�����Ҫ Obstacles ͼ������ Collider
            if (blockByPhysics && obstacleMask.value != 0)
            {
                // ���а뾶 = cellSize * cos30 = 0.866 * cellSize
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
