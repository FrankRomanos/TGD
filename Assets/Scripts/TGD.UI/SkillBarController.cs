// Assets/Scripts/TGD.UI/SkillBarController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TGD.Combat;
using TGD.Data;

namespace TGD.UI
{
    /// <summary>
    /// 10格技能条（点击暂时直接对自己释放，先跑流程）：
    /// 1 移动（Class里最大的 skillID）
    /// 2 普攻（第二大）
    /// 3 精通（最小）
    /// 4 DeepBlue
    /// 5 DarkYellow
    /// 6 Green
    /// 7 LightBlue
    /// 8 Purple
    /// 9 Red（大招）
    /// 10 Red（第二个红，治疗类）
    /// </summary>
    public sealed class SkillBarController : BaseTurnUiBehaviour
    {
        [Header("UI")]
        public Transform slotsRoot;         // HorizontalLayoutGroup 容器
        public GameObject slotPrefab;       // 你做好的 UI_SkillSlot 预制
        public int totalSlots = 10;

        [Header("Hotkeys (optional)")]
        public string[] hotkeys = { "Q", "W", "E", "R", "A", "S", "D", "F", "G", "H" };
        [Header("Skill Ordering")]
        public string moveSkillTag = "Move";

        [Header("Targeting")]
        public SkillTargetingController targetingController;


        // 运行态
        Unit _active;
        readonly List<SkillSlotView> _slots = new();
        readonly List<SkillDefinition> _ordered = new();
        readonly Dictionary<string, SkillDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        int _lastSkillsRevision = -1;
        bool HasActive => _active != null;
        protected override void HandleTurnBegan(Unit u)
        {
            _active = u;
            targetingController ??= FindFirstObjectByTypeSafe<SkillTargetingController>();
            targetingController?.CancelSelection();
            BuildBar();
            Refresh();
            _lastSkillsRevision = _active?.SkillsRevision ?? -1;
            gameObject.SetActive(true);
        }

        protected override void HandleTurnEnded(Unit u)
        {
            targetingController?.CancelSelection();
            gameObject.SetActive(false);
            _active = null;
            _lastSkillsRevision = -1;
        }

        // ---------- 构建 ----------
        void BuildBar()
        {
            EnsureSlotViews();
            PickSkillsFor(_active, moveSkillTag, totalSlots, _ordered, _byId);

            for (int i = 0; i < totalSlots; i++)
            {
                var view = _slots[i];
                SkillDefinition s = (i < _ordered.Count) ? _ordered[i] : null;

                if (s == null)
                {
                    view.Bind("", null, GetHotkey(i));
                    view.SetInteractable(false);
                    view.SetCooldown(0f, 0);
                    view.button.onClick.RemoveAllListeners();
                }
                else
                {
                    view.Bind(s.skillID, s.icon, GetHotkey(i));
                    view.SetInteractable(true);
                    
                    view.button.onClick.RemoveAllListeners();
                    view.button.onClick.AddListener(() =>
                    {
                        RequestSkill(s);
                    });
                }
            }
        }

        void EnsureSlotViews()
        {
            // 清空/补足
            _slots.Clear();
            for (int i = slotsRoot.childCount - 1; i >= 0; --i)
                Destroy(slotsRoot.GetChild(i).gameObject);

            for (int i = 0; i < totalSlots; i++)
            {
                var go = Instantiate(slotPrefab, slotsRoot);
                var view = go.GetComponent<SkillSlotView>();
                _slots.Add(view);
            }
        }

        // ---------- 刷新（冷却/可交互） ----------
        void Update()
        {
            if (!HasActive) return;
            if (_active != null && _lastSkillsRevision != _active.SkillsRevision)
            {
                _lastSkillsRevision = _active.SkillsRevision;
                BuildBar();
            }
            Refresh();
        }

        void Refresh()
        {
            if (_active == null) return;

            for (int i = 0; i < totalSlots; i++)
            {
                var view = _slots[i];
                if (string.IsNullOrEmpty(view.skillId))
                {
                    view.SetInteractable(false);
                    view.SetCooldown(0f, 0);
                    continue;
                }

                if (!_byId.TryGetValue(view.skillId, out var def))
                {
                    view.SetInteractable(false);
                    view.SetCooldown(0f, 0);
                    continue;
                }

                bool onCd = _active.IsOnCooldown(def);
                int uiRounds = _active.GetUiRounds(def);
                view.SetCooldown(onCd ? 1f : 0f, uiRounds);
                view.SetInteractable(!onCd);
            }
        }

