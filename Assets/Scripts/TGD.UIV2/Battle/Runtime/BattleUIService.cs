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
            CacheChildViews();
            EnsureManagers();
        }

        void OnEnable()
        {
            CacheChildViews();
            EnsureManagers();

            // --- 初始化每个UI控制器并把 manager 注入
            if (timeline != null)
            {
                timeline.Initialize(turnManager, combatManager);
                // UI -> Service 的回调：先防止重复，再订阅
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.ActiveUnitDeferred += OnUnitDeferred;
            }

            if (turnHud != null)
            {
                turnHud.Initialize(turnManager, combatManager);
            }

            if (chainPopup != null)
            {
                chainPopup.Initialize(turnManager, combatManager);
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;
                chainPopup.ChainPopupOpened += HandleChainPopupOpened;
                chainPopup.HideImmediate();
            }

            // --- 游戏层事件 -> UI
            Subscribe();

            // --- 第一次把当前战斗状态推给UI（血条/沙漏/当前激活角色等）
            DispatchInitialState();
        }


        void OnDisable()
        {
            // 1. 取消 gameplay -> UI 的订阅
            Unsubscribe();

            // 2. 取消 UI -> Service 的回调订阅
            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;

            if (chainPopup != null)
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;

            // 3. 让每个UI控制器把自己复位并断开对manager的引用
            if (timeline != null)
                timeline.Shutdown();

            if (chainPopup != null)
                chainPopup.Shutdown();

            if (turnHud != null)
                turnHud.Shutdown();
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
            if (turnManager != null)
            {
                if (turnHud != null)
                {
                    turnHud.HandlePhaseBegan(turnManager.IsPlayerPhase);
                    var activeUnit = turnManager.ActiveUnit;
                    if (activeUnit != null)
                    {
                        turnHud.HandleTurnStarted(activeUnit);
                        turnHud.HandleUnitRuntimeChanged(activeUnit);
                    }
                }

                if (timeline != null)
                {
                    timeline.NotifyPhaseBeganExternal(turnManager.IsPlayerPhase);
                    var activeUnit = turnManager.ActiveUnit;
                    if (activeUnit != null)
                        timeline.NotifyTurnStartedExternal(activeUnit);
                    timeline.NotifyTurnOrderChangedExternal(true);
                }
            }

            if (combatManager != null)
            {
                if (turnHud != null)
                {
                    turnHud.HandleChainFocusChanged(combatManager.CurrentChainFocus);
                    turnHud.HandleBonusTurnStateChanged();
                }

                if (timeline != null && combatManager.IsBonusTurnActive)
                    timeline.NotifyBonusTurnStateChangedExternal();
            }
        }

        void CacheChildViews()
        {
            if (timeline == null)
                timeline = GetComponentInChildren<TurnTimelineController>(true);
            if (chainPopup == null)
                chainPopup = GetComponentInChildren<ChainPopupPresenter>(true);
            if (turnHud == null)
                turnHud = GetComponentInChildren<TurnHudController>(true);
            if (actionHudMessageListener == null)
                actionHudMessageListener = GetComponentInChildren<ActionHudMessageListenerTMP>(true);
        }

        void EnsureManagers()
        {
            if (turnManager == null)
                turnManager = AutoFind<TurnManagerV2>();
            if (combatManager == null)
                combatManager = AutoFind<CombatActionManagerV2>();
            if (audioManager == null)
                audioManager = AutoFind<BattleAudioManager>();
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
