using UnityEngine;

namespace TGD.CombatV2
{
    public sealed class TestReactionAction40 : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Reaction;

        void Reset()
        {
            actionId = "Reaction40";
            timeCostSeconds = 2;
            energyCost = 40;
        }
    }
}
