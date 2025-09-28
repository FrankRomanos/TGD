using System;
using System.Collections.Generic;

namespace TGD.HexBoard
{
    public sealed class MovableRangeResult
    {
        // �յ� �� ���·������������յ㣩
        public readonly Dictionary<Hex, List<Hex>> Paths = new();
        // ���赲�ĸ��ӣ���������ɫ��ʾ��
        public readonly HashSet<Hex> Blocked = new();
    }

    public static class HexMovableRange
    {
        public static MovableRangeResult Compute(
            HexBoardLayout layout,
            HexBoardMap<Unit> map,
            Hex start,
            int steps,
            System.Func<Hex, bool> isBlocked // Խ��/ռλ/�����ϰ���ͳһ�ж�
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

            // �������·��
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
        /// <summary>
        /// ��Ȩ�ɴDijkstra����cost(h)=1/ speedMult(h)��
        /// ���أ��ɴ�� -> ·��������㵽������
        /// </summary>
        public static (Dictionary<Hex, List<Hex>> Paths, HashSet<Hex> Blocked)
            ComputeWeighted(
                HexBoardLayout layout,
                Hex start,
                float budget,                        // ����Ԥ�㣨���������� steps��
                Func<Hex, bool> isBlocked,           // ��Ȼ�������е��赲��ռλ/����/Խ��ȣ�
                Func<Hex, float> getSpeedMult        // ���� HexEnvironmentSystem
            )
        {
            var blocked = new HashSet<Hex>();
            var dist = new Dictionary<Hex, float>();
            var prev = new Dictionary<Hex, Hex>();
            var pq = new PriorityQueue<Hex, float>();

            dist[start] = 0f;
            pq.Enqueue(start, 0f);

            while (pq.Count > 0)
            {
                var cur = pq.Dequeue();

                // ��֦
                if (dist[cur] > budget + 1e-4f) continue;

                foreach (var nb in SixNeighbors(cur))
                {
                    if (!layout.Contains(nb))
                    {
                        blocked.Add(nb);
                        continue;
                    }
                    if (isBlocked != null && isBlocked(nb))
                    {
                        blocked.Add(nb);
                        continue;
                    }

                    float mult = Math.Max(0.1f, getSpeedMult != null ? getSpeedMult(nb) : 1f);
                    float stepCost = 1f / mult; // mult=0.5 => 2��ɱ���mult=2 => 0.5��ɱ�
                    float nd = dist[cur] + stepCost;

                    if (nd > budget + 1e-4f) continue;

                    if (!dist.TryGetValue(nb, out var old) || nd + 1e-6f < old)
                    {
                        dist[nb] = nd;
                        prev[nb] = cur;
                        pq.Enqueue(nb, nd);
                    }
                }
            }
            // ��װ·��
            var paths = new Dictionary<Hex, List<Hex>>();
            foreach (var kv in dist)
            {
                var cell = kv.Key;
                if (cell.Equals(start)) continue;

                var path = new List<Hex>();
                var cur = cell;
                while (!cur.Equals(start))
                {
                    path.Add(cur);
                    cur = prev[cur];
                }
                path.Add(start);
                path.Reverse();
                paths[cell] = path;
            }

            return (paths, blocked);
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
        // �������ȶ��У�.NET 4.x ������ PriorityQueue��
        sealed class PriorityQueue<T, TKey> where TKey : IComparable<TKey>
        {
            readonly List<(T item, TKey key)> data = new();
            public int Count => data.Count;
            public void Enqueue(T item, TKey key)
            {
                data.Add((item, key));
                int i = data.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (data[i].key.CompareTo(data[p].key) >= 0) break;
                    (data[i], data[p]) = (data[p], data[i]);
                    i = p;
                }
            }
            public T Dequeue()
            {
                var root = data[0].item;
                int last = data.Count - 1;
                data[0] = data[last];
                data.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, s = i;
                    if (l < data.Count && data[l].key.CompareTo(data[s].key) < 0) s = l;
                    if (r < data.Count && data[r].key.CompareTo(data[s].key) < 0) s = r;
                    if (s == i) break;
                    (data[i], data[s]) = (data[s], data[i]);
                    i = s;
                }
                return root;
            }
        }

    }
}
