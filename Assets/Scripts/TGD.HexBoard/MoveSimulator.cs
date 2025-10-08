// File: TGD.HexBoard/MoveSimulator.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public static class MoveSimulator
    {
        public sealed class Result
        {
            public readonly List<Hex> ReachedPath = new();
            public readonly List<float> StepEffectiveRates = new();
            public float UsedSeconds;
            public int RefundedSeconds;
            public bool Arrived;
        }

        public readonly struct StickySample
        {
            public readonly float Multiplier;
            public readonly bool Sticky;

            public StickySample(float multiplier, bool sticky)
            {
                Multiplier = multiplier;
                Sticky = sticky;
            }

            public static StickySample None => new(1f, false);
        }

        const float MR_MIN = 1f;
        const float MR_MAX = 12f;
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;

        /// <summary>
        /// Accurate move simulation that mirrors the preview calculation.
        /// MR_base = clamp(baseMoveRateNoEnv, [MR_MIN, MR_MAX])
        /// effMR(step) = clamp(MR_base * stickyProduct, [MR_MIN, MR_MAX])
        /// Refund is measured against the planned step cost (MR_click).
        /// </summary>
        public static Result Run(
            IList<Hex> path,
            float baseMoveRateNoEnv,
            float plannedMoveRate,
            int budgetSeconds,
            System.Func<Hex, StickySample> getStepModifier,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0) return res;

            float mrBase = Mathf.Clamp(baseMoveRateNoEnv, MR_MIN, MR_MAX);
            float mrPlanned = Mathf.Clamp(plannedMoveRate, MR_MIN, MR_MAX);

            res.ReachedPath.Add(path[0]);

            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            float used = 0f;
            int refunds = 0;

            float baseStepCost = 1f / Mathf.Max(MR_MIN, mrPlanned);

            float activeSticky = 1f;
            float immediateMult = 1f;
            HashSet<Hex> appliedSticky = getStepModifier != null ? new HashSet<Hex>() : null;

            if (getStepModifier != null)
            {
                var startSample = getStepModifier(path[0]);
                float startMult = Mathf.Clamp(startSample.Multiplier <= 0f ? 1f : startSample.Multiplier, ENV_MIN, ENV_MAX);
                if (startSample.Sticky && !Mathf.Approximately(startMult, 1f))
                {
                    appliedSticky.Add(path[0]);
                    activeSticky = Mathf.Clamp(activeSticky * startMult, ENV_MIN, ENV_MAX);
                    immediateMult = 1f;
                }
                else
                {
                    immediateMult = startMult;
                }
            }

            for (int i = 1; i < path.Count; i++)
            {
                float stepMult = Mathf.Clamp(activeSticky * immediateMult, ENV_MIN, ENV_MAX);
                float effMR = Mathf.Clamp(mrBase * stepMult, MR_MIN, MR_MAX);
                float stepCost = 1f / Mathf.Max(MR_MIN, effMR);

                if (budget + 1e-6f < stepCost) break;

                budget -= stepCost;
                used += stepCost;
                var to = path[i];
                res.ReachedPath.Add(to);
                res.StepEffectiveRates.Add(effMR);

                float stepSaved = Mathf.Max(0f, baseStepCost - stepCost);
                saved += stepSaved;

                while (saved >= refundThresholdSeconds)
                {
                    saved -= refundThresholdSeconds;
                    refunds += 1;
                    budget += 1f;
                    if (debug)
                        Debug.Log($"[MoveSim-Refund] +1s thr={refundThresholdSeconds:F2} savedLeft={saved:F3} budget={budget:F3}");
                }

                if (debug)
                    Debug.Log($"[MoveSim-Step] i={i} effMR={effMR:F2} cost={stepCost:F3} budgetLeft={budget:F3}");

                immediateMult = 1f;
                if (getStepModifier != null)
                {
                    var sample = getStepModifier(to);
                    float mult = Mathf.Clamp(sample.Multiplier <= 0f ? 1f : sample.Multiplier, ENV_MIN, ENV_MAX);
                    if (sample.Sticky && !Mathf.Approximately(mult, 1f))
                    {
                        if (appliedSticky.Add(to))
                        {
                            activeSticky = Mathf.Clamp(activeSticky * mult, ENV_MIN, ENV_MAX);
                            if (debug)
                                Debug.Log($"[MoveSim-Sticky] hex={to} mult={mult:F2} active={activeSticky:F2}");
                        }
                    }
                    else
                    {
                        immediateMult = mult;
                    }
                }
            }

            res.UsedSeconds = Mathf.Max(0f, used);
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }
    }
}
