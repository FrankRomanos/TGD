using System.Collections.Generic;
using TGD.Data;

namespace TGD.Combat
{
    public sealed class RuntimeCtx
    {
        public Unit Caster;
        public Unit PrimaryTarget;
        public IReadOnlyList<Unit> Allies;
        public IReadOnlyList<Unit> Enemies;

        public ICombatEventBus EventBus { get; set; }
        public ICombatLogger Logger { get; set; }
        public ICombatTime Time { get; set; }
        public ISkillResolver SkillResolver { get; set; }

        public IDamageSystem DamageSystem { get; set; }
        public IStatusSystem StatusSystem { get; set; }
        public ICooldownSystem CooldownSystem { get; set; }
        public ISkillModSystem SkillModSystem { get; set; }
        public IMovementSystem MovementSystem { get; set; }
        public IAuraSystem AuraSystem { get; set; }
        public IScheduler Scheduler { get; set; }
        public IResourceSystem ResourceSystem { get; set; }
    }
}