namespace TGD.CoreV2.Rules
{
    public abstract class RuleModifierBase : IRuleModifier
    {
        public int Priority { get; protected set; } = 0;
        public RuleFilter filter = new();

        public virtual bool Matches(in RuleContext ctx)
        {
            return filter == null || filter.Matches(ctx);
        }
    }
}
