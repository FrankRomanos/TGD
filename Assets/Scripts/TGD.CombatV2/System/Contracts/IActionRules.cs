using System.Collections.Generic;

namespace TGD.CombatV2
{
    /// <summary>
    /// 提供 CAM 查询的统一回合规则接口。
    /// </summary>
    public interface IActionRules
    {
        /// <summary>
        /// 指定动作类型是否允许在 Idle 阶段裸用。
        /// </summary>
        bool CanActivateAtIdle(ActionKind kind);

        /// <summary>
        /// PhaseStartFree（Begin 时点）允许的首层动作集合。
        /// </summary>
        IReadOnlyList<ActionKind> AllowedAtPhaseStartFree();

        /// <summary>
        /// 基础动作确认后，首层连锁允许的动作集合。
        /// </summary>
        IReadOnlyList<ActionKind> AllowedChainFirstLayer(ActionKind baseKind, bool isEnemyPhase);

        /// <summary>
        /// 递归连锁：上一层选择的动作类型允许继续连哪些动作。
        /// </summary>
        IReadOnlyList<ActionKind> AllowedChainNextLayer(ActionKind chosenKind);

        /// <summary>
        /// 反应动作是否必须满足“耗时 ≤ 基础动作耗时”的硬约束。
        /// </summary>
        bool ReactionMustBeWithinBaseTime();

        /// <summary>
        /// 友军是否允许跨回合插入自己的动作。
        /// </summary>
        bool AllowFriendlyInsertions();

        /// <summary>
        /// 指定基础动作在 W4.5 可派生的动作 ID 列表。
        /// </summary>
        IReadOnlyList<string> AllowedDerivedActions(string baseSkillId);
    }
}
