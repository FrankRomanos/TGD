// File: Assets/Scripts/TGD.HexBoard/HexBoardLabelerGizmos.cs
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TGD.HexBoard
{
    /// 在 Scene 视图里绘制 (q,r)；不生成任何对象，性能安全。
    [ExecuteAlways]
    public sealed class HexBoardLabelerGizmos : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;

        [Header("Draw Options")]
        public bool onlyWhenSelected = true;   // 只在选中时画，避免干扰
        public bool drawInPlayMode = true;     // 运行时也画
        [Range(1, 8)] public int step = 1;     // 每 step 个格子画一次
        public float y = 0.02f;                // 文本抬高
        public float maxDistance = 80f;        // 超远不画，减负

        [Header("Style")]
        public Color color = new Color(1f, 0.95f, 0.4f, 1f);
        public int fontSize = 11;

        void OnDrawGizmos()
        {
            if (!onlyWhenSelected) Draw();
        }

        void OnDrawGizmosSelected()
        {
            if (onlyWhenSelected) Draw();
        }

        void Draw()
        {
#if UNITY_EDITOR
            if (authoring == null || authoring.Layout == null) return;
            if (Application.isPlaying && !drawInPlayMode) return;

            var L = authoring.Layout;
            var cam = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : Camera.current;

            var style = new GUIStyle(EditorStyles.miniBoldLabel);
            style.fontSize = Mathf.Max(8, fontSize);
            style.normal.textColor = color;

            int q0 = L.minQ, r0 = L.minR, W = authoring.width, H = authoring.height;
            int s = Mathf.Max(1, step);
            float maxDist2 = maxDistance * maxDistance;

            for (int q = q0; q < q0 + W; q += s)
                for (int r = r0; r < r0 + H; r += s)
                {
                    var h = new Hex(q, r);
                    var p = L.World(h, y);
                    if (cam != null && (cam.transform.position - p).sqrMagnitude > maxDist2) continue;
                    Handles.Label(p, $"({q},{r})", style);
                }
#endif
        }
    }
}
