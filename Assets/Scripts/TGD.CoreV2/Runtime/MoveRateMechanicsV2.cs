using UnityEngine;

namespace TGD.CoreV2
{
    /// 计划移速/节省累计/地形“学习一次”的通用小机制（不依赖 Hex）
    public sealed class MoveRateMechanicsV2
    {
        public float refundThresholdSeconds = 0.8f;   // 累计 ≥ 阈值 => 返 1 秒
        public bool permanentTerrainLearning = true;  // 仅“地形”可正负各学习 1 次

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
            // 不重置 _learnedX：地形学习为“生涯/关卡期”一次；若想每次重置，可显式调用 ResetLearningFlags()
        }

        /// envAdd：相对“计划移速”的加法（地形/技能已经合并后的加总）。
        /// isFromTerrain：是否属于“地形”来源（只有它才触发“学习一次”）
        public StepResult OnEnterCell(int envAdd, bool isFromTerrain)
        {
            int effective = Mathf.Max(1, _plannedBase + envAdd);

            // —— 节省累计（比计划更快才有节省）——
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

            // —— 永久学习（仅地形；正负各一次）——
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
            public readonly int LearnDelta; // (+/-) 写回 BaseMoveRate 的增量

            public StepResult(int effectiveMoveRate, bool shouldLearnPermanently, int learnDelta)
            {
                EffectiveMoveRate = effectiveMoveRate;
                ShouldLearnPermanently = shouldLearnPermanently;
                LearnDelta = learnDelta;
            }
        }
    }
}
