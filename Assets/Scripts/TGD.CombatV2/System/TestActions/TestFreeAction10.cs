using UnityEngine;
using TGD.CombatV2.Targeting;

namespace TGD.CombatV2
{
    public sealed class TestFreeAction10 : ChainActionBase
    {
        public override ActionKind Kind => ActionKind.Free;

        void Reset()
        {
            skillId = "Free10";
            timeCostSeconds = 0;
            energyCost = 10;
            targetMode = TargetMode.AnyClick;
            cooldownSeconds = 6;
        }
    }
}
