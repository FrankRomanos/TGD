// File: TGD.HexBoard/MoveSimulator.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// 执行期逐格模拟（v2，加法口径）：
    /// - 环境倍率先转成“基于基础移速”的加法增量： envAdd = floor(baseBaseMoveRate * (mult - 1))
    /// - 本步有效 MR = max(1, effectiveBaseNoEnv + envAdd)
    /// - 返还按“计划步长”（不含环境）与“实际步长”的差额累计
    public static class MoveSimulator
    {
        public sealed class Result
        {
            public readonly List<Hex> ReachedPath = new();
            public float UsedSeconds;      // float 便于日志；对外扣费仍用整秒
            public int RefundedSeconds;
            public bool Arrived;
        }

        /// <summary>
        /// 统一新口径（乘后再加）：
        /// effMR(step) = baseR * noEnvMultNow * fromMult + flatAfterNow
        /// - noEnvMultNow = buffMult_now * stickyMult_now（不含地形）
        /// - fromMult: 本步起点格的地形倍率
        /// </summary>
        public static Result RunMultiThenFlat(
            IList<Hex> path,
            int baseR,
            float noEnvMultNow,          // buff * sticky（不含地形）
            float flatAfterNow,          // 高贵平加（乘法后再加）
            int budgetSeconds,           // 整秒预算
            System.Func<Hex, float> getEnvMult, // 地形倍率（from）
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0 || baseR <= 0) return res;

            res.ReachedPath.Add(path[0]);
            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            int refunds = 0;

            // “计划步长成本”：不含地形时的基线
            float baseMR_noEnv = StatsMathV2.MR_MultiThenFlat(baseR, new float[] { noEnvMultNow }, flatAfterNow);
            float baseStepCost = 1f / Mathf.Max(0.01f, baseMR_noEnv);

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                float fromMult = Mathf.Clamp(getEnvMult != null ? getEnvMult(from) : 1f, 0.01f, 100f);
                float effMR = StatsMathV2.MR_MultiThenFlat(baseR, new float[] { noEnvMultNow * fromMult }, flatAfterNow);
                float actualCost = 1f / Mathf.Max(0.01f, effMR);

                if (budget + 1e-6f < actualCost) break;

                budget -= actualCost;
                res.ReachedPath.Add(to);

                float stepSaved = Mathf.Max(0f, baseStepCost - actualCost);
                saved += stepSaved;

                while (saved >= refundThresholdSeconds)
                {
                    saved -= refundThresholdSeconds;
                    refunds += 1;
                    budget += 1f;
                    if (debug) Debug.Log($"[MoveSim·MultiThenFlat] refund+1 thr={refundThresholdSeconds:F2} savedLeft={saved:F3} budget={budget:F3} effMR={effMR:F2}");
                }

                if (debug)
                    Debug.Log($"[MoveSim·Step] i={i} from={from} effMR={effMR:F2} cost={actualCost:F3} budgetLeft={budget:F3}");
            }

            res.UsedSeconds = budgetSeconds - budget;
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }


        /// v2 正式版（加法口径）——已经废弃
        public static Result RunAdditive(
            IList<Hex> path,
            int baseBaseMoveRate,                // 纯基础 MR：ctx.BaseMoveRate
            int effectiveBaseNoEnv,              // 基础 +（基于基础的 buff/黏性等）后的 MR（不含环境）
            int budgetSeconds,                   // 本次整秒预算
            System.Func<Hex, float> getEnvMult,  // 环境倍率（>0）；按 from 取
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            Debug.Log($"[MoveSim v2] additive=true asm={typeof(MoveSimulator).Assembly.FullName}");

            var res = new Result();
            if (path == null || path.Count == 0 || effectiveBaseNoEnv <= 0) return res;

            // 起点先入，保证 Count>=1
            res.ReachedPath.Add(path[0]);

            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            int refunds = 0;

            // “计划步长”（不含环境），用于返还累积
            float baseStepCost = 1f / Mathf.Max(0.01f, effectiveBaseNoEnv);

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                float multFrom = Mathf.Clamp(getEnvMult != null ? getEnvMult(from) : 1f, 0.1f, 5f);

                // 把倍率转成“基于基础移速”的加法增量
                int envAdd = Mathf.FloorToInt(Mathf.Max(1, baseBaseMoveRate) * (multFrom - 1f));
                int effMR = Mathf.Max(1, effectiveBaseNoEnv + envAdd);      // 本步有效 MR（最低=1）
                float actualCost = 1f / Mathf.Max(0.01f, effMR);              // 本步实际耗时

                if (debug) Debug.Log($"[MoveSim+Add] step={i} from={from} mult={multFrom:F2} baseNoEnv={effectiveBaseNoEnv} envAdd={envAdd} effMR={effMR} cost={actualCost:F3} budget={budget:F3}");

                if (budget + 1e-6f < actualCost) break; // 预算不足一格 → 截断

                budget -= actualCost;
                res.ReachedPath.Add(to);

                // 返还累积（计划-实际）
                float stepSaved = Mathf.Max(0f, baseStepCost - actualCost);
                saved += stepSaved;
                while (saved >= refundThresholdSeconds)
                {
                    saved -= refundThresholdSeconds;
                    refunds += 1;
                    budget += 1f;
                    if (debug) Debug.Log($"[MoveSim+Add] refund+1 thr={refundThresholdSeconds:F2} saved={saved:F3} budg={budget:F3} effMR={effMR}");
                }
            }

            res.UsedSeconds = budgetSeconds - budget;
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }

        /// 兼容包装（旧签名）：把 baseMoveRate 视为“不含环境的有效 MR”，
        /// 同时用 floor(baseMoveRate) 近似 baseBaseMoveRate。
        /// 建议尽快把调用点切到 RunAdditive(...) 并传入真实 baseBaseMoveRate。
        public static Result Run(
            IList<Hex> path,
            float baseMoveRate,                 // 视为“不含环境的有效 MR”
            int budgetSeconds,
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            int effectiveNoEnv = Mathf.Max(1, Mathf.FloorToInt(baseMoveRate));
            int baseBaseMoveRate = effectiveNoEnv; // 近似；推荐调用点改为传 ctx.BaseMoveRate
            if (debug) Debug.LogWarning("[MoveSim v2] Run(legacy) wrapper in use → please migrate to RunAdditive(...) with ctx.BaseMoveRate.");
            return RunAdditive(path, baseBaseMoveRate, effectiveNoEnv, budgetSeconds, getEnvMult, refundThresholdSeconds, debug);
        }
    }
}
