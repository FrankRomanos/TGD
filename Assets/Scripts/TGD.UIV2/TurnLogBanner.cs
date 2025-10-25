using System.Collections;
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

        [Header("Fade Animation")]
        public bool enableFade = true;
        [Min(0f)]
        public float fadeInDuration = 0.25f;
        [Min(0f)]
        public float fadeOutDuration = 0.25f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        readonly Queue<(string message, Color color)> _queue = new();
        readonly HashSet<string> _playerLabels = new();
        readonly HashSet<string> _enemyLabels = new();
        float _timer;
        bool _showing;
        bool _isVisible;
        Coroutine _fadeRoutine;

        static T AutoFind<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return FindObjectOfType<T>();
#endif
        }
        [SerializeField] float lingerAfterEmpty = 0.2f;
        float _emptySince = -1f;
        void Awake()
        {
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
        }

        void OnEnable()
        {
            RegisterManagerEvents();
            Application.logMessageReceived += HandleLogMessage;
            bool initialVisible = !autoHideWhenEmpty;
            _isVisible = !initialVisible;
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

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            // 直接落地，不走协程
            if (canvasGroup)
            {
                bool targetVisible = !autoHideWhenEmpty;
                canvasGroup.alpha = targetVisible ? 1f : 0f;
                canvasGroup.interactable = targetVisible;
                canvasGroup.blocksRaycasts = targetVisible;
            }

            _isVisible = !autoHideWhenEmpty;
            if (messageText) messageText.text = string.Empty;
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
                if (_emptySince < 0f) _emptySince = Time.time;
                if (Time.time - _emptySince >= lingerAfterEmpty)
                {
                    if (autoHideWhenEmpty) SetVisible(false);
                    if (messageText) messageText.text = string.Empty;
                }
                return;
            }

            _emptySince = -1f; // 有消息了，清除空闲计时
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
            if (_isVisible == visible)
            {
                if (!visible && canvasGroup)
                {
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }
                return;
            }

            _isVisible = visible;

            if (!canvasGroup)
            {
                if (messageText) messageText.enabled = visible;
                return;
            }

            // >>> 关键：在物体未启用或不需要淡入淡出时，直接设置，不开协程
            if (!enableFade || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            _fadeRoutine = StartCoroutine(FadeCanvas(visible));
        }


        IEnumerator FadeCanvas(bool visible)
        {
            float duration = visible ? fadeInDuration : fadeOutDuration;
            AnimationCurve curve = visible ? fadeInCurve : fadeOutCurve;
            float startAlpha = canvasGroup.alpha;
            float endAlpha = visible ? 1f : 0f;

            if (duration <= 0f)
            {
                canvasGroup.alpha = endAlpha;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve != null ? curve.Evaluate(t) : t;
                    canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, curveValue);
                    yield return null;
                }
                canvasGroup.alpha = endAlpha;
            }

            if (!visible)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
