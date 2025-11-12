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

        public readonly struct GoalScore : IComparable<GoalScore>
        {
            public GoalScore(int primary, int secondary)
                : this(primary, secondary, 0)
            {
            }

            public GoalScore(int primary, int secondary, int tertiary)
            {
                Primary = primary;
                Secondary = secondary;
                Tertiary = tertiary;
            }

            public int Primary { get; }
            public int Secondary { get; }
            public int Tertiary { get; }

            public int CompareTo(GoalScore other)
            {
                int cmp = Primary.CompareTo(other.Primary);
                if (cmp != 0)
                    return cmp;

                cmp = Secondary.CompareTo(other.Secondary);
                if (cmp != 0)
                    return cmp;

                return Tertiary.CompareTo(other.Tertiary);
            }
        }

        public readonly struct NearestPathResult
        {
            readonly bool _found;
            readonly Hex _goal;
            readonly int _distance;
            readonly GoalScore _score;
            readonly BfsResult _bfs;

            internal NearestPathResult(bool found, Hex goal, int distance, GoalScore score, BfsResult bfs)
            {
                _found = found;
                _goal = goal;
                _distance = distance;
                _score = score;
                _bfs = bfs;
            }

            public bool Found => _found;

            public Hex Goal => _goal;

            public int Distance => _distance;

            public GoalScore Score => _score;

            public BfsResult Bfs => _bfs;

            public bool TryBuildPath(List<Hex> buffer)
            {
                if (!_found)
                    return false;

                return _bfs.TryBuildPath(_goal, buffer);
            }
        }

        public static bool TryFindNearest(
            Hex start,
            Func<Hex, bool> isInBounds,
            Func<Hex, bool> canEnter,
            Func<Hex, GoalScore?> evaluateGoal,
            out NearestPathResult result)
        {
            var parents = new Dictionary<Hex, Hex>();
            var distances = new Dictionary<Hex, int>();
            var queue = new Queue<Hex>();

            queue.Enqueue(start);
            parents[start] = start;
            distances[start] = 0;

            bool found = false;
            Hex bestGoal = start;
            int bestDistance = int.MaxValue;
            GoalScore bestScore = default;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int currentDistance = distances[current];

                if (found && currentDistance > bestDistance)
                    break;

                if (evaluateGoal != null)
                {
                    var evaluation = evaluateGoal(current);
                    if (evaluation.HasValue)
                    {
                        var score = evaluation.Value;
                        if (!found ||
                            currentDistance < bestDistance ||
                            (currentDistance == bestDistance && score.CompareTo(bestScore) < 0))
                        {
                            found = true;
                            bestGoal = current;
                            bestDistance = currentDistance;
                            bestScore = score;
                        }
                    }
                }

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
                    distances[next] = currentDistance + 1;
                    queue.Enqueue(next);
                }
            }

            if (found)
            {
                var bfs = new BfsResult(start, parents, distances);
                result = new NearestPathResult(true, bestGoal, bestDistance, bestScore, bfs);
                return true;
            }

            result = default;
            return false;
        }
    }
}
