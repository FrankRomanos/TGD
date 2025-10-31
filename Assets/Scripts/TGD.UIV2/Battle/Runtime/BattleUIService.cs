using UnityEngine;
using TGD.AudioV2;
using TGD.CombatV2;
using TGD.HexBoard;

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
        public ActionHudMessageListenerTMP actionHudMessageListener;

        bool _subscriptionsActive;
        bool _turnManagerSubscribed;
        bool _combatManagerSubscribed;

        static T AutoFind<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        void Awake()
        {
            if (turnManager == null)
                turnManager = AutoFind<TurnManagerV2>();

            if (combatManager == null)
                combatManager = AutoFind<CombatActionManagerV2>();

            if (audioManager == null)
                audioManager = AutoFind<BattleAudioManager>();

            if (timeline == null)
                timeline = AutoFind<TurnTimelineController>();

        }

        void OnEnable()
        {
            if (turnManager == null)
                turnManager = AutoFind<TurnManagerV2>();

            if (combatManager == null)
                combatManager = AutoFind<CombatActionManagerV2>();

            if (audioManager == null)
                audioManager = AutoFind<BattleAudioManager>();

            if (timeline == null)
                timeline = AutoFind<TurnTimelineController>();

            if (timeline != null)
            {
                timeline.Initialize(turnManager, combatManager);
                timeline.ActiveUnitDeferred += OnUnitDeferred;
            }

            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();

            if (timeline != null)
            {
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.Deinitialize();
            }
        }

        void OnDestroy()
        {
            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
            Unsubscribe();
        }

        void Subscribe()
        {
            if (turnManager != null && !_turnManagerSubscribed)
            {
                turnManager.PhaseBegan += HandlePhaseBegan;
                turnManager.TurnStarted += HandleTurnStarted;
                turnManager.TurnEnded += HandleTurnEnded;
                turnManager.TurnOrderChanged += HandleTurnOrderChanged;
                _turnManagerSubscribed = true;
            }

            if (combatManager != null && !_combatManagerSubscribed)
            {
                combatManager.BonusTurnStateChanged += HandleBonusTurnStateChanged;
                _combatManagerSubscribed = true;
            }

            _subscriptionsActive = _turnManagerSubscribed || _combatManagerSubscribed;
        }

        void Unsubscribe()
        {
            if (!_subscriptionsActive)
                return;

            if (turnManager != null && _turnManagerSubscribed)
            {
                turnManager.PhaseBegan -= HandlePhaseBegan;
                turnManager.TurnStarted -= HandleTurnStarted;
                turnManager.TurnEnded -= HandleTurnEnded;
                turnManager.TurnOrderChanged -= HandleTurnOrderChanged;
                _turnManagerSubscribed = false;
            }

            if (combatManager != null && _combatManagerSubscribed)
            {
                combatManager.BonusTurnStateChanged -= HandleBonusTurnStateChanged;
                _combatManagerSubscribed = false;
            }

            _subscriptionsActive = false;
        }

        void HandlePhaseBegan(bool isPlayerPhase)
        {
            if (timeline != null)
                timeline.NotifyPhaseBeganExternal(isPlayerPhase);
        }

        void HandleTurnStarted(Unit unit)
        {
            if (timeline != null)
                timeline.NotifyTurnStartedExternal(unit);
        }

        void HandleTurnEnded(Unit unit)
        {
            if (timeline != null)
                timeline.NotifyTurnEndedExternal(unit);
        }

        void HandleTurnOrderChanged(bool isPlayerSide)
        {
            if (timeline != null)
                timeline.NotifyTurnOrderChangedExternal(isPlayerSide);
        }

        void OnUnitDeferred(TGD.HexBoard.Unit u)
        {
            if (audioManager != null)
                audioManager.PlayEvent(BattleAudioEvent.TurnTimelineInsert);
        }

        void HandleBonusTurnStateChanged()
        {
            if (timeline != null)
                timeline.NotifyBonusTurnStateChangedExternal();
        }
    }
}
