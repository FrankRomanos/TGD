using UnityEngine;


namespace TGD.HexBoard
{
    /// <summary>
    /// 在 Scene 视图画当前单位的“距离 = radius”的环（逐格）。
    /// </summary>
    [ExecuteAlways]
    public sealed class HexRingGizmos : MonoBehaviour
    {
        public HexBoardAuthoringLite authoring;
        public HexBoardTestDriver driver;
        public int radius = 1;
        public Color color = new Color(1f, 0.92f, 0.16f, 0.9f);


        void OnDrawGizmos()
        {
            if (authoring == null || driver == null) return;
            var layout = authoring.Layout; if (layout == null) return;
            var center = driver != null ? driver.GetType().GetField("unit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(driver) as Unit : null;
            if (center == null) return;
            var u = center as Unit; var c = u.Position;


            Gizmos.color = color;
            foreach (var h in Hex.Ring(c, radius))
            {
                if (!layout.Contains(h)) continue;
                var w = layout.World(h, authoring.y);
                Gizmos.DrawWireCube(w, new Vector3(authoring.cellSize * 0.9f, authoring.cellSize * 0.02f, authoring.cellSize * 0.9f));
            }
        }
    }
}