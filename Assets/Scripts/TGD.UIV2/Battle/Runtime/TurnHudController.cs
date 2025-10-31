using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TGD.CombatV2;
using TGD.HexBoard;


namespace TGD.UIV2.Battle
{
    /// <summary>
    /// Displays the active unit HUD for TurnManagerV2 driven encounters.
    /// </summary>
    public sealed class TurnHudController : MonoBehaviour
    {
        [Header("Root")]
        public CanvasGroup rootGroup;
        public TMP_Text unitLabel;

        [Header("Health")]
        public Image healthFill;
        public TMP_Text healthValue;
        public TurnHudStatGauge healthGauge;

        [Header("Energy")]
        public Image energyFill;
        public TMP_Text energyValue;
        public TMP_Text energyRegen;
        public TurnHudStatGauge energyGauge;

        [Header("Time Budget")]
        public RectTransform hourglassContainer;
        public GameObject hourglassPrefab;
        public Sprite hourglassAvailableSprite;
        public Sprite hourglassConsumedSprite;
        public Color hourglassAvailableColor = Color.white;
        public Color hourglassConsumedColor = new(0.4f, 0.4f, 0.4f, 1f);
        public Vector2 hourglassSize = new(48f, 48f);
        public float visibleAlpha = 1f;
        public float hiddenAlpha = 0f;

        TurnManagerV2 _turnManager;
        CombatActionManagerV2 _combatManager;
        bool _isInitialized;
        Unit _displayUnit;
        readonly List<Image> _hourglassPool = new();
        readonly List<bool> _hourglassConsumedState = new();
        bool _enemyPhaseActive;
        bool _hourglassStateInitialized;
        bool _forceInstantStats;

        void Awake()
        {
            // 把沙漏池子建好、记住初始大小等
            CacheInitialHourglasses();

            // 容错：如果 Inspector 没手动绑 gauge，那就顺藤摸瓜找一遍
            if (healthGauge == null && healthFill != null)
                healthGauge = healthFill.GetComponentInParent<TurnHudStatGauge>();
            if (energyGauge == null && energyFill != null)
                energyGauge = energyFill.GetComponentInParent<TurnHudStatGauge>();

            // 初始状态：没有激活角色，HUD 应该是干净/隐藏的
            _displayUnit = null;
            _forceInstantStats = true;
            RefreshAll(); // 这会把血条归零/隐藏自己等
        }


        public void Initialize(TurnManagerV2 turnManager, CombatActionManagerV2 combatManager)
        {
            _turnManager = turnManager;
            _combatManager = combatManager;

            // 必须两个核心管理器都注入才算真正上线
            _isInitialized = (_turnManager != null && _combatManager != null);

            // 敌方回合标记，用于决定HUD要不要显示
            _enemyPhaseActive = _turnManager != null && !_turnManager.IsPlayerPhase;

            // 第一帧刷新的数值别做渐变，直接瞬移到位
            _forceInstantStats = true;

            // 选中当前激活单位（玩家那边）作为展示对象
            RefreshDisplayUnit(_turnManager != null ? _turnManager.ActiveUnit : null);

            // 刷整套UI
            RefreshAll();
        }

        public void Shutdown()
        {
            _turnManager = null;
            _combatManager = null;
            _isInitialized = false;

            _displayUnit = null;
            _enemyPhaseActive = false;
            _hourglassStateInitialized = false;

            // 下一次如果再被 Initialize，我们希望数值直接瞬移到正确值
            _forceInstantStats = true;

            // 刷一遍，把血条清零、自己淡出
            RefreshAll();
        }

        void CacheInitialHourglasses()
        {
            _hourglassPool.Clear();
            if (!hourglassContainer) return;

            for (int i = 0; i < hourglassContainer.childCount; i++)
            {
                var child = hourglassContainer.GetChild(i);
                if (!child) continue;

                var img = child.GetComponent<Image>();
                if (!img) continue;

                PrepareHourglass(img);
                _hourglassPool.Add(img);
            }

            // 改完尺寸后，强制重建一遍布局，立刻生效
            LayoutRebuilder.ForceRebuildLayoutImmediate(hourglassContainer);
            EnsureHourglassStateCapacity(_hourglassPool.Count);
        }

        public void HandleTurnStarted(Unit unit)
        {
            if (!_isInitialized)
                return;

            if (!IsPlayerUnit(unit) && _enemyPhaseActive)
            {
                RefreshAll();
                return;
            }

            RefreshDisplayUnit(unit);
        }

        public void HandleTurnEnded(Unit unit)
        {
            if (!_isInitialized)
                return;

            if (unit == _displayUnit)
                RefreshAll();
        }

        public void HandleUnitRuntimeChanged(Unit unit)
        {
            if (!_isInitialized)
                return;

            if (unit == _displayUnit)
                RefreshStats();
        }

