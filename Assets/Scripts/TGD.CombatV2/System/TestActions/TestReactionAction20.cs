using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction20 : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            actionId = "Reaction20";
            timeCostSeconds = 1;
            energyCost = 20;
            targetMode = TargetMode.EnemyOrGround;
            cooldownSeconds = 12;
        }
    }
}
