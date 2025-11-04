using System.Collections.Generic;
using TGD.CoreV2;
using TGD.CoreV2.Rules;
using UnityEngine;                 // 为了 uctx.GetInstanceID()

namespace TGD.CombatV2
{
    public static class RulesAdapter
    {
        public static RulesActionKind ToRulesKind(this ActionKind k) => k switch
        {
            ActionKind.Standard => RulesActionKind.Standard,
            ActionKind.Reaction => RulesActionKind.Reaction,
            ActionKind.Derived => RulesActionKind.Derived,
            ActionKind.FullRound => RulesActionKind.FullRound,
            ActionKind.Sustained => RulesActionKind.Sustained,
            ActionKind.Free => RulesActionKind.Free,
            _ => RulesActionKind.Standard
        };

        /// <summary>
        /// 统一在 Combat 侧把运行时信息打包成 RuleContext。
        /// - unitIdHint: 可传蓝图里的 unitId（如果你此时拿得到）；传 null 也可以。
        /// - isFriendlyHint: 若你这步能判断阵营就传 true/false，传 null 则 faction 留空（不影响大多数规则）。
        /// </summary>
        public static RuleContext BuildContext(
            UnitRuntimeContext uctx,
            string actionId,
            ActionKind kind,
            int chainDepth,
            int comboIndex,
            int planSecs,
            int planEnergy,
            string unitIdHint = null,
            bool? isFriendlyHint = null
        )
        {
            // unitKey 优先使用蓝图/外部提供的稳定ID；否则回退到 uctx 的 InstanceID（稳定到会话期，足够做日志 & 过滤）
            string unitKey = !string.IsNullOrEmpty(unitIdHint)
                ? unitIdHint
                : (uctx != null ? uctx.GetInstanceID().ToString() : "unit");

            string faction = isFriendlyHint.HasValue
                ? (isFriendlyHint.Value ? "Friendly" : "Enemy")
                : null; // 允许为空；只有使用 onlyFriendly/onlyEnemy 的规则才需要它

            var stats = uctx?.stats;
            IReadOnlyList<string> tags = null; // 你以后给 UCTX 加 Tags 再填

            return new RuleContext(
                unitKey, faction, actionId, kind.ToRulesKind(),
                chainDepth, comboIndex, planSecs, planEnergy,
                stats, tags
            );
        }
    }
}
