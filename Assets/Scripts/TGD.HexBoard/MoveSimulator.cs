// File: TGD.HexBoard/MoveSimulator.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// ִ�������ģ�⣨v2���ӷ��ھ�����
    /// - ����������ת�ɡ����ڻ������١��ļӷ������� envAdd = floor(baseBaseMoveRate * (mult - 1))
    /// - ������Ч MR = max(1, effectiveBaseNoEnv + envAdd)
    /// - ���������ƻ��������������������롰ʵ�ʲ������Ĳ���ۼ�
    public static class MoveSimulator
    {
        public sealed class Result
        {
            public readonly List<Hex> ReachedPath = new();
            public float UsedSeconds;      // float ������־������۷���������
            public int RefundedSeconds;
            public bool Arrived;
        }

        /// <summary>
        /// ͳһ�¿ھ����˺��ټӣ���
        /// effMR(step) = baseR * noEnvMultNow * fromMult + flatAfterNow
        /// - noEnvMultNow = buffMult_now * stickyMult_now���������Σ�
        /// - fromMult: ��������ĵ��α���
        /// </summary>
        public static Result RunMultiThenFlat(
            IList<Hex> path,
            int baseR,
            float noEnvMultNow,          // buff * sticky���������Σ�
            float flatAfterNow,          // �߹�ƽ�ӣ��˷����ټӣ�
            int budgetSeconds,           // ����Ԥ��
            System.Func<Hex, float> getEnvMult, // ���α��ʣ�from��
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0 || baseR <= 0) return res;

            res.ReachedPath.Add(path[0]);
            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            int refunds = 0;

            // ���ƻ������ɱ�������������ʱ�Ļ���
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
                    if (debug) Debug.Log($"[MoveSim��MultiThenFlat] refund+1 thr={refundThresholdSeconds:F2} savedLeft={saved:F3} budget={budget:F3} effMR={effMR:F2}");
                }

                if (debug)
                    Debug.Log($"[MoveSim��Step] i={i} from={from} effMR={effMR:F2} cost={actualCost:F3} budgetLeft={budget:F3}");
            }

            res.UsedSeconds = budgetSeconds - budget;
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }


        /// v2 ��ʽ�棨�ӷ��ھ��������Ѿ�����
        public static Result RunAdditive(
            IList<Hex> path,
            int baseBaseMoveRate,                // ������ MR��ctx.BaseMoveRate
            int effectiveBaseNoEnv,              // ���� +�����ڻ����� buff/��Եȣ���� MR������������
            int budgetSeconds,                   // ��������Ԥ��
            System.Func<Hex, float> getEnvMult,  // �������ʣ�>0������ from ȡ
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            Debug.Log($"[MoveSim v2] additive=true asm={typeof(MoveSimulator).Assembly.FullName}");

            var res = new Result();
            if (path == null || path.Count == 0 || effectiveBaseNoEnv <= 0) return res;

            // ������룬��֤ Count>=1
            res.ReachedPath.Add(path[0]);

            float budget = Mathf.Max(0f, budgetSeconds);
            float saved = 0f;
            int refunds = 0;

            // ���ƻ������������������������ڷ����ۻ�
            float baseStepCost = 1f / Mathf.Max(0.01f, effectiveBaseNoEnv);

            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];

                float multFrom = Mathf.Clamp(getEnvMult != null ? getEnvMult(from) : 1f, 0.1f, 5f);

                // �ѱ���ת�ɡ����ڻ������١��ļӷ�����
                int envAdd = Mathf.FloorToInt(Mathf.Max(1, baseBaseMoveRate) * (multFrom - 1f));
                int effMR = Mathf.Max(1, effectiveBaseNoEnv + envAdd);      // ������Ч MR�����=1��
                float actualCost = 1f / Mathf.Max(0.01f, effMR);              // ����ʵ�ʺ�ʱ

                if (debug) Debug.Log($"[MoveSim+Add] step={i} from={from} mult={multFrom:F2} baseNoEnv={effectiveBaseNoEnv} envAdd={envAdd} effMR={effMR} cost={actualCost:F3} budget={budget:F3}");

                if (budget + 1e-6f < actualCost) break; // Ԥ�㲻��һ�� �� �ض�

                budget -= actualCost;
                res.ReachedPath.Add(to);

                // �����ۻ����ƻ�-ʵ�ʣ�
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

        /// ���ݰ�װ����ǩ�������� baseMoveRate ��Ϊ��������������Ч MR����
        /// ͬʱ�� floor(baseMoveRate) ���� baseBaseMoveRate��
        /// ���龡��ѵ��õ��е� RunAdditive(...) ��������ʵ baseBaseMoveRate��
        public static Result Run(
            IList<Hex> path,
            float baseMoveRate,                 // ��Ϊ��������������Ч MR��
            int budgetSeconds,
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            int effectiveNoEnv = Mathf.Max(1, Mathf.FloorToInt(baseMoveRate));
            int baseBaseMoveRate = effectiveNoEnv; // ���ƣ��Ƽ����õ��Ϊ�� ctx.BaseMoveRate
            if (debug) Debug.LogWarning("[MoveSim v2] Run(legacy) wrapper in use �� please migrate to RunAdditive(...) with ctx.BaseMoveRate.");
            return RunAdditive(path, baseBaseMoveRate, effectiveNoEnv, budgetSeconds, getEnvMult, refundThresholdSeconds, debug);
        }
    }
}
