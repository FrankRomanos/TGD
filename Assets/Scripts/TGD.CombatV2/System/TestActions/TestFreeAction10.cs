using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestFreeAction10 : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Free;

        void Reset()
        {
            actionId = "Free10";
            timeCostSeconds = 0;
            energyCost = 10;
            targetMode = TargetMode.AnyClick;
            cooldownSeconds = 6;
        }
    }
}
