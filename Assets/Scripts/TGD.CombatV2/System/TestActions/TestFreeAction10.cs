using UnityEngine;
using TGD.CoreV2;
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
            targetRule = TargetRule.AnyClick;
            cooldownSeconds = 6;
        }
    }
}
