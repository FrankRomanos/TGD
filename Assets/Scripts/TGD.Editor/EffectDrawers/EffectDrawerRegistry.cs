using System;
using System.Collections.Generic;
using TGD.Data;

namespace TGD.Editor
{
    public static class EffectDrawerRegistry
    {
        private static readonly Dictionary<EffectType, IEffectDrawer> _map = new();

        static EffectDrawerRegistry()
        {
            // ������ע������ Drawer
            Register(EffectType.GainResource, new GainResourceDrawer());
            Register(EffectType.ModifySkill, new ModifySkillDrawer());
            Register(EffectType.ApplyStatus, new ApplyStatusDrawer());
            Register(EffectType.ScalingBuff, new ScalingBuffDrawer());
            Register(EffectType.ModifyAction, new ModifyActionDrawer());
            Register(EffectType.CooldownModifier, new CooldownModifierDrawer());
            Register(EffectType.ReplaceSkill, new ReplaceSkillDrawer());
            Register(EffectType.ConditionalEffect, new ConditionalEffectDrawer());
            // ����
            Register(EffectType.None, new DefaultEffectDrawer());
            // ͬʱ��Ϊ Default
            Register((EffectType)(-1), new DefaultEffectDrawer());
            // ֱ�ӽ�ע��ŵ� EffectDrawerRegistry ��

            Register(EffectType.AttributeModifier, new AttributeModifierDrawer());
            Register(EffectType.Damage, new DamageDrawer());
            Register(EffectType.Heal, new HealDrawer());
            Register(EffectType.Move, new MoveDrawer());
            Register(EffectType.MasteryPosture, new MasteryPostureDrawer());
            Register(EffectType.RandomOutcome, new RandomOutcomeDrawer());
            Register(EffectType.Repeat, new RepeatEffectDrawer());
            Register(EffectType.ProbabilityModifier, new ProbabilityModifierDrawer());
            Register(EffectType.DotHotModifier, new DotHotModifierDrawer());
        }

        public static void Register(EffectType type, IEffectDrawer drawer)
        {
            _map[type] = drawer;
        }

        public static IEffectDrawer Get(EffectType type)
        {
            if (_map.TryGetValue(type, out var d)) return d;
            return _map[(EffectType)(-1)];
        }
    }
}

