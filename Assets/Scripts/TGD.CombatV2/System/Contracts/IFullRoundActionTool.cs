using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public struct FullRoundQueuedPlan
    {
        public bool valid;
        public Hex target;
        public int plannedSeconds;
        public int plannedMoveEnergy;
        public int plannedAttackEnergy;
        public int budgetBefore;
        public int energyBefore;
        public int budgetAfter;
        public int energyAfter;

        public int TotalEnergy => Mathf.Max(0, plannedMoveEnergy + plannedAttackEnergy);
        public int NetSeconds => Mathf.Max(0, plannedSeconds);
    }

    /// <summary>
    /// 承载整轮动作的工具需要实现的接口，提供额外的整轮流程钩子。
    /// </summary>
    public interface IFullRoundActionTool
    {
        /// <summary>
        /// 整轮动作需要等待的轮数（Round），最小值为 1。
        /// </summary>
        int FullRoundRounds { get; }

        /// <summary>
        /// CombatActionManager 在进入执行前，将“本次预扣的秒数”传递给工具。
        /// </summary>
        void PrepareFullRoundSeconds(int seconds);

        /// <summary>
        /// 在 W2 阶段链式动作完成后、进入 W3 之前触发的即时效果（日志/动画等）。
        /// </summary>
        void TriggerFullRoundImmediate(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan);

        /// <summary>
        /// 当计时轮数耗尽、在指定回合开始阶段触发的最终效果。
        /// </summary>
        void TriggerFullRoundResolution(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan);
    }
}
