using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction40 : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            skillId = "Reaction40";
            timeCostSeconds = 2;
            energyCost = 40;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 18;
        }
    }
}
