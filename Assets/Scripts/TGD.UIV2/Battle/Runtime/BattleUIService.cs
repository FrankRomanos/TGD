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

            if (chainPopup == null)
                chainPopup = AutoFind<ChainPopupPresenter>();

            if (turnHud == null)
                turnHud = AutoFind<TurnHudController>();

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

            if (chainPopup == null)
                chainPopup = AutoFind<ChainPopupPresenter>();

            if (turnHud == null)
                turnHud = AutoFind<TurnHudController>();

            if (timeline != null)
            {
                timeline.Initialize(turnManager, combatManager);
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.ActiveUnitDeferred += OnUnitDeferred;
            }

            if (turnHud != null)
                turnHud.Initialize(turnManager, combatManager);

            if (chainPopup != null)
            {
                chainPopup.Initialize(turnManager, combatManager);
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
                chainPopup.ChainPopupOpened += HandleChainPopupOpened;
            }

            Subscribe();
            DispatchInitialState();
        }

        void OnDisable()
        {
            Unsubscribe();

            if (timeline != null)
            {
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.Deinitialize();
            }

            if (chainPopup != null)
            {
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
                chainPopup.Deinitialize();
            }

            if (turnHud != null)
                turnHud.Deinitialize();
        }

        void OnDestroy()
        {
            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
            if (chainPopup != null)
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
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
                turnManager.UnitRuntimeChanged += HandleUnitRuntimeChanged;
                _turnManagerSubscribed = true;
            }

            if (combatManager != null && !_combatManagerSubscribed)
            {
                combatManager.BonusTurnStateChanged += HandleBonusTurnStateChanged;
                combatManager.ChainFocusChanged += HandleChainFocusChanged;
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
                turnManager.UnitRuntimeChanged -= HandleUnitRuntimeChanged;
                _turnManagerSubscribed = false;
            }

            if (combatManager != null && _combatManagerSubscribed)
            {
                combatManager.BonusTurnStateChanged -= HandleBonusTurnStateChanged;
                combatManager.ChainFocusChanged -= HandleChainFocusChanged;
                _combatManagerSubscribed = false;
            }

            _subscriptionsActive = false;
        }

        void DispatchInitialState()
        {
            if (turnHud == null)
                return;

            if (turnManager != null)
            {
                turnHud.HandlePhaseBegan(turnManager.IsPlayerPhase);
                var activeUnit = turnManager.ActiveUnit;
                if (activeUnit != null)
                {
                    turnHud.HandleTurnStarted(activeUnit);
                    turnHud.HandleUnitRuntimeChanged(activeUnit);
                }
            }

            if (combatManager != null)
            {
                turnHud.HandleChainFocusChanged(combatManager.CurrentChainFocus);
                turnHud.HandleBonusTurnStateChanged();
            }
        }

        void HandlePhaseBegan(bool isPlayerPhase)
        {
            if (timeline != null)
                timeline.NotifyPhaseBeganExternal(isPlayerPhase);

            if (turnHud != null)
                turnHud.HandlePhaseBegan(isPlayerPhase);
        }

        void HandleTurnStarted(Unit unit)
        {
            if (timeline != null)
                timeline.NotifyTurnStartedExternal(unit);

            if (turnHud != null)
                turnHud.HandleTurnStarted(unit);
        }

        void HandleTurnEnded(Unit unit)
        {
            if (timeline != null)
                timeline.NotifyTurnEndedExternal(unit);

            if (turnHud != null)
                turnHud.HandleTurnEnded(unit);
        }

        void HandleTurnOrderChanged(bool isPlayerSide)
        {
            if (timeline != null)
                timeline.NotifyTurnOrderChangedExternal(isPlayerSide);
        }

        void HandleUnitRuntimeChanged(Unit unit)
        {
            if (turnHud != null)
                turnHud.HandleUnitRuntimeChanged(unit);
        }

        void HandleChainFocusChanged(Unit unit)
        {
            if (turnHud != null)
                turnHud.HandleChainFocusChanged(unit);
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

            if (turnHud != null)
                turnHud.HandleBonusTurnStateChanged();
        }

        void HandleChainPopupOpened()
        {
            if (audioManager != null)
                audioManager.PlayEvent(BattleAudioEvent.ChainPopupOpen);
        }
    }
}
