// File: TGD.CombatV2/AttackPlannerV2.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public static class AttackPlannerV2
    {
        public sealed class Plan
        {
            public Hex target;
            public Hex chosenLanding;
            public List<Hex> rawShortestPath;   // 起点→落脚点（无加权）
            public List<Hex> truncatedPath;     // 预算截断后的执行路径
            public bool canHit;                 // 是否到达落脚点
            public float usedSeconds;
            public int refundedSeconds;
        }

        public static Plan PlanMeleeApproach(
            HexBoardLayout layout,
            HexOccupancy occ,
            IGridActor attacker,
            Hex start,
            Hex target,
            int meleeRange,
            System.Func<Hex, bool> isPit,
            int budgetSeconds,
            float baseMoveRate,
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f)
        {
            var plan = new Plan { target = target };
            if (layout == null || occ == null) return plan;

            // 1) 候选落脚点（整组落位；pit 不能站）
            var candidates = new List<Hex>();
            foreach (var h in Hex.Ring(target, meleeRange))
            {
                if (!layout.Contains(h)) continue;
                if (isPit != null && isPit(h)) continue;
                if (!occ.CanPlace(attacker, h, attacker.Facing, ignore: attacker)) continue;
                candidates.Add(h);
            }
            if (candidates.Count == 0) return plan;

            // 2) 最短路径（不考虑环境），中途不穿人/不踩 pit
            List<Hex> best = null; int bestLen = int.MaxValue; Hex chosen = default;

            foreach (var landing in candidates)
            {
                var raw = ShortestPath(
                    start, landing,
                    isBlocked: (cell) =>
                    {
                        if (!layout.Contains(cell)) return true;
                        if (isPit != null && isPit(cell)) return true;
                        if (occ.IsBlocked(cell, attacker) && !cell.Equals(start) && !cell.Equals(landing))
                            return true;
                        return false;
                    });

                if (raw != null && raw.Count > 0 && raw.Count < bestLen)
                {
                    best = raw; bestLen = raw.Count; chosen = landing;
                }
            }

            plan.chosenLanding = chosen;
            plan.rawShortestPath = best;
            if (best == null || best.Count < 2) return plan;

            // 3) 执行期：用 MoveSimulator 截断/返还
            var sim = MoveSimulator.Run(
                best,
                baseMoveRate,
                Mathf.Max(0, budgetSeconds),
                getEnvMult,
                refundThresholdSeconds
            );

            plan.truncatedPath = sim.ReachedPath;
            plan.usedSeconds = sim.UsedSeconds;
            plan.refundedSeconds = sim.RefundedSeconds;
            plan.canHit = (sim.ReachedPath != null && sim.ReachedPath.Count == best.Count);

            return plan;
        }

        // —— 内置简单 BFS（无加权） —— //
        static List<Hex> ShortestPath(Hex start, Hex goal, System.Func<Hex, bool> isBlocked)
        {
            var q = new Queue<Hex>();
            var came = new Dictionary<Hex, Hex>();
            q.Enqueue(start); came[start] = start;

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(goal)) break;
                foreach (var nb in Neigh6(cur))
                {
                    if (came.ContainsKey(nb)) continue;
                    if (isBlocked != null && isBlocked(nb)) continue;
                    came[nb] = cur; q.Enqueue(nb);
                }
            }

            if (!came.ContainsKey(goal)) return null;
            var path = new List<Hex> { goal };
            var c = goal;
            while (!c.Equals(start)) { c = came[c]; path.Add(c); }
            path.Reverse();
            return path;
        }

        static IEnumerable<Hex> Neigh6(Hex h)
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