        public void HandlePhaseBegan(bool isPlayerPhase)
        {
            if (!_isInitialized)
                return;

            _enemyPhaseActive = !isPlayerPhase;
            if (isPlayerPhase)
            {
                RefreshDisplayUnit(_turnManager != null ? _turnManager.ActiveUnit : _displayUnit);
            }
            else
            {
                var focus = GetCurrentChainFocus();
                if (IsPlayerUnit(focus))
                {
                    RefreshDisplayUnit(focus);
                }
                else if (IsPlayerUnit(_displayUnit))
                {
                    RefreshAll();
                }
                else
                {
                    RefreshDisplayUnit(ResolveFirstPlayerUnit());
                }
            }
        }

        public void HandleChainFocusChanged(Unit unit)
        {
            if (!_isInitialized)
                return;

            if (unit == null)
            {
                if (_enemyPhaseActive)
                {
                    if (!IsPlayerUnit(_displayUnit))
                        RefreshDisplayUnit(ResolveFirstPlayerUnit());
                    else
                        RefreshAll();
                }
                else
                {
                    RefreshDisplayUnit(_turnManager != null ? _turnManager.ActiveUnit : _displayUnit);
                }
                return;
            }

            if (!IsPlayerUnit(unit))
            {
                if (_enemyPhaseActive)
                {
                    if (!IsPlayerUnit(_displayUnit))
                        RefreshDisplayUnit(ResolveFirstPlayerUnit());
                    else
                        RefreshAll();
                }
                else
                {
                    RefreshAll();
                }
                return;
            }

            RefreshDisplayUnit(unit);
        }

        public void HandleBonusTurnStateChanged()
        {
            if (!_isInitialized)
                return;

            RefreshAll();
        }

        void RefreshDisplayUnit(Unit unit)
        {
            if (unit != null && unit == _displayUnit)
            {
                RefreshAll();
                return;
            }

            bool changed = unit != _displayUnit;
            _displayUnit = unit;
            _hourglassStateInitialized = false;
            if (changed)
                _forceInstantStats = true;
            RefreshAll();
        }

        void RefreshAll()
        {
            RefreshStats();
            RefreshVisibility();
        }

        void RefreshStats()
        {
            bool instant = _forceInstantStats;
            _forceInstantStats = false;

            if (_displayUnit == null)
            {
                SetUnitLabel("-");
                UpdateHealth(0, 0, instant);
                UpdateEnergy(0, 0, 0, instant);
                UpdateHourglasses(0, 0);
                return;
            }

            string label = _turnManager != null ? TurnManagerV2.FormatUnitLabel(_displayUnit) : _displayUnit.Id;
            SetUnitLabel(label);

            var context = _turnManager != null ? _turnManager.GetContext(_displayUnit) : null;
            var stats = context != null ? context.stats : null;

            int hp = stats != null ? Mathf.Max(0, stats.HP) : 0;
            int maxHp = stats != null ? Mathf.Max(1, stats.MaxHP) : 1;
            UpdateHealth(hp, maxHp, instant);

            int energy = stats != null ? Mathf.Max(0, stats.Energy) : 0;
            int maxEnergy = stats != null ? Mathf.Max(0, stats.MaxEnergy) : 0;
            int regenPer2s = stats != null ? Mathf.Max(0, stats.EnergyRegenPer2s) : 0;
            UpdateEnergy(energy, maxEnergy, regenPer2s, instant);

            int baseTime = 0;
            int remaining = 0;
            if (_turnManager != null && _turnManager.TryGetRuntimeSnapshot(_displayUnit, out var snapshot))
            {
                baseTime = snapshot.baseTime > 0 ? snapshot.baseTime : snapshot.turnTime;
                baseTime = Mathf.Max(0, baseTime);
                int runtimeRemaining = Mathf.Max(0, snapshot.remaining);
                remaining = baseTime > 0 ? Mathf.Min(runtimeRemaining, baseTime) : runtimeRemaining;
            }

            if (_combatManager != null && _combatManager.IsBonusTurnFor(_displayUnit))
            {
                int cap = _combatManager.CurrentBonusTurnCap;
                if (cap > 0)
                    baseTime = baseTime > 0 ? Mathf.Min(baseTime, cap) : cap;
                int bonusRemain = Mathf.Clamp(_combatManager.CurrentBonusTurnRemaining, 0, cap > 0 ? cap : int.MaxValue);
                remaining = baseTime > 0 ? Mathf.Min(bonusRemain, baseTime) : bonusRemain;
            }

            UpdateHourglasses(baseTime, baseTime - remaining);
        }

        void UpdateHealth(int current, int max, bool immediate)
        {
            if (healthGauge)
            {
                healthGauge.SetValue(current, max, null, immediate);
                return;
            }

            if (healthFill)
                healthFill.fillAmount = max > 0 ? current / (float)max : 0f;

            if (healthValue)
                healthValue.text = max > 0 ? $"{current}/{max}" : "0/0";
        }

