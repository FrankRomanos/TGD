using UnityEngine;

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
        }
    }
}
