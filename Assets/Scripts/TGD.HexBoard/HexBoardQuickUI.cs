using UnityEngine;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// <summary>
    /// ��������ť��
    /// 1) Show Movables ���� ��ʾ/���� ���� moveDistance�� �Ŀɴ�񣨼��� Range����
    /// 2) Move ���� ����ǰ�����ƶ� moveDistance ��ÿ�ε������ѭ����ǰ���ҡ�����󣩡�
    /// Inspector �ɱ༭ moveDistance��Marker �� Cube ����/���١�
    /// </summary>
    public sealed class HexBoardQuickUI : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;   // �� �ǵ��� Inspector ������

        [Range(1, 10)] public int moveDistance = 2;
        public Vector2 guiOffset = new Vector2(10, 40);
        public Vector2 buttonSize = new Vector2(260, 36);

        // �� ��Щ���ڡ�û�� tiler��ʱ�Ż��õ������÷�����
        public float markerY = 0.01f;
        public float markerUnits = 2f;
        public bool showOutOfBounds = false;
        public GameObject markerPrefab = null; // �� �� Tiler ʱ�뱣��Ϊ��
        public float prefabSizeInUnits = 1f;
        public Vector3 prefabLocalEuler = Vector3.zero;
        public Vector3 prefabLocalOffset = Vector3.zero;
        public bool tintWithColors = true;
        public Color inBoundsColor = new(0.2f, 0.8f, 1f, 0.85f);
        public Color outBoundsColor = new(1f, 0.3f, 0.3f, 0.7f);
        // HexBoardQuickUI.cs ���� ��������
        [ContextMenu("Clear Range Visuals Now")]
        public void ClearRangeVisualsNow() => ClearVisuals();

        MoveCardinal currentDir = MoveCardinal.Forward;
        readonly System.Collections.Generic.List<GameObject> markers = new();
        readonly System.Collections.Generic.List<GameObject> tinted = new();
        bool showing = false;

        int lastExpected, lastInBounds, lastOutBounds;

        bool Ready()
        {
            if (authoring == null || driver == null) return false;
            driver.EnsureInit();                         // �� �Ȼ��� Driver
            return driver.IsReady && authoring.Layout != null;
        }

        void OnGUI()
        {
            if (!Ready())
            {
                GUI.Label(new Rect(guiOffset.x, guiOffset.y, 360, 22), "Board/Driver not ready.");
                return;
            }

            var pos = guiOffset;
            if (GUI.Button(new Rect(pos.x, pos.y, buttonSize.x, buttonSize.y), showing ? $"Hide Movables (��{moveDistance})" : $"Show Movables (��{moveDistance})"))
            {
                showing = !showing;
                if (showing) ShowMovables(); else ClearVisuals();
            }
            pos.y += buttonSize.y + 8f;

            if (GUI.Button(new Rect(pos.x, pos.y, buttonSize.x, buttonSize.y), $"Move {moveDistance}  [{currentDir}]"))
            {
                var unit = driver.UnitRef;
                driver.Movement.Execute(new MoveOp(unit, currentDir, moveDistance));
                driver.SyncView();
                if (showing) ShowMovables();
                currentDir = Next(currentDir);
            }
            pos.y += buttonSize.y + 8f;

            GUI.Label(new Rect(pos.x, pos.y, 520, 22),
              $"Expected cells: {lastExpected}  InBounds: {lastInBounds}  Out: {lastOutBounds}");
        }

        void ShowMovables()
        {
            ClearVisuals();
            if (!Ready()) return;

            // �� Tiler ��û�̻������б���գ��Ȳ�һ��
            if (tiler != null && (tiler.Tiles == null || tiler.Tiles.Count == 0)) tiler.Rebuild();

            var layout = authoring.Layout;
            var center = driver.UnitRef.Position;
            int r = moveDistance;
            lastExpected = 1 + 3 * r * (r + 1);
            lastInBounds = 0; lastOutBounds = 0;

            foreach (var h in Hex.Range(center, moveDistance))
            {
                bool inside = layout.Contains(h);
                if (inside) lastInBounds++; else lastOutBounds++;
                if (!inside && !showOutOfBounds) continue;

                if (tiler != null && tiler.TryGetTile(h, out var tileGO))
                {
                    // ����ɫ������ʵ�����ڶ���
                    Tint(tileGO, inside ? inBoundsColor : outBoundsColor);
                    tinted.Add(tileGO);
                }
                else
                {
                    // ֻ����û��שʱ�Ż��ߵ����������ʾ��
                    var markerGO = CreateMarkerFor(h, inside);
                    markers.Add(markerGO);
                }
            }
        }

        void ClearVisuals()
        {
            foreach (var m in markers) if (m) Destroy(m);
            markers.Clear();
            foreach (var t in tinted) if (t) Tint(t, Color.white); // �ָ�ԭɫ
            tinted.Clear();
        }

        void Tint(GameObject go, Color c)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", c); mpb.SetColor("_BaseColor", c);
                r.SetPropertyBlock(mpb);
            }
        }

        GameObject CreateMarkerFor(Hex h, bool inBounds)
        {
            var layout = authoring.Layout; var pos = layout.World(h, authoring.y + markerY);
            GameObject go;
            if (markerPrefab != null)
            {
                go = Instantiate(markerPrefab, pos, Quaternion.identity, this.transform);
                go.name = inBounds ? $"Marker {h.q},{h.r}" : $"OOB {h.q},{h.r}";
                float target = authoring.cellSize * Mathf.Max(0.1f, markerUnits);
                float k = (prefabSizeInUnits <= 1e-6f) ? 1f : (target / prefabSizeInUnits);
                go.transform.localScale = go.transform.localScale * k;
                go.transform.localRotation = Quaternion.Euler(prefabLocalEuler);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                if (tintWithColors) Tint(go, inBounds ? inBoundsColor : outBoundsColor);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = inBounds ? $"Marker {h.q},{h.r}" : $"OOB {h.q},{h.r}";
                go.transform.position = pos;
                float s = authoring.cellSize * Mathf.Max(0.1f, markerUnits) * 0.95f;
                go.transform.localScale = new Vector3(s, authoring.cellSize * 0.02f, s);
                var mr = go.GetComponent<Renderer>(); if (mr) mr.material.color = inBounds ? inBoundsColor : outBoundsColor;
                var col = go.GetComponent<Collider>(); if (col) Destroy(col);
                go.transform.SetParent(this.transform, true);
            }
            return go;
        }
        // ����ѭ����ǰ �� �� �� �� �� �� �� ǰ
        static MoveCardinal Next(MoveCardinal d) => d switch
        {
            MoveCardinal.Forward => MoveCardinal.Right,
            MoveCardinal.Right => MoveCardinal.Backward,
            MoveCardinal.Backward => MoveCardinal.Left,
            _ => MoveCardinal.Forward,
        };
        void OnDisable() => ClearVisuals();
    }
}