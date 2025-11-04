namespace TGD.CoreV2.Rules
{
    public interface IRuleModifier
    {
        int Priority { get; }
        bool Matches(in RuleContext ctx);
    }

    public interface ICostModifier : IRuleModifier
    {
        void ModifyCost(in RuleContext ctx, ref int secs, ref int energyMove, ref int energyAtk);
    }

    public interface ICooldownPolicy : IRuleModifier
    {
        void OnStartCooldown(in RuleContext ctx, ref int startSeconds);
        void OnTickCooldown(in RuleContext ctx, ref int tickDelta);
    }

    public interface IComboPolicy : IRuleModifier
    {
        void ModifyComboFactor(in RuleContext ctx, ref float factor);
    }

    public interface IRefundPolicy : IRuleModifier
    {
        void ModifyRefund(in RuleContext ctx, ref int secsRefund, ref int energyRefund);
    }
}
