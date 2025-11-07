using System.Linq;

namespace TGD.CoreV2.Rules
{
    [System.Flags]
    public enum KindMask
    {
        Standard = 1 << 0,
        Reaction = 1 << 1,
        Derived = 1 << 2,
        FullRound = 1 << 3,
        Sustained = 1 << 4,
        Free = 1 << 5,
        Any = ~0
    }

    [System.Serializable]
    public sealed class RuleFilter
    {
        public KindMask kinds = KindMask.Any;
        public string skillIdEquals;
        public string skillIdStartsWith;
        public string[] requireTags;
        public bool onlyFriendly;
        public bool onlyEnemy;

        public bool Matches(in RuleContext ctx)
        {
            if ((kinds & MaskOf(ctx.kind)) == 0) return false;

            if (!string.IsNullOrEmpty(skillIdEquals) && skillIdEquals != ctx.skillId) return false;
            if (!string.IsNullOrEmpty(skillIdStartsWith) && (ctx.skillId == null || !ctx.skillId.StartsWith(skillIdStartsWith))) return false;

            if (onlyFriendly && ctx.faction != "Friendly") return false;
            if (onlyEnemy && ctx.faction != "Enemy") return false;

            if (requireTags != null && requireTags.Length > 0)
            {
                var t = ctx.tags;
                if (t == null || !requireTags.All(req => t.Contains(req))) return false;
            }
            return true;
        }

        static KindMask MaskOf(RulesActionKind kind) => kind switch
        {
            RulesActionKind.Standard => KindMask.Standard,
            RulesActionKind.Reaction => KindMask.Reaction,
            RulesActionKind.Derived => KindMask.Derived,
            RulesActionKind.FullRound => KindMask.FullRound,
            RulesActionKind.Sustained => KindMask.Sustained,
            RulesActionKind.Free => KindMask.Free,
            _ => KindMask.Any
        };
    }
}
