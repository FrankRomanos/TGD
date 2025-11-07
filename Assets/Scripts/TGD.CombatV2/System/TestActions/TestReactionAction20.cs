using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction20 : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            skillId = "Reaction20";
            timeCostSeconds = 1;
            energyCost = 20;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
