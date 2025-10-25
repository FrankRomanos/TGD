using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UI
{
    /// <summary>
    /// Displays the most recent turn log messages with faction aware coloring.
    /// </summary>
    public sealed class TurnLogBanner : MonoBehaviour
    {
        [Header("Runtime Refs")]
        public TurnManagerV2 turnManager;

        [Header("UI")]
        public TMP_Text messageText;
        public CanvasGroup canvasGroup;

        [Header("Look")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f);
        public Color enemyColor = new(0.85f, 0.2f, 0.2f);
        public Color bonusColor = new(0.2f, 0.35f, 0.85f);
        public float displaySeconds = 2f;
        public bool autoHideWhenEmpty = true;

        readonly Queue<(string message, Color color)> _queue = new();
        readonly HashSet<string> _playerLabels = new();
        readonly HashSet<string> _enemyLabels = new();
        float _timer;
        bool _showing;

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
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
        }

        void OnEnable()
        {
            RegisterManagerEvents();
            Application.logMessageReceived += HandleLogMessage;
            SetVisible(!autoHideWhenEmpty);
            if (messageText)
                messageText.text = string.Empty;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= HandleLogMessage;
            UnregisterManagerEvents();
            _queue.Clear();
            _showing = false;
            _timer = 0f;
            SetVisible(!autoHideWhenEmpty);
            if (messageText)
                messageText.text = string.Empty;
        }

        void Update()
        {
            if (!_showing)
            {
                TryDisplayNext();
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _showing = false;
                TryDisplayNext();
            }
        }

        void RegisterManagerEvents()
        {
            if (!turnManager)
                return;

            turnManager.TurnStarted += OnTurnStarted;
            turnManager.PlayerPhaseStarted += OnPlayerPhaseStarted;
            turnManager.EnemyPhaseStarted += OnEnemyPhaseStarted;
        }

        void UnregisterManagerEvents()
        {
            if (!turnManager)
                return;

            turnManager.TurnStarted -= OnTurnStarted;
            turnManager.PlayerPhaseStarted -= OnPlayerPhaseStarted;
            turnManager.EnemyPhaseStarted -= OnEnemyPhaseStarted;
        }

        void OnTurnStarted(Unit unit)
        {
            RegisterUnit(unit);
        }

        void OnPlayerPhaseStarted()
        {
            RegisterSide(turnManager?.GetSideUnits(true), true);
        }

        void OnEnemyPhaseStarted()
        {
            RegisterSide(turnManager?.GetSideUnits(false), false);
        }

        void RegisterSide(IReadOnlyList<Unit> units, bool isPlayer)
        {
            if (units == null)
                return;

            foreach (var unit in units)
                RegisterUnit(unit, isPlayer);
        }

        void RegisterUnit(Unit unit, bool? forceSide = null)
        {
            if (unit == null)
                return;

            string label = TurnManagerV2.FormatUnitLabel(unit);
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

        void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || string.IsNullOrEmpty(condition))
                return;

            if (!condition.StartsWith("[Turn]"))
                return;

            Enqueue(condition);
        }

        void Enqueue(string message)
        {
            var color = ResolveColor(message);
            string display = FormatDisplayMessage(message);
            _queue.Enqueue((display, color));
            if (!_showing)
                TryDisplayNext();
        }

        Color ResolveColor(string message)
        {
            if (message.Contains("BonusT"))
                return bonusColor;

            string label = ExtractLabel(message);
            if (!string.IsNullOrEmpty(label))
            {
                if (!_playerLabels.Contains(label) && !_enemyLabels.Contains(label))
                    RegisterUnit(ResolveUnitByLabel(label));

                if (_playerLabels.Contains(label))
                    return friendlyColor;
                if (_enemyLabels.Contains(label))
                    return enemyColor;
            }

            // Default fallback based on phase keywords
            if (message.Contains("(Enemy)"))
                return enemyColor;
            if (message.Contains("(Player)"))
                return friendlyColor;

            return friendlyColor;
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

        void TryDisplayNext()
        {
            if (_queue.Count == 0)
            {
                if (autoHideWhenEmpty)
                    SetVisible(false);
                if (messageText)
                    messageText.text = string.Empty;
                return;
            }

            var entry = _queue.Dequeue();
            if (messageText)
            {
                messageText.text = entry.message;
                messageText.color = entry.color;
            }

            _timer = Mathf.Max(0.1f, displaySeconds);
            _showing = true;
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (canvasGroup)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
                return;
            }

            if (messageText)
                messageText.enabled = visible;
        }
    }
}
