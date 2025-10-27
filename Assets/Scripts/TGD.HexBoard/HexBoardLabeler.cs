using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TGD.HexBoard
{
    /// <summary>
    /// Scene view helper for displaying each hex coordinate and its world position.
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
            var space = HexSpace.Instance;
            if (space == null) return;

#if UNITY_EDITOR
            var style = new GUIStyle(EditorStyles.miniLabel) { fontSize = fontSize };
            style.normal.textColor = Color.white;
#endif
            foreach (var h in layout.Coordinates())
            {
                var w = space.HexToWorld(h, authoring.y + yOffset);
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
