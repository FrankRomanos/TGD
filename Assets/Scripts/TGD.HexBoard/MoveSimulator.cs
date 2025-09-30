// File: TGD.HexBoard/MoveSimulator.cs
using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    /// ִ�������ģ�⣺���������ʽ���Ԥ��/�������ɱ� CombatV2 �ȸ���
    public static class MoveSimulator
    {
        public sealed class Result
        {
            // �����ɶ����ⲿ��Ҫ��·�����ڲ��� Run �����
            public readonly List<Hex> ReachedPath = new();
            public float UsedSeconds;      // �� Run ��д���ⲿֻ��
            public int RefundedSeconds;    // �� Run ��д���ⲿֻ��
            public bool Arrived;           // ��������·����
        }

        /// ���ģ�⣻stepCost = 1 / (baseMoveRate * envMult)
        public static Result Run(
            IList<Hex> path,
            float baseMoveRate,           // ��/��
            int budgetSeconds,            // ����
            System.Func<Hex, float> getEnvMult,
            float refundThresholdSeconds = 0.8f,
            bool debug = false)
        {
            var res = new Result();
            if (path == null || path.Count == 0 || baseMoveRate <= 0f )
                return res;
            // �� ����Ԥ����٣��Ȱ����Ž�ȥ����֤ Count>=1
            res.ReachedPath.Add(path[0]);


            float budget = Mathf.Max(0f, budgetSeconds); // �� ����Ϊ 0
            float saved = 0f;
            int refunds = 0;
            float baseStepCost = 1f / Mathf.Max(0.01f, baseMoveRate);

            for (int i = 1; i < path.Count; i++)
            {
              
                var from = path[i - 1];
                var to = path[i];
                // �� ��ִ����һ�£��� from �Ļ������ʽ���ò��ɱ�
                float multFrom = Mathf.Clamp(getEnvMult != null ? getEnvMult(from) : 1f, 0.1f, 5f);
                float actualCost = 1f / Mathf.Max(0.01f, baseMoveRate * multFrom);

                if (budget + 1e-6f < actualCost) break; // Ԥ�㲻��һ��

                budget -= actualCost;
                res.ReachedPath.Add(to);

                float stepSaved = Mathf.Max(0f, baseStepCost - actualCost);
                saved += stepSaved;
                while (saved >= refundThresholdSeconds)
                {
                    saved -= refundThresholdSeconds;
                    refunds += 1;
                    budget += 1f;
                    if (debug) Debug.Log($"[MoveSim] refund+1 (threshold={refundThresholdSeconds:F2}) �� refunds={refunds} savedLeft={saved:F3} budget={budget:F3}");
                }
            }

            res.UsedSeconds = budgetSeconds - budget;
            res.RefundedSeconds = refunds;
            res.Arrived = (res.ReachedPath.Count == path.Count);
            return res;
        }
    }
}
