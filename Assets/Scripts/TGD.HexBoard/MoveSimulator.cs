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
            public float UsedSeconds;
            public int RefundedSeconds;
            public bool Arrived;
        }

        const float MR_MIN = 1f;
        const float MR_MAX = 12f;
        const float ENV_MIN = 0.1f;
        const float ENV_MAX = 5f;

        /// <summary>
        /// Accurate move simulation that mirrors the preview calculation.
        /// MR_base = clamp(baseMoveRateNoEnv, [MR_MIN, MR_MAX])
        /// effMR(step) = clamp(MR_base * envMult(from), [MR_MIN, MR_MAX])
        /// Refund is measured against the planned step cost (MR_click).
        /// </summary>
        public static Result Run(
            IList<Hex> path,
            float baseMoveRateNoEnv,
            float startEnvMultiplier,
            int budgetSeconds,
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0) return res;

            float mrBase = Mathf.Clamp(baseMoveRateNoEnv, MR_MIN, MR_MAX);
            float startEnv = Mathf.Clamp(startEnvMultiplier, ENV_MIN, ENV_MAX);
            float mrClick = Mathf.Clamp(mrBase * startEnv, MR_MIN, MR_MAX);

            res.ReachedPath.Add(path[0]);

            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            float used = 0f;
            int refunds = 0;

            float baseStepCost = 1f / Mathf.Max(MR_MIN, mrClick);

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                float envMult = getEnvMult != null ? getEnvMult(from) : 1f;
                envMult = Mathf.Clamp(envMult, ENV_MIN, ENV_MAX);

                float effMR = Mathf.Clamp(mrBase * envMult, MR_MIN, MR_MAX);
                float stepCost = 1f / Mathf.Max(MR_MIN, effMR);

                if (budget + 1e-6f < stepCost) break;

                budget -= stepCost;
                used += stepCost;
                res.ReachedPath.Add(to);

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
                    Debug.Log($"[MoveSim-Step] i={i} from={from} effMR={effMR:F2} cost={stepCost:F3} budgetLeft={budget:F3}");
            }

            res.UsedSeconds = Mathf.Max(0f, used);
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }
    }
}
