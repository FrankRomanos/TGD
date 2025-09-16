using System.Collections.Generic;
using TGD.Data;

namespace TGD.Combat
{
    /// <summary>
    /// Reaction 双阶段：
    /// 1) 收集 Reaction + Free
    /// 2) 二次只显示 Free 的连锁
    /// 结算顺序：先 Free（按选中顺序），再 Reaction（逆序），最后原动作
    /// 这里是占位骨架。
    /// </summary>
    public static class ReactionSystem
    {
        public static void CollectAndResolve(Unit actor, SkillDefinition skill)
        {
            // TODO: 收集窗口（来自UI选择）
            // var pickedReactions = CollectReactions(...);
            // var pickedFrees     = CollectFrees(...);

            // TODO: 结算顺序（Free → Reaction逆序 → 原动作）
            // 暂时留空，由 ActionSystem.Apply 去做原动作效果
        }

        // private static List<SkillDefinition> CollectReactions(Unit ...){...}
        // private static List<SkillDefinition> CollectFrees(Unit ...){...}
    }
}
