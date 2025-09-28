using System;
using TGD.Data;

namespace TGD.Combat
{
    public static class ActionSystem
    {
        public static void Execute(Unit caster, SkillDefinition skill, RuntimeCtx runtime, EffectContext effectContext = null)
        {
            if (skill == null)
                throw new ArgumentNullException(nameof(skill));
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            runtime.Caster = caster;
            runtime.Skill = skill;

            var context = effectContext ?? new EffectContext(caster, skill);
            context.SkillResolver = runtime.SkillResolver;
            context.PrimaryTarget = runtime.PrimaryTarget;
            context.SecondaryTarget = runtime.SecondaryTarget;
            context.Grid = runtime.Grid;

            context.Allies.Clear();
            if (runtime.Allies != null)
            {
                foreach (var ally in runtime.Allies)
                    if (ally != null)
                        context.Allies.Add(ally);
            }

            context.Enemies.Clear();
            if (runtime.Enemies != null)
            {
                foreach (var enemy in runtime.Enemies)
                    if (enemy != null)
                        context.Enemies.Add(enemy);
            }

            var preview = EffectInterpreter.InterpretSkill(context);
            var ops = EffectResolver.Resolve(preview, context);
            EffectOpRunner.Run(ops, runtime);
            runtime.Logger?.Log("ACTION_COMMIT", skill.skillID);
        }
    }
}
