using System;
using System.Collections.Generic;

namespace TGD.CoreV2
{
    /// <summary>
    /// Breadth-first search helpers for hex grids. The routines operate purely on
    /// delegates supplied by the caller so the combat controllers can share a
    /// consistent shortest-path implementation.
    /// </summary>
    public static class HexPathfinding
    {
        public readonly struct BfsResult
        {
            readonly Hex _start;
            readonly Dictionary<Hex, Hex> _parents;
            readonly Dictionary<Hex, int> _distances;

            internal BfsResult(Hex start, Dictionary<Hex, Hex> parents, Dictionary<Hex, int> distances)
            {
                _start = start;
                _parents = parents;
                _distances = distances;
            }

            public Hex Start => _start;

            public bool TryGetDistance(Hex cell, out int distance)
            {
                if (_distances != null && _distances.TryGetValue(cell, out var stored))
                {
                    distance = stored;
                    return true;
                }

                distance = default;
                return false;
            }

            public bool TryBuildPath(Hex goal, List<Hex> buffer)
            {
                if (buffer == null || _parents == null || !_parents.ContainsKey(goal))
                    return false;

                buffer.Clear();
                var current = goal;
                buffer.Add(current);
                while (!current.Equals(_start))
                {
                    current = _parents[current];
                    buffer.Add(current);
                }
                buffer.Reverse();
                return true;
            }
        }

        public static BfsResult Run(Hex start, Func<Hex, bool> isInBounds, Func<Hex, bool> canEnter)
        {
            var parents = new Dictionary<Hex, Hex>();
            var distances = new Dictionary<Hex, int>();
            var queue = new Queue<Hex>();

            queue.Enqueue(start);
            parents[start] = start;
            distances[start] = 0;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int i = 0; i < Hex.Directions.Length; i++)
                {
                    var next = current + Hex.Directions[i];
                    if (parents.ContainsKey(next))
                        continue;
                    if (isInBounds != null && !isInBounds(next))
                        continue;
                    if (canEnter != null && !canEnter(next))
                        continue;

                    parents[next] = current;
                    distances[next] = distances[current] + 1;
                    queue.Enqueue(next);
                }
            }

            return new BfsResult(start, parents, distances);
        }
    }
}
