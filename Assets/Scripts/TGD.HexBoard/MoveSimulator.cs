// File: TGD.HexBoard/MoveSimulator.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 执行期逐格模拟：按环境倍率结算预算/返还；可被 CombatV2 等复用
    public static class MoveSimulator
    {
        public sealed class Result
        {
            // 公共可读：外部需要读路径；内部在 Run 中填充
            public readonly List<Hex> ReachedPath = new();
            public float UsedSeconds;      // 在 Run 内写，外部只读
            public int RefundedSeconds;    // 在 Run 内写，外部只读
            public bool Arrived;           // 到达完整路径？
        }

        /// 逐格模拟；stepCost = 1 / (baseMoveRate * envMult)
        public static Result Run(
            IList<Hex> path,
            float baseMoveRate,           // 格/秒
            int budgetSeconds,            // 整秒
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0 || baseMoveRate <= 0f )
                return res;
            // ★ 无论预算多少，先把起点放进去，保证 Count>=1
            res.ReachedPath.Add(path[0]);


            float budget = Mathf.Max(0f, budgetSeconds); // ★ 允许为 0
            float saved = 0f;
            int refunds = 0;
            float baseStepCost = 1f / Mathf.Max(0.01f, baseMoveRate);

            for (int i = 1; i < path.Count; i++)
            {
              
                var from = path[i - 1];
                var to = path[i];
                // ★ 与执行期一致：按 from 的环境倍率结算该步成本
                float multFrom = Mathf.Clamp(getEnvMult != null ? getEnvMult(from) : 1f, 0.1f, 5f);
                float actualCost = 1f / Mathf.Max(0.01f, baseMoveRate * multFrom);

                if (budget + 1e-6f < actualCost) break; // 预算不足一格

                budget -= actualCost;
                res.ReachedPath.Add(to);

                float stepSaved = Mathf.Max(0f, baseStepCost - actualCost);
                saved += stepSaved;
                while (saved >= refundThresholdSeconds)
                {
                    saved -= refundThresholdSeconds;
                    refunds += 1;
                    budget += 1f;
                    if (debug) Debug.Log($"[MoveSim] refund+1 (threshold={refundThresholdSeconds:F2}) → refunds={refunds} savedLeft={saved:F3} budget={budget:F3}");
                }
            }

            res.UsedSeconds = budgetSeconds - budget;
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }
    }
}
