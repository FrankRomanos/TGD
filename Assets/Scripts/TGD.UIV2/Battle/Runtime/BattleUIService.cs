using UnityEngine;
using TGD.CombatV2;
using TGD.AudioV2;
using TGD.UIV2;

namespace TGD.UIV2.Battle
{
    public sealed class BattleUIService : MonoBehaviour
    {
        [Header("Sources")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 combatManager;
        public BattleAudioManager audioManager;

        [Header("Views")]
        public TurnTimelineController timeline;
        public ChainPopupPresenter chainPopup;
        public TurnHudController turnHud;

        private void Awake()
        {
            // 目前什么都不做，先只是把引用拖上 Inspector。
        }
    }
}
