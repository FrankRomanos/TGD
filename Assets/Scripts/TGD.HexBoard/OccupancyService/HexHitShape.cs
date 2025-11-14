// File: TGD.HexBoard/HexHitShape.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public static class HexHitShape
    {
        public static IEnumerable<Hex> Expand(Hex anchor, Facing4 facing, HitShape shape)
        {
            _ = facing; // Hit shape 目前与朝向无关，但保留签名以兼容旧调用。
            yield return anchor;

            if (shape == null)
                yield break;

            int radius = Mathf.Max(0, shape.radius);
            if (radius <= 0)
                yield break;

            var visited = new HashSet<Hex> { anchor };
            foreach (var hex in Hex.Range(anchor, radius))
            {
                int dist = Hex.Distance(anchor, hex);
                if (dist < 1 || dist > radius)
                    continue;

                if (visited.Add(hex))
                    yield return hex;
            }
        }
    }
}
