using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TGD.HexBoard
{
    /// <summary>
    /// 在 Scene 视图用标签显示每个格子的 (q,r) 与其世界坐标 (x,z) ——便于 Debug。
    /// 可选绘制格中心小标记。
    /// </summary>
    [ExecuteAlways]
    public sealed class HexBoardLabeler : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public bool showLabels = true;
        public bool showCenters = true;
        public int fontSize = 11;
        public float yOffset = 0.02f;

        void OnDrawGizmos()
        {
            if (authoring == null || authoring.Layout == null) return;
            var layout = authoring.Layout;

#if UNITY_EDITOR
            var style = new GUIStyle(EditorStyles.miniLabel) { fontSize = fontSize };
            style.normal.textColor = Color.white;
#endif
            foreach (var h in layout.Coordinates())
            {
                var w = layout.World(h, authoring.y + yOffset);
                if (showCenters)
                {
                    Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
                    Gizmos.DrawSphere(w, authoring.cellSize * 0.05f);
                }
#if UNITY_EDITOR
                if (showLabels)
                {
                    Handles.color = Color.white;
                    Handles.Label(w, $"({h.q},{h.r})", style);
                }
#endif
            }
        }
    }
}