using System.Collections.Generic;

namespace TGD.CoreV2.Rules
{
    public readonly struct RuleContext
    {
        public readonly string unitKey;     // 可为临时字符串（见适配器）
        public readonly string faction;     // "Friendly"/"Enemy"（可为空）
        public readonly string skillId;    // 具体动作ID（如 MOVE / ATK / SK_XXX）
        public readonly ActionKind kind;
        public readonly int chainDepth;
        public readonly int comboIndex;
        public readonly int planSecs;
        public readonly int planEnergy;
        public readonly StatsV2 stats;      // 已在 Core
        public readonly IReadOnlyList<string> tags; // 可为 null

        public RuleContext(string unitKey, string faction, string skillId, ActionKind kind,
                           int chainDepth, int comboIndex, int planSecs, int planEnergy,
                           StatsV2 stats, IReadOnlyList<string> tags)
        {
            this.unitKey = unitKey;
            this.faction = faction;
            this.skillId = skillId;
            this.kind = kind;
            this.chainDepth = chainDepth;
            this.comboIndex = comboIndex;
            this.planSecs = planSecs;
            this.planEnergy = planEnergy;
            this.stats = stats;
            this.tags = tags;
        }
    }
}
