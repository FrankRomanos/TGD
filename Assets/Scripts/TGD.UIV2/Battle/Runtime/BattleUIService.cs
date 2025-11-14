using UnityEngine;
using System;
using System.Collections.Generic;
using TGD.AudioV2;
using TGD.CombatV2;
using TGD.CoreV2;


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
        public TurnBannerController turnBanner;
        public ActionHudMessageListenerTMP actionHudMessageListener;

        bool _subscriptionsActive;
        bool _turnManagerSubscribed;
        bool _combatManagerSubscribed;
        bool _actionHudSubscribed;
        bool _turnLogSubscribed;
        readonly HashSet<string> _playerLabels = new();
        readonly HashSet<string> _enemyLabels = new();

        static T AutoFind<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>();
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

            if (turnBanner == null)
                turnBanner = AutoFind<TurnBannerController>();

            if (timeline == null)
                timeline = AutoFind<TurnTimelineController>();

            if (chainPopup == null)
                chainPopup = AutoFind<ChainPopupPresenter>();

            if (turnHud == null)
                turnHud = AutoFind<TurnHudController>();

            if (actionHudMessageListener == null)
                actionHudMessageListener = AutoFind<ActionHudMessageListenerTMP>();

        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            // --- 懒加载依赖（防御）
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
            if (turnBanner == null)
                turnBanner = AutoFind<TurnBannerController>();
            if (actionHudMessageListener == null)
                actionHudMessageListener = AutoFind<ActionHudMessageListenerTMP>();

            // --- 初始化每个UI控制器并把 manager 注入
            if (timeline != null)
            {
                timeline.Initialize(turnManager, combatManager);
                // UI -> Service 的回调：先防止重复，再订阅
                timeline.ActiveUnitDeferred -= OnUnitDeferred;
                timeline.ActiveUnitDeferred += OnUnitDeferred;
                timeline.ShowImmediate();
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
            }
            if (turnBanner != null)
                turnBanner.ForceHideImmediate();

            _playerLabels.Clear();
            _enemyLabels.Clear();
            // --- 游戏层事件 -> UI
            Subscribe();

            BeginTurnLogForwarding();

            // --- 第一次把当前战斗状态推给UI（血条/沙漏/当前激活角色等）
            DispatchInitialState();
        }


        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            // 1. 取消 gameplay -> UI 的订阅
            Unsubscribe();
            EndTurnLogForwarding();

            // 2. 取消 UI -> Service 的回调订阅
            if (timeline != null)
                timeline.ActiveUnitDeferred -= OnUnitDeferred;

            if (chainPopup != null)
                chainPopup.ChainPopupOpened -= HandleChainPopupOpened;

            // 3. 让每个UI控制器把自己复位并断开对manager的引用
            if (timeline != null)
            {
                timeline.Shutdown();     // <- 需要把原来的 Deinitialize 重命名成 Shutdown
                timeline.HideImmediate();
            }    

            if (chainPopup != null)
                chainPopup.Shutdown();   // <- 同理，ChainPopupPresenter 里的 Deinitialize 改成 Shutdown

            if (turnHud != null)
                turnHud.Shutdown();
            if (turnBanner != null)
                turnBanner.ForceHideImmediate();
            if (actionHudMessageListener != null)
                actionHudMessageListener.HideImmediate();
        }


        void OnDestroy()
        {
            if (!Application.isPlaying)
                return;

            // 理论上 OnDisable 已经清了所有事件。
            // 这里只是再防御一次，防止 Unity 某些极端销毁顺序下 OnDisable 没被调用。
            Unsubscribe();
            EndTurnLogForwarding();

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

            if (actionHudMessageListener != null && !_actionHudSubscribed)
            {
                HexMoveEvents.MoveRejected += HandleMoveRejected;
                HexMoveEvents.TimeRefunded += HandleMoveRefunded;
                AttackEventsV2.AttackRejected += HandleAttackRejected;
                AttackEventsV2.AttackMiss += HandleAttackMiss;
                _actionHudSubscribed = true;
            }

            _subscriptionsActive = _turnManagerSubscribed || _combatManagerSubscribed || _actionHudSubscribed;
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

            if (_actionHudSubscribed)
            {
                HexMoveEvents.MoveRejected -= HandleMoveRejected;
                HexMoveEvents.TimeRefunded -= HandleMoveRefunded;
                AttackEventsV2.AttackRejected -= HandleAttackRejected;
                AttackEventsV2.AttackMiss -= HandleAttackMiss;
                _actionHudSubscribed = false;
            }

            _subscriptionsActive = _turnManagerSubscribed || _combatManagerSubscribed || _actionHudSubscribed;
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
            if (turnManager != null)
                RegisterSide(turnManager.GetSideUnits(isPlayerPhase), isPlayerPhase);

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

            RegisterUnit(unit);
        }

        void HandleTurnEnded(Unit unit)
        {
            if (timeline != null)
                timeline.NotifyTurnEndedExternal(unit);

            if (turnHud != null)
                turnHud.HandleTurnEnded(unit);
        }

        void HandleMoveRefunded(Unit unit, int seconds)
        {
            if (!ShouldDisplayActionHud(unit))
                return;

            ShowActionHud($"+{seconds}s refunded", ActionHudMessageListenerTMP.HudKind.Time);
        }

        void HandleMoveRejected(Unit unit, MoveBlockReason reason, string message)
        {
            if (!ShouldDisplayActionHud(unit))
                return;

            string resolved = string.IsNullOrEmpty(message)
                ? ResolveMoveRejectionMessage(reason)
                : message;

            var kind = MapKindForMove(reason, resolved);
            ShowActionHud(resolved, kind);
        }

        void HandleAttackRejected(Unit unit, AttackRejectReasonV2 reason, string message)
        {
            if (!ShouldDisplayActionHud(unit))
                return;

            string resolved = string.IsNullOrEmpty(message)
                ? ResolveAttackRejectionMessage(reason)
                : message;

            var kind = MapKindForAttack(reason, resolved);
            ShowActionHud(resolved, kind);
        }

        void HandleAttackMiss(Unit unit, string message)
        {
            if (!ShouldDisplayActionHud(unit))
                return;

            string resolved = string.IsNullOrEmpty(message) ? "Attack missed." : message;
            ShowActionHud(resolved, HudKindByText(resolved));
        }

        bool ShouldDisplayActionHud(Unit unit, string label = null)
        {
            if (actionHudMessageListener == null)
                return false;

            if (turnManager == null)
                return true;

            if (unit != null)
                return turnManager.IsPlayerUnit(unit);

            if (string.IsNullOrEmpty(label))
                return false;

            if (_playerLabels.Contains(label))
                return true;

            if (_enemyLabels.Contains(label))
                return false;

            return label.IndexOf("(Player)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ShowActionHud(string message, ActionHudMessageListenerTMP.HudKind kind)
        {
            if (actionHudMessageListener == null || string.IsNullOrEmpty(message))
                return;

            actionHudMessageListener.ShowMessage(message, kind);
        }

        string ResolveMoveRejectionMessage(MoveBlockReason reason)
        {
            return reason switch
            {
                MoveBlockReason.Entangled => "I can't move!",
                MoveBlockReason.NoSteps => "Not now!",
                MoveBlockReason.OnCooldown => "Move is on cooldown.",
                MoveBlockReason.NotEnoughResource => "Not enough energy.",
                MoveBlockReason.PathBlocked => "That path is blocked.",
                MoveBlockReason.NoBudget => "No More Time",
                _ => "Can't move."
            };
        }

        string ResolveAttackRejectionMessage(AttackRejectReasonV2 reason)
        {
            return reason switch
            {
                AttackRejectReasonV2.NotReady => "Attack not ready.",
                AttackRejectReasonV2.Busy => "Already attacking.",
                AttackRejectReasonV2.OnCooldown => "Attack is on cooldown.",
                AttackRejectReasonV2.NotEnoughResource => "Not enough energy.",
                AttackRejectReasonV2.NoPath => "Can't reach that target.",
                AttackRejectReasonV2.CantMove => "Can't move to attack.",
                _ => "Attack unavailable."
            };
        }

        ActionHudMessageListenerTMP.HudKind MapKindForMove(MoveBlockReason reason, string message)
        {
            return reason switch
            {
                MoveBlockReason.NotEnoughResource => ActionHudMessageListenerTMP.HudKind.Energy,
                MoveBlockReason.NoBudget => ActionHudMessageListenerTMP.HudKind.Time,
                MoveBlockReason.Entangled => ActionHudMessageListenerTMP.HudKind.Snare,
                _ => HudKindByText(message)
            };
        }

        ActionHudMessageListenerTMP.HudKind MapKindForAttack(AttackRejectReasonV2 reason, string message)
        {
            return reason switch
            {
                AttackRejectReasonV2.NotEnoughResource => ActionHudMessageListenerTMP.HudKind.Energy,
                AttackRejectReasonV2.OnCooldown => ActionHudMessageListenerTMP.HudKind.Info,
                AttackRejectReasonV2.NoPath => ActionHudMessageListenerTMP.HudKind.Info,
                AttackRejectReasonV2.CantMove => ActionHudMessageListenerTMP.HudKind.Info,
                _ => HudKindByText(message)
            };
        }

        ActionHudMessageListenerTMP.HudKind HudKindByText(string message)
        {
            if (string.IsNullOrEmpty(message))
                return ActionHudMessageListenerTMP.HudKind.Info;

            if (message.IndexOf("energy", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActionHudMessageListenerTMP.HudKind.Energy;
            if (message.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActionHudMessageListenerTMP.HudKind.Time;
            if (message.IndexOf("entangle", StringComparison.OrdinalIgnoreCase) >= 0
             || message.IndexOf("can't move", StringComparison.OrdinalIgnoreCase) >= 0)
                return ActionHudMessageListenerTMP.HudKind.Snare;

            return ActionHudMessageListenerTMP.HudKind.Info;
        }

        void HandleTurnOrderChanged(bool isPlayerSide)
        {
            if (timeline != null)
                timeline.NotifyTurnOrderChangedExternal(isPlayerSide);

            RefreshTurnBannerCaches();
        }

        void HandleUnitRuntimeChanged(Unit unit)
        {
            if (turnHud != null)
                turnHud.HandleUnitRuntimeChanged(unit);

            RegisterUnit(unit);
        }

        void HandleChainFocusChanged(Unit unit)
        {
            if (turnHud != null)
                turnHud.HandleChainFocusChanged(unit);
        }

        void OnUnitDeferred(Unit u)
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

        void BeginTurnLogForwarding()
        {
            if (turnBanner == null || _turnLogSubscribed)
                return;

            Application.logMessageReceived += HandleTurnLogMessage;
            _turnLogSubscribed = true;
            RefreshTurnBannerCaches();
        }

        void EndTurnLogForwarding()
        {
            if (!_turnLogSubscribed)
                return;

            Application.logMessageReceived -= HandleTurnLogMessage;
            _turnLogSubscribed = false;
            _playerLabels.Clear();
            _enemyLabels.Clear();
        }

        void RefreshTurnBannerCaches()
        {
            _playerLabels.Clear();
            _enemyLabels.Clear();

            if (turnManager == null)
                return;

            RegisterSide(turnManager.GetSideUnits(true), true);
            RegisterSide(turnManager.GetSideUnits(false), false);

            var active = turnManager.ActiveUnit;
            if (active != null)
                RegisterUnit(active);
        }

        void HandleTurnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || string.IsNullOrEmpty(condition))
                return;

            if (condition.StartsWith("[Action]", StringComparison.Ordinal))
            {
                HandleActionLogMessage(condition);
                return;
            }

            if (turnBanner == null)
                return;

            if (!condition.StartsWith("[Turn]", StringComparison.Ordinal))
                return;

            TurnBannerTone tone = ResolveTone(condition);
            string display = FormatDisplayMessage(condition);
            turnBanner.EnqueueMessage(display, tone);
        }

        void HandleActionLogMessage(string message)
        {
            const string prefix = "[Action] ";
            if (!message.StartsWith(prefix, StringComparison.Ordinal))
                return;

            int labelStart = prefix.Length;
            int labelEnd = message.IndexOf(" [", labelStart, StringComparison.Ordinal);
            if (labelEnd <= labelStart)
                return;

            string label = message.Substring(labelStart, labelEnd - labelStart);
            if (string.IsNullOrEmpty(label))
                return;

            if (message.IndexOf("W2_ConfirmAbort", StringComparison.Ordinal) < 0
             || message.IndexOf("(reason=targetInvalid)", StringComparison.Ordinal) < 0)
                return;

            int toolStart = message.IndexOf('[', labelEnd + 1);
            int toolEnd = toolStart >= 0 ? message.IndexOf(']', toolStart + 1) : -1;
            if (toolStart < 0 || toolEnd <= toolStart)
                return;

            string toolId = message.Substring(toolStart + 1, toolEnd - toolStart - 1);

            var unit = ResolveUnitByLabel(label);
            if (unit != null)
                RegisterUnit(unit);

            var context = turnManager != null && unit != null ? turnManager.GetContext(unit) : null;
            if (context != null)
            {
                string moveSkillId = context.MoveSkillId;
                if (!string.IsNullOrEmpty(moveSkillId)
                 && string.Equals(toolId, moveSkillId, StringComparison.Ordinal))
                    return;
            }

            if (string.Equals(toolId, AttackProfileRules.DefaultSkillId, StringComparison.Ordinal))
                return;

            if (!ShouldDisplayActionHud(unit, label))
                return;

            const string hudMessage = "Invalid target.";
            ShowActionHud(hudMessage, ActionHudMessageListenerTMP.HudKind.Info);
        }

        TurnBannerTone ResolveTone(string message)
        {
            if (string.IsNullOrEmpty(message))
                return TurnBannerTone.Friendly;

            if (message.Contains("BonusT"))
                return TurnBannerTone.Bonus;

            string label = ExtractLabel(message);
            if (!string.IsNullOrEmpty(label))
            {
                if (!_playerLabels.Contains(label) && !_enemyLabels.Contains(label))
                    RegisterUnit(ResolveUnitByLabel(label));

                if (_playerLabels.Contains(label))
                    return TurnBannerTone.Friendly;
                if (_enemyLabels.Contains(label))
                    return TurnBannerTone.Enemy;
            }

            if (message.Contains("(Enemy)"))
                return TurnBannerTone.Enemy;
            if (message.Contains("(Player)"))
                return TurnBannerTone.Friendly;

            return TurnBannerTone.Friendly;
        }

        string FormatDisplayMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            if (!message.StartsWith("[Turn]"))
                return message;

            int extraIndex = message.IndexOf(" TT=");
            if (extraIndex > 0)
                return message.Substring(0, extraIndex).TrimEnd();

            return message;
        }

        string ExtractLabel(string message)
        {
            int open = message.IndexOf('(');
            int close = message.IndexOf(')', open + 1);
            if (open >= 0 && close > open)
                return message.Substring(open + 1, close - open - 1);
            return null;
        }

        void RegisterSide(IReadOnlyList<Unit> units, bool isPlayerSide)
        {
            if (units == null)
                return;

            for (int i = 0; i < units.Count; i++)
                RegisterUnit(units[i], isPlayerSide);
        }

        void RegisterUnit(Unit unit, bool? forceSide = null)
        {
            if (unit == null)
                return;

            string label = TurnManagerV2.FormatUnitLabel(unit);
            if (string.IsNullOrEmpty(label))
                return;

            bool isPlayer = forceSide ?? (turnManager != null && turnManager.IsPlayerUnit(unit));
            bool isEnemy = forceSide.HasValue ? !forceSide.Value : (turnManager != null && turnManager.IsEnemyUnit(unit));

            if (isPlayer)
            {
                _playerLabels.Add(label);
                _enemyLabels.Remove(label);
            }
            else if (isEnemy)
            {
                _enemyLabels.Add(label);
                _playerLabels.Remove(label);
            }
        }

        Unit ResolveUnitByLabel(string label)
        {
            if (turnManager == null || string.IsNullOrEmpty(label))
                return null;

            var playerUnits = turnManager.GetSideUnits(true);
            if (playerUnits != null)
            {
                for (int i = 0; i < playerUnits.Count; i++)
                {
                    var unit = playerUnits[i];
                    if (unit != null && TurnManagerV2.FormatUnitLabel(unit) == label)
                        return unit;
                }
            }

            var enemyUnits = turnManager.GetSideUnits(false);
            if (enemyUnits != null)
            {
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var unit = enemyUnits[i];
                    if (unit != null && TurnManagerV2.FormatUnitLabel(unit) == label)
                        return unit;
                }
            }

            return null;
        }
    }
}
