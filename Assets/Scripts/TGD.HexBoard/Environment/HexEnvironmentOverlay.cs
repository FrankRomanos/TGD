// File: TGD.HexBoard/HexEnvironmentOverlay.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class HexEnvironmentOverlay : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public HexBoardTiler tiler;
        public HexEnvironmentSystem env;

        public Color slowColor = new(0.65f, 0.3f, 1f, 0.65f); // 减速（来自 GetSpeedMult）
        public Color fastColor = new(0.2f, 1f, 0.85f, 0.65f);  // 加速
        public Color trapColor = new(1f, 0.25f, 0.25f, 0.75f); // 陷阱（红）
        public Color entangleTrapColor = new(0.1f, 0.45f, 0.15f, 0.7f); // 缠绕陷阱（深绿）
        public Color pitColor = new(0.1f, 0.1f, 0.1f, 0.85f); // 落穴（深色）

        public bool showOnlyChanged = true;
        public float epsilon = 0.001f;
        public bool repaintOnStart = true;
        public bool repaintEveryFrame = false;

        HexAreaPainter _painter;

        void Awake() { _painter = new HexAreaPainter(tiler); }
        void Start() { tiler?.EnsureBuilt(); if (repaintOnStart) Repaint(); }
        void OnDisable() { _painter?.Clear(); }

        [ContextMenu("Repaint Overlay")]
        public void Repaint()
        {
            if (authoring?.Layout == null || env == null) return;
            var L = authoring.Layout;
            var slow = new List<Hex>();
            var fast = new List<Hex>();
            var traps = new List<Hex>();
            var entangleTraps = new List<Hex>();
            var pits = new List<Hex>();

            foreach (var h in L.Coordinates())
            {
                float m = Mathf.Clamp(env.GetSpeedMult(h), 0.1f, 5f);
                if (!showOnlyChanged || Mathf.Abs(m - 1f) > epsilon)
                { if (m < 1f) slow.Add(h); else if (m > 1f) fast.Add(h); }

                if (env.IsEntangleTrap(h)) entangleTraps.Add(h);
                else if (env.IsTrap(h)) traps.Add(h);
                if (env.IsPit(h)) pits.Add(h);
            }

            _painter.Clear();
            if (slow.Count > 0) _painter.Paint(slow, slowColor);
            if (fast.Count > 0) _painter.Paint(fast, fastColor);
            if (pits.Count > 0) _painter.Paint(pits, pitColor);
            if (entangleTraps.Count > 0) _painter.Paint(entangleTraps, entangleTrapColor);
            if (traps.Count > 0) _painter.Paint(traps, trapColor);
        }

        void Update() { if (repaintEveryFrame) Repaint(); }
    }
}
