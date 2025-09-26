using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    public static class DemoUtilities
    {
        /// <summary> 将所有单位“世界坐标”对齐到各自格中心（用于回合开始自动校正）。
        /// 这里返回一个列表，包含每个单位应当放置到的世界坐标（由上层 View 去处理 Transform）。
        /// </summary>
        public static List<(Unit unit, Vector3 world)> SnapAllToWorld(HexBoardLayout layout, IEnumerable<Unit> units, float y = 0f)
        {
            var list = new List<(Unit, Vector3)>();
            foreach (var u in units)
            {
                var w = layout.World(u.Position, y);
                list.Add((u, w));
            }
            return list;
        }

        public static IEnumerable<Vector3> WorldPoints(HexBoardLayout layout, IEnumerable<Hex> cells, float y = 0f)
        {
            foreach (var c in cells) yield return layout.World(c, y);
        }

        /// <summary> 取“距离 = r”的提示环（格集），直接喂给渲染逻辑逐格高亮即可。</summary>
        public static IEnumerable<Hex> Ring(Hex center, int r, HexBoardLayout layout)
        {
            foreach (var h in Hex.Ring(center, r)) if (layout.Contains(h)) yield return h;
        }

        /// <summary> 取“距离 ≤ r”的范围（格集）。</summary>
        public static IEnumerable<Hex> Range(Hex center, int r, HexBoardLayout layout)
        {
            foreach (var h in Hex.Range(center, r)) if (layout.Contains(h)) yield return h;
        }
    }
}