        void UpdateEnergy(int current, int max, int regen, bool immediate)
        {
            if (energyGauge)
            {
                string regenText = $"+{regen}";
                energyGauge.SetValue(current, max, regenText, immediate);
                return;
            }

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

            for (int i = _hourglassConsumedState.Count; i < _hourglassPool.Count; i++)
                _hourglassConsumedState.Add(false);

            for (int i = 0; i < _hourglassPool.Count; i++)
            {
                var image = _hourglassPool[i];
                if (!image)
                    continue;

                bool active = i < maxTime;
                if (image.gameObject.activeSelf != active)
                    image.gameObject.SetActive(active);

                var animator = EnsureHourglassAnimator(image);
                if (animator != null)
                    animator.ConfigureSprites(hourglassAvailableSprite, hourglassConsumedSprite, hourglassAvailableColor, hourglassConsumedColor);

                if (!active)
                {
                    if (animator != null)
                        animator.SetConsumed(false, false);
                    if (i < _hourglassConsumedState.Count)
                        _hourglassConsumedState[i] = false;
                    continue;
                }

                bool consumed = i < used;
                bool animate = _hourglassStateInitialized && i < _hourglassConsumedState.Count && _hourglassConsumedState[i] != consumed;

                if (animator != null)
                {
                    animator.SetConsumed(consumed, animate);
                }
                else
                {
                    image.sprite = consumed
                        ? (hourglassConsumedSprite ? hourglassConsumedSprite : hourglassAvailableSprite)
                        : hourglassAvailableSprite;
                    image.color = consumed ? hourglassConsumedColor : hourglassAvailableColor;
                }

                if (i < _hourglassConsumedState.Count)
                    _hourglassConsumedState[i] = consumed;
            }

            _hourglassStateInitialized = maxTime > 0;
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
                    PrepareHourglass(img);
                    _hourglassPool.Add(img);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(hourglassContainer);
            EnsureHourglassStateCapacity(_hourglassPool.Count);
        }

        void ApplyHourglassSize(Image image)
        {
            if (!image) return;

            // 1) 用 LayoutElement 才能赢过 HorizontalLayoutGroup
            var le = image.GetComponent<LayoutElement>();
            if (!le) le = image.gameObject.AddComponent<LayoutElement>();

            if (hourglassSize.x > 0f)
            {
                le.preferredWidth = hourglassSize.x;
                le.minWidth = hourglassSize.x;        // 防止被压扁
                le.flexibleWidth = 0f;                // 不吃弹性空间
            }
            if (hourglassSize.y > 0f)
            {
                le.preferredHeight = hourglassSize.y;
                le.minHeight = hourglassSize.y;
                le.flexibleHeight = 0f;
            }

            // 2) 可选：保持图标不变形
            image.preserveAspect = true;
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
            if (!_isInitialized)
                return false;

            if (_displayUnit == null)
                return false;

            bool isPlayerUnit = _turnManager != null && _turnManager.IsPlayerUnit(_displayUnit);
            if (!isPlayerUnit)
                return false;

            if (_enemyPhaseActive)
                return true;

            bool playerPhase = _turnManager != null && _turnManager.IsPlayerPhase;
            if (playerPhase)
                return true;

            bool hasBonus = _combatManager != null && _combatManager.IsBonusTurnFor(_displayUnit);
            return hasBonus;
        }

        void PrepareHourglass(Image image)
        {
            if (!image)
                return;

            image.raycastTarget = false;
            if (!image.sprite && hourglassAvailableSprite)
                image.sprite = hourglassAvailableSprite;
            image.color = hourglassAvailableColor;

            ApplyHourglassSize(image);

            var animator = EnsureHourglassAnimator(image);
            if (animator != null)
                animator.ConfigureSprites(hourglassAvailableSprite, hourglassConsumedSprite, hourglassAvailableColor, hourglassConsumedColor);
        }

        TurnHudHourglass EnsureHourglassAnimator(Image image)
        {
            if (!image)
                return null;

            var animator = image.GetComponent<TurnHudHourglass>();
            if (!animator)
                animator = image.gameObject.AddComponent<TurnHudHourglass>();
            return animator;
        }

        void EnsureHourglassStateCapacity(int count)
        {
            while (_hourglassConsumedState.Count < count)
                _hourglassConsumedState.Add(false);
        }

        Unit ResolveFirstPlayerUnit()
        {
            if (_turnManager == null)
                return null;

            var list = _turnManager.GetSideUnits(true);
            if (list == null)
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                var unit = list[i];
                if (unit != null)
                    return unit;
            }

            return null;
        }

        Unit GetCurrentChainFocus()
        {
            return _combatManager != null ? _combatManager.CurrentChainFocus : null;
        }

        bool IsPlayerUnit(Unit unit)
        {
            return unit != null && _turnManager != null && _turnManager.IsPlayerUnit(unit);
        }
    }
}
