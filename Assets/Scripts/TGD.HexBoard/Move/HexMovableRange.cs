using System.Collections.Generic;

namespace TGD.HexBoard
{
    public sealed class MovableRangeResult
    {
        // 终点 → 最短路径（含起点与终点）
        public readonly Dictionary<Hex, List<Hex>> Paths = new();
        // 被阻挡的格子（可用作红色提示）
        public readonly HashSet<Hex> Blocked = new();
    }

    public static class HexMovableRange
    {
        public static MovableRangeResult Compute(
            HexBoardLayout layout,
            HexBoardMap<Unit> map,
            Hex start,
            int steps,
            System.Func<Hex, bool> isBlocked // 越界/占位/物理障碍的统一判定
        )
        {
            var res = new MovableRangeResult();
            if (steps <= 0) return res;

            var frontier = new Queue<Hex>();
            var dist = new Dictionary<Hex, int>();
            var came = new Dictionary<Hex, Hex>();

            frontier.Enqueue(start);
            dist[start] = 0;
            came[start] = start;

            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                int d = dist[cur];

                foreach (var nb in SixNeighbors(cur))
                {
                    if (dist.ContainsKey(nb)) continue;
                    if (d + 1 > steps) continue;

                    bool blocked = isBlocked != null && isBlocked(nb);
                    if (blocked)
                    {
                        res.Blocked.Add(nb);
                        continue;
                    }

                    dist[nb] = d + 1;
                    came[nb] = cur;
                    frontier.Enqueue(nb);
                }
            }

            // 回溯最短路径
            foreach (var kv in dist)
            {
                var cell = kv.Key; int d = kv.Value;
                if (d == 0 || d > steps) continue;

                var path = new List<Hex> { cell };
                var cur = cell;
                while (!cur.Equals(start))
                {
                    cur = came[cur];
                    path.Add(cur);
                }
                path.Reverse();
                res.Paths[cell] = path;
            }

            return res;
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
    }
}
