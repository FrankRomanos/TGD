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

            if (timeline != null)
                timeline.ForceRebuildNow();
        }

        void Start()
        {
            if (timeline != null)
                timeline.Initialize(turnManager, combatManager, audioManager);

            _didStart = true;
            Subscribe();
        }

        void OnEnable()
        {
            if (timeline != null)
                timeline.ActiveUnitDeferred += OnUnitDeferred;

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
            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;

            Unsubscribe();
        }

        void OnDestroy()
        {
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

        void OnUnitDeferred(TGD.HexBoard.Unit u)
        {
            if (audioManager != null)
                audioManager.PlayEvent(BattleAudioEvent.TurnTimelineInsert);
            UnityEngine.Debug.Log($"[BattleUIService] Deferred unit: {u}");
        }

        void HandleBonusTurnStateChanged()
        {
            if (timeline != null)
                timeline.NotifyBonusTurnStateChangedExternal();
        }
    }
}
