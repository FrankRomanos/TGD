using UnityEngine;

namespace TGD.CoreV2
{
    /// �ƻ�����/��ʡ�ۼ�/���Ρ�ѧϰһ�Ρ���ͨ��С���ƣ������� Hex��
    public sealed class MoveRateMechanicsV2
    {
        public float refundThresholdSeconds = 0.8f;   // �ۼ� �� ��ֵ => �� 1 ��
        public bool permanentTerrainLearning = true;  // �������Ρ���������ѧϰ 1 ��

        int _plannedBase = 1;
        float _savedAccum = 0f;
        int _refunds = 0;

        bool _learnedHaste = false;
        bool _learnedSlow = false;

        public void BeginAction(int plannedBaseMoveRate)
        {
            _plannedBase = Mathf.Max(1, plannedBaseMoveRate);
            _savedAccum = 0f;
            _refunds = 0;
            // ������ _learnedX������ѧϰΪ������/�ؿ��ڡ�һ�Σ�����ÿ�����ã�����ʽ���� ResetLearningFlags()
        }

        /// envAdd����ԡ��ƻ����١��ļӷ�������/�����Ѿ��ϲ���ļ��ܣ���
        /// isFromTerrain���Ƿ����ڡ����Ρ���Դ��ֻ�����Ŵ�����ѧϰһ�Ρ���
        public StepResult OnEnterCell(int envAdd, bool isFromTerrain)
        {
            int effective = Mathf.Max(1, _plannedBase + envAdd);

            // ���� ��ʡ�ۼƣ��ȼƻ�������н�ʡ������
            if (effective > _plannedBase)
            {
                float save = (1f / _plannedBase) - (1f / (float)effective);
                if (save > 0f)
                {
                    _savedAccum += save;
                    while (_savedAccum >= 1f)
                    {
                        _savedAccum -= 1f;
                        _refunds += 1;
                    }
                }
            }

            // ���� ����ѧϰ�������Σ�������һ�Σ�����
            bool shouldLearn = false;
            int learnDelta = 0;
            if (permanentTerrainLearning && isFromTerrain && envAdd != 0)
            {
                if (envAdd > 0 && !_learnedHaste) { shouldLearn = true; learnDelta = envAdd; _learnedHaste = true; }
                else if (envAdd < 0 && !_learnedSlow) { shouldLearn = true; learnDelta = envAdd; _learnedSlow = true; }
            }

            return new StepResult(effective, shouldLearn, learnDelta);
        }

        public int EndAction() => _refunds;

        public void ResetLearningFlags()
        {
            _learnedHaste = false;
            _learnedSlow = false;
        }

        public readonly struct StepResult
        {
            public readonly int EffectiveMoveRate;
            public readonly bool ShouldLearnPermanently;
            public readonly int LearnDelta; // (+/-) д�� BaseMoveRate ������

            public StepResult(int effectiveMoveRate, bool shouldLearnPermanently, int learnDelta)
            {
                EffectiveMoveRate = effectiveMoveRate;
                ShouldLearnPermanently = shouldLearnPermanently;
                LearnDelta = learnDelta;
            }
        }
    }
}