        string GetHotkey(int i) => (hotkeys != null && i < hotkeys.Length) ? hotkeys[i] : "";
        void RequestSkill(SkillDefinition skill)
        {
            if (_active == null || combat == null || skill == null)
                return;

            targetingController ??= FindFirstObjectByTypeSafe<SkillTargetingController>();
            if (targetingController)
            {
                if (targetingController.BeginSkillSelection(_active, skill))
                    return;
            }

            combat.ExecuteSkill(_active, skill, _active);
        }

        // ---------- 规则挑选 ----------
        static void PickSkillsFor(Unit u, string moveTag, int slotCount, List<SkillDefinition> orderedOut,
                                 Dictionary<string, SkillDefinition> byIdOut)
        {
            orderedOut.Clear();
            byIdOut.Clear();

            if (u == null)
                return;

            slotCount = Mathf.Max(0, slotCount);
            if (slotCount == 0)
                return;

            // 职业技能：优先用 Unit.Skills，否则数据库查 ClassId
            var all = (u.Skills != null && u.Skills.Count > 0)
                ? new List<SkillDefinition>(u.Skills.Where(s => s != null))
                : new List<SkillDefinition>(SkillDatabase.GetSkillsForClass(u.ClassId));

            if (all.Count == 0)
                return;

            bool IsUsable(SkillDefinition skill)
            {
                if (skill == null || string.IsNullOrEmpty(skill.skillID))
                    return false;
                return skill.skillType == SkillType.Active || skill.skillType == SkillType.Mastery;
            }

            var available = new List<SkillDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in all)
            {
                if (!IsUsable(skill))
                    continue;
                if (seen.Contains(skill.skillID))
                    continue;
                seen.Add(skill.skillID);
                available.Add(skill);
            }

            if (available.Count == 0)
                return;

            int GetNumericId(SkillDefinition s)
            {
                if (s == null || string.IsNullOrEmpty(s.skillID))
                    return 0;
                int value = 0;
                foreach (char c in s.skillID)
                {
                    if (char.IsDigit(c))
                        value = value * 10 + (c - '0');
                }
                return value;
            }

            bool MatchesMoveSkill(SkillDefinition skill)
            {
                if (skill == null)
                    return false;
                if (!string.IsNullOrWhiteSpace(moveTag))
                {
                    if (string.Equals(skill.skillTag, moveTag, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (skill.tags != null && skill.tags.Exists(t => string.Equals(t, moveTag, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                if (string.Equals(skill.skillName, "Move", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(skill.skillID, "SK013", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (skill.effects != null && skill.effects.Exists(e => e != null && e.effectType == EffectType.Move))
                    return true;
                return false;
            }

            // 数字序号解析（SK123 -> 123；兜底为 0）
            SkillDefinition SelectCandidate(Func<SkillDefinition, bool> predicate, bool descending = false)
            {
                var candidates = available.Where(predicate).ToList();
                if (candidates.Count == 0)
                    return null;
                return descending
                    ? candidates.OrderByDescending(GetNumericId).First()
                    : candidates.OrderBy(GetNumericId).First();
            }

            void PushSkill(SkillDefinition s)
            {
                if (s == null || orderedOut.Count >= slotCount)
                    return;
                if (string.IsNullOrEmpty(s.skillID))
                    return;
                if (byIdOut.ContainsKey(s.skillID))
                    return;
                orderedOut.Add(s);
                byIdOut[s.skillID] = s;
                available.Remove(s);
            }
            PushSkill(SelectCandidate(MatchesMoveSkill));
            PushSkill(SelectCandidate(_ => true, descending: true));
            PushSkill(SelectCandidate(s => s.skillType == SkillType.Mastery));

            var colorOrder = new[]
            {
                SkillColor.DeepBlue,
                SkillColor.DarkYellow,
                SkillColor.Green,
                SkillColor.Purple,
                SkillColor.LightBlue
            };

            foreach (var color in colorOrder)
                PushSkill(SelectCandidate(s => s.skillColor == color));

            var reds = available.Where(s => s.skillColor == SkillColor.Red)
                                 .OrderBy(GetNumericId)
                                 .Take(2)
                                 .ToList();
            foreach (var red in reds)
                PushSkill(red);

            while (orderedOut.Count < slotCount)
                orderedOut.Add(null);
        }
    }
}
