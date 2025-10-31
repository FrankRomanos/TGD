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

        bool _didStart;
        bool _subscriptionsActive;
        bool _turnManagerSubscribed;
        bool _combatManagerSubscribed;

        static T AutoFind<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return FindObjectOfType<T>();
#endif
        }

        void Awake()
        {
            AutoAssignSources();
            AutoAssignViews();
            InitializeViews();
        }

        void Start()
        {
            _didStart = true;
            Subscribe();
        }

        void OnEnable()
        {
            if (_didStart)
                Subscribe();
        }

        void Update()
        {
            if (!_didStart)
                return;

            if (!_subscriptionsActive && (turnManager != null || combatManager != null))
                Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void AutoAssignSources()
        {
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
            if (!combatManager)
                combatManager = AutoFind<CombatActionManagerV2>();
            if (!audioManager)
                audioManager = AutoFind<BattleAudioManager>();
        }

        void AutoAssignViews()
        {
            if (!timeline)
                timeline = GetComponentInChildren<TurnTimelineController>(true);
            if (!chainPopup)
                chainPopup = GetComponentInChildren<ChainPopupPresenter>(true);
            if (!turnHud)
                turnHud = GetComponentInChildren<TurnHudController>(true);
            if (!actionHudMessageListener)
                actionHudMessageListener = GetComponentInChildren<ActionHudMessageListenerTMP>(true);
        }

        void InitializeViews()
        {
            if (timeline != null)
                timeline.Init(turnManager, combatManager, audioManager);

            if (turnHud != null)
                turnHud.Init(turnManager, combatManager, audioManager);

            if (chainPopup != null)
            {
                chainPopup.Init(turnManager, combatManager, audioManager);
                if (combatManager != null)
                    combatManager.chainPopupUiBehaviour = chainPopup;
            }
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
                combatManager.BonusTurnStateChanged += HandleBonusTurnStateChanged;

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

        void HandleBonusTurnStateChanged()
        {
            if (timeline != null)
                timeline.NotifyBonusTurnStateChangedExternal();
        }
    }
}
