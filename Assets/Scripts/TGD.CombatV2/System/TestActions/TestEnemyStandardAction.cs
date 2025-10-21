using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestEnemyStandardAction : ChainTestActionBase
    {
        public override ActionKind Kind => ActionKind.Standard;

        protected override void Awake()
        {
            base.Awake();
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            Debug.Log($"[EnemyTest] StandardAction target={hex}", this);
            return base.OnConfirm(hex);
        }
    }
}
