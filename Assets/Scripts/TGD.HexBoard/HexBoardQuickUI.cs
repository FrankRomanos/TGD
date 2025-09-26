using UnityEngine;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    /// <summary>
    /// 仅两个按钮：
    /// 1) Show Movables —— 显示/隐藏 “≤ moveDistance” 的可达格（简单用 Range）。
    /// 2) Move —— 按当前方向移动 moveDistance 格；每次点击后方向循环（前→右→后→左）。
    /// Inspector 可编辑 moveDistance；Marker 用 Cube 生成/销毁。
    /// </summary>
    public sealed class HexBoardQuickUI : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public HexBoardTiler tiler;   // ← 记得在 Inspector 里拖上

        [Range(1, 10)] public int moveDistance = 2;
        public Vector2 guiOffset = new Vector2(10, 40);
        public Vector2 buttonSize = new Vector2(260, 36);

        // ↓ 这些仅在“没有 tiler”时才会用到（备用方案）
        public float markerY = 0.01f;
        public float markerUnits = 2f;
        public bool showOutOfBounds = false;
        public GameObject markerPrefab = null; // ★ 有 Tiler 时请保持为空
        public float prefabSizeInUnits = 1f;
        public Vector3 prefabLocalEuler = Vector3.zero;
        public Vector3 prefabLocalOffset = Vector3.zero;
        public bool tintWithColors = true;
        public Color inBoundsColor = new(0.2f, 0.8f, 1f, 0.85f);
        public Color outBoundsColor = new(1f, 0.3f, 0.3f, 0.7f);
        // HexBoardQuickUI.cs —— 放在类里
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
            driver.EnsureInit();                         // ★ 先唤醒 Driver
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
            if (GUI.Button(new Rect(pos.x, pos.y, buttonSize.x, buttonSize.y), showing ? $"Hide Movables (≤{moveDistance})" : $"Show Movables (≤{moveDistance})"))
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

            // 若 Tiler 还没铺或运行中被清空，先补一次
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
                    // 仅上色，不再实例化第二层
                    Tint(tileGO, inside ? inBoundsColor : outBoundsColor);
                    tinted.Add(tileGO);
                }
                else
                {
                    // 只有在没铺砖时才会走到这里（备用显示）
                    var markerGO = CreateMarkerFor(h, inside);
                    markers.Add(markerGO);
                }
            }
        }

        void ClearVisuals()
        {
            foreach (var m in markers) if (m) Destroy(m);
            markers.Clear();
            foreach (var t in tinted) if (t) Tint(t, Color.white); // 恢复原色
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
        // 方向循环：前 → 右 → 后 → 左 → 前
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