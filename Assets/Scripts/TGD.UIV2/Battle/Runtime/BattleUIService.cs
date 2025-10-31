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
            if (turnManager == null) turnManager = AutoFind<TurnManagerV2>();
            if (combatManager == null) combatManager = AutoFind<CombatActionManagerV2>();
            if (audioManager == null) audioManager = AutoFind<BattleAudioManager>();
            if (timeline == null) timeline = AutoFind<TurnTimelineController>();
            if (chainPopup == null) chainPopup = AutoFind<ChainPopupPresenter>();
            if (turnHud == null) turnHud = AutoFind<TurnHudController>();

            if (timeline != null)
            {
                timeline.Initialize(turnManager, combatManager);
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.ActiveUnitDeferred += OnUnitDeferred;
                timeline.ForceRebuildNow();
            }

            if (turnHud != null)
            {
                turnHud.Initialize(turnManager, combatManager);
            }

            if (chainPopup != null)
            {
                chainPopup.Initialize(turnManager, combatManager);

                bool stillWaiting = combatManager != null && combatManager.IsAwaitingChainDecision;
                chainPopup.RestoreFromSnapshotIfNeeded(stillWaiting);
                chainPopup.RefreshNow();

                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
                chainPopup.ChainPopupOpened += HandleChainPopupOpened;
            }

            Subscribe();

            DispatchInitialState();
        }



        void OnDisable()
        {
            // 1. 退订全局事件
            Unsubscribe();

            // 2. 拆掉 service->view 的事件线
            if (timeline != null)
            {
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.Shutdown(); // ★ 用统一 Shutdown
            }

            if (chainPopup != null)
            {
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
                chainPopup.Shutdown(); // ★ 用统一 Shutdown
            }

            if (turnHud != null)
            {
                turnHud.Shutdown(); // 已经有
            }
        }


        void OnDestroy()
        {
            // 理论上 OnDisable 已经清了所有事件。
            // 这里只是再防御一次，防止 Unity 某些极端销毁顺序下 OnDisable 没被调用。
            Unsubscribe();

            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;

            if (chainPopup != null)
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
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
