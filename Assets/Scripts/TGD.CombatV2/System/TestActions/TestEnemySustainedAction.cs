using System.Collections;
using UnityEngine;
using TGD.CombatV2.Targeting;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestEnemySustainedAction : ChainTestActionBase
    {
        [SerializeField]
        string resolveLog = "Test Sustained";

        public override ActionKind Kind => ActionKind.Sustained;

        void Reset()
        {
            actionId = "EnemyTestSustained";
            timeCostSeconds = 4;
            energyCost = 0;
            targetMode = TargetMode.AnyClick;
            cooldownSeconds = 0;
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            yield return base.OnConfirm(hex);
            if (!string.IsNullOrEmpty(resolveLog))
                Debug.Log(resolveLog);
        }
    }
}
