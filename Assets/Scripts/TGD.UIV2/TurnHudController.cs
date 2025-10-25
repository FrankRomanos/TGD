using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.UI
{
    /// <summary>
    /// Displays the active unit HUD for TurnManagerV2 driven encounters.
    /// </summary>
    public sealed class TurnHudController : MonoBehaviour
    {
        [Header("Runtime Refs")]
        public TurnManagerV2 turnManager;
        public CombatActionManagerV2 combatManager;

        [Header("Root")]
        public CanvasGroup rootGroup;
        public TMP_Text unitLabel;

        [Header("Health")]
        public Image healthFill;
        public TMP_Text healthValue;

        [Header("Energy")]
        public Image energyFill;
        public TMP_Text energyValue;
        public TMP_Text energyRegen;

        [Header("Time Budget")]
        public Transform hourglassContainer;
        public GameObject hourglassPrefab;
        public Sprite hourglassAvailableSprite;
        public Sprite hourglassConsumedSprite;
        public Color hourglassAvailableColor = Color.white;
        public Color hourglassConsumedColor = new(0.4f, 0.4f, 0.4f, 1f);
        public float visibleAlpha = 1f;
        public float hiddenAlpha = 0f;

        Unit _displayUnit;
        readonly List<Image> _hourglassPool = new();

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
            if (!combatManager)
                combatManager = AutoFind<CombatActionManagerV2>();

            CacheInitialHourglasses();
        }

        void OnEnable()
        {
            Subscribe();
            RefreshDisplayUnit(turnManager != null ? turnManager.ActiveUnit : null);
            RefreshAll();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Subscribe()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted += OnTurnStarted;
                turnManager.TurnEnded += OnTurnEnded;
                turnManager.UnitRuntimeChanged += OnUnitRuntimeChanged;
                turnManager.PhaseBegan += OnPhaseBegan;
            }

            if (combatManager != null)
            {
                combatManager.ChainFocusChanged += OnChainFocusChanged;
            }
        }

        void Unsubscribe()
        {
            if (turnManager != null)
            {
                turnManager.TurnStarted -= OnTurnStarted;
                turnManager.TurnEnded -= OnTurnEnded;
                turnManager.UnitRuntimeChanged -= OnUnitRuntimeChanged;
                turnManager.PhaseBegan -= OnPhaseBegan;
            }

            if (combatManager != null)
            {
                combatManager.ChainFocusChanged -= OnChainFocusChanged;
            }
        }

        void CacheInitialHourglasses()
        {
            _hourglassPool.Clear();
            if (!hourglassContainer)
                return;

            for (int i = 0; i < hourglassContainer.childCount; i++)
            {
                var child = hourglassContainer.GetChild(i);
                if (!child)
                    continue;
                var img = child.GetComponent<Image>();
                if (img)
                    _hourglassPool.Add(img);
            }
        }

        void OnTurnStarted(Unit unit)
        {
            RefreshDisplayUnit(unit);
        }

        void OnTurnEnded(Unit unit)
        {
            if (unit == _displayUnit)
                RefreshAll();
        }

        void OnUnitRuntimeChanged(Unit unit)
        {
            if (unit == _displayUnit)
                RefreshStats();
        }

        void OnPhaseBegan(bool isPlayerPhase)
        {
            RefreshVisibility();
        }

        void OnChainFocusChanged(Unit unit)
        {
            if (unit == null)
                RefreshDisplayUnit(turnManager != null ? turnManager.ActiveUnit : null);
            else
                RefreshDisplayUnit(unit);
        }

        void RefreshDisplayUnit(Unit unit)
        {
            if (unit == _displayUnit)
                return;

            _displayUnit = unit;
            RefreshAll();
        }

        void RefreshAll()
        {
            RefreshStats();
            RefreshVisibility();
        }

        void RefreshStats()
        {
            if (_displayUnit == null)
            {
                SetUnitLabel("-");
                UpdateHealth(0, 0);
                UpdateEnergy(0, 0, 0);
                UpdateHourglasses(0, 0);
                return;
            }

            string label = turnManager != null ? TurnManagerV2.FormatUnitLabel(_displayUnit) : _displayUnit.Id;
            SetUnitLabel(label);

            var context = turnManager != null ? turnManager.GetContext(_displayUnit) : null;
            var stats = context != null ? context.stats : null;

            int hp = stats != null ? Mathf.Max(0, stats.HP) : 0;
            int maxHp = stats != null ? Mathf.Max(1, stats.MaxHP) : 1;
            UpdateHealth(hp, maxHp);

            int energy = stats != null ? Mathf.Max(0, stats.Energy) : 0;
            int maxEnergy = stats != null ? Mathf.Max(0, stats.MaxEnergy) : 0;
            int regenPer2s = stats != null ? Mathf.Max(0, stats.EnergyRegenPer2s) : 0;
            UpdateEnergy(energy, maxEnergy, regenPer2s);

            int baseTime = 0;
            int remaining = 0;
            if (turnManager != null && turnManager.TryGetRuntimeSnapshot(_displayUnit, out var snapshot))
            {
                baseTime = snapshot.baseTime > 0 ? snapshot.baseTime : snapshot.turnTime;
                baseTime = Mathf.Max(0, baseTime);
                int runtimeRemaining = Mathf.Max(0, snapshot.remaining);
                remaining = baseTime > 0 ? Mathf.Min(runtimeRemaining, baseTime) : runtimeRemaining;
            }

            if (combatManager != null && combatManager.IsBonusTurnFor(_displayUnit))
            {
                int cap = combatManager.CurrentBonusTurnCap;
                if (cap > 0)
                    baseTime = baseTime > 0 ? Mathf.Min(baseTime, cap) : cap;
                int bonusRemain = Mathf.Clamp(combatManager.CurrentBonusTurnRemaining, 0, cap > 0 ? cap : int.MaxValue);
                remaining = baseTime > 0 ? Mathf.Min(bonusRemain, baseTime) : bonusRemain;
            }

            UpdateHourglasses(baseTime, baseTime - remaining);
        }

        void UpdateHealth(int current, int max)
        {
            if (healthFill)
                healthFill.fillAmount = max > 0 ? current / (float)max : 0f;

            if (healthValue)
                healthValue.text = max > 0 ? $"{current}/{max}" : "0/0";
        }

        void UpdateEnergy(int current, int max, int regen)
        {
            float fill = max > 0 ? current / (float)max : 0f;
            if (energyFill)
                energyFill.fillAmount = Mathf.Clamp01(fill);

            if (energyRegen && energyRegen != energyValue)
                energyRegen.text = $"+{regen}";

            if (energyValue)
            {
                if (energyRegen && energyRegen != energyValue)
                    energyValue.text = max > 0 ? $"{current}/{max}" : "0/0";
                else
                    energyValue.text = max > 0 ? $"{current}/{max} +{regen}" : $"0/0 +{regen}";
            }
        }

        void UpdateHourglasses(int maxTime, int used)
        {
            if (!hourglassContainer)
                return;

            maxTime = Mathf.Max(0, maxTime);
            used = Mathf.Clamp(used, 0, maxTime);

            EnsureHourglassPool(maxTime);

            for (int i = 0; i < _hourglassPool.Count; i++)
            {
                var image = _hourglassPool[i];
                if (!image)
                    continue;

                bool active = i < maxTime;
                if (image.gameObject.activeSelf != active)
                    image.gameObject.SetActive(active);

                if (!active)
                    continue;

                bool consumed = i < used;
                image.sprite = consumed
                    ? (hourglassConsumedSprite ? hourglassConsumedSprite : hourglassAvailableSprite)
                    : hourglassAvailableSprite;
                image.color = consumed ? hourglassConsumedColor : hourglassAvailableColor;
            }
        }

        void EnsureHourglassPool(int count)
        {
            if (count <= _hourglassPool.Count)
                return;

            if (!hourglassContainer)
                return;

            for (int i = _hourglassPool.Count; i < count; i++)
            {
                Image img = null;
                if (hourglassPrefab)
                {
                    var go = Instantiate(hourglassPrefab, hourglassContainer);
                    img = go ? go.GetComponent<Image>() : null;
                }
                else
                {
                    var go = new GameObject($"Hourglass_{i}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(hourglassContainer, false);
                    img = go.GetComponent<Image>();
                }

                if (img)
                {
                    if (!img.sprite && hourglassAvailableSprite)
                        img.sprite = hourglassAvailableSprite;
                    img.color = hourglassAvailableColor;
                    _hourglassPool.Add(img);
                }
            }
        }

        void SetUnitLabel(string label)
        {
            if (unitLabel)
                unitLabel.text = label ?? string.Empty;
        }

        void RefreshVisibility()
        {
            bool visible = ShouldBeVisible();

            if (rootGroup)
            {
                rootGroup.alpha = visible ? visibleAlpha : hiddenAlpha;
                rootGroup.interactable = visible;
                rootGroup.blocksRaycasts = visible;
            }
            else
            {
                if (gameObject.activeSelf != visible)
                    gameObject.SetActive(visible);
            }

        }

        bool ShouldBeVisible()
        {
            if (_displayUnit == null)
                return false;

            bool isPlayerUnit = turnManager != null && turnManager.IsPlayerUnit(_displayUnit);
            if (!isPlayerUnit)
                return false;

            bool playerPhase = turnManager != null && turnManager.IsPlayerPhase;
            bool hasBonus = combatManager != null && combatManager.IsBonusTurnFor(_displayUnit);
            return playerPhase || hasBonus;
        }
    }
}
