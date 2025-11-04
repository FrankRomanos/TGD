namespace TGD.CoreV2.Rules
{
    public sealed class RuleEngineV2
    {
        public static RuleEngineV2 Instance { get; } = new RuleEngineV2();

        public void ApplyCostModifiers(UnitRuleSet set, in RuleContext ctx, ref int moveSecs, ref int atkSecs, ref int moveEnergy, ref int atkEnergy)
        {
            if (set == null) return;
            foreach (var m in set.Enumerate<ICostModifier>())
                if (m.Matches(ctx)) m.ModifyCost(ctx, ref moveSecs, ref atkSecs, ref moveEnergy, ref atkEnergy);
        }

        public void OnStartCooldown(UnitRuleSet set, in RuleContext ctx, ref int startSeconds)
        {
            if (set == null) return;
            foreach (var m in set.Enumerate<ICooldownPolicy>())
                if (m.Matches(ctx)) m.OnStartCooldown(ctx, ref startSeconds);
        }

        public void OnTickCooldown(UnitRuleSet set, in RuleContext ctx, ref int tickDelta)
        {
            if (set == null) return;
            foreach (var m in set.Enumerate<ICooldownPolicy>())
                if (m.Matches(ctx)) m.OnTickCooldown(ctx, ref tickDelta);
        }

        public void ModifyComboFactor(UnitRuleSet set, in RuleContext ctx, ref float factor)
        {
            if (set == null) return;
            foreach (var m in set.Enumerate<IComboPolicy>())
                if (m.Matches(ctx)) m.ModifyComboFactor(ctx, ref factor);
        }

        public void ModifyRefund(UnitRuleSet set, in RuleContext ctx, ref int secsRefund, ref int energyRefund)
        {
            if (set == null) return;
            foreach (var m in set.Enumerate<IRefundPolicy>())
                if (m.Matches(ctx)) m.ModifyRefund(ctx, ref secsRefund, ref energyRefund);
        }
    }
}
