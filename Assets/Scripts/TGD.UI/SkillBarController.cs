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
    /// 10格技能条：
    /// 1 Move（skillID = Move，全职业共用）
    /// 2 普攻（Class 内编号最大）
    /// 3 Class 技能中编号最小
    /// 4~8 按颜色：DeepBlue、DarkYellow、Green、LightBlue、Purple（每种颜色只取一个）
    /// 9~10 Red：优先取最高编号作为大招，再取第二个（多为治疗）
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
            if (!ReferenceEquals(_active, u))
                return;

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
        static void PickSkillsFor(Unit u, string moveId, int slotCount, List<SkillDefinition> orderedOut,
                                 Dictionary<string, SkillDefinition> byIdOut)
        {
            orderedOut.Clear();
            byIdOut.Clear();

            if (u == null)
                return;

            slotCount = Mathf.Max(0, slotCount);
            if (slotCount == 0)
                return;

            var pool = BuildInitialPool(u);
            if (pool.Count == 0)
                return;

            var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Push(SkillDefinition skill)
            {
                if (skill == null || orderedOut.Count >= slotCount)
                    return;
                if (string.IsNullOrEmpty(skill.skillID))
                    return;
                if (!picked.Add(skill.skillID))
                    return;

                orderedOut.Add(skill);
                byIdOut[skill.skillID] = skill;
                RemoveFromPool(skill);
            }

            SkillDefinition moveSkill = ResolveMoveSkill(moveId);
            Push(moveSkill);

            Push(TakeFromPool(s => !IsMoveSkill(s), descending: true));
            Push(TakeFromPool(s => !IsMoveSkill(s), descending: false));

            var colorOrder = new[]
            {
                SkillColor.DeepBlue,
                SkillColor.DarkYellow,
                SkillColor.Green,
                SkillColor.LightBlue,
                SkillColor.Purple
            };

            foreach (var color in colorOrder)
                Push(TakeColor(color));

            Push(TakeColor(SkillColor.Red, preferHighest: true));
            Push(TakeColor(SkillColor.Red, preferHighest: true));

            while (orderedOut.Count < slotCount)
                orderedOut.Add(null);

            List<SkillDefinition> BuildInitialPool(Unit unit)
            {
                var list = new List<SkillDefinition>();
                IReadOnlyList<SkillDefinition> source = SkillDatabase.GetSkillsForClass(unit?.ClassId);

                if (source == null || source.Count == 0)
                {
                    if (unit?.Skills != null)
                        source = unit.Skills;
                }

                if (source == null)
                    return list;

                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var skill in source)
                {
                    if (!IsEligible(skill))
                        continue;
                    if (!seenIds.Add(skill.skillID))
                        continue;
                    list.Add(skill);
                }
                return list;
            }

            SkillDefinition ResolveMoveSkill(string requestedId)
            {
                SkillDefinition candidate = pool.FirstOrDefault(IsMoveSkill);
                if (candidate != null)
                    return candidate;

                if (!string.IsNullOrWhiteSpace(requestedId))
                {
                    candidate = SkillDatabase.GetSkillById(requestedId);
                    if (IsEligible(candidate) && IsMoveSkill(candidate))
                        return candidate;
                }

                foreach (var skill in SkillDatabase.GetAllSkills())
                {
                    if (IsEligible(skill) && IsMoveSkill(skill))
                        return skill;
                }

                return null;
            }

            SkillDefinition TakeFromPool(Func<SkillDefinition, bool> predicate, bool descending)
            {
                SkillDefinition best = null;
                int bestValue = descending ? int.MinValue : int.MaxValue;

                foreach (var skill in pool)
                {
                    if (skill == null || !predicate(skill))
                        continue;

                    int value = GetNumericId(skill);
                    if (best == null || (descending ? value > bestValue : value < bestValue))
                    {
                        best = skill;
                        bestValue = value;
                    }
                }

                if (best != null)
                    RemoveFromPool(best);

                return best;
            }

            SkillDefinition TakeColor(SkillColor color, bool preferHighest = false)
            {
                return TakeFromPool(s => s.skillColor == color && !IsMoveSkill(s), descending: preferHighest);
            }

            void RemoveFromPool(SkillDefinition pickedSkill)
            {
                if (pickedSkill == null)
                    return;

                for (int index = pool.Count - 1; index >= 0; index--)
                {
                    var candidate = pool[index];
                    if (candidate == null)
                        continue;

                    bool sameId = string.Equals(candidate.skillID, pickedSkill.skillID, StringComparison.OrdinalIgnoreCase);
                    bool sameModule = !string.IsNullOrWhiteSpace(pickedSkill.moduleID) &&
                                      !string.IsNullOrWhiteSpace(candidate.moduleID) &&
                                      string.Equals(candidate.moduleID, pickedSkill.moduleID, StringComparison.OrdinalIgnoreCase) &&
                                      candidate.skillColor == pickedSkill.skillColor;

                    if (sameId || sameModule)
                        pool.RemoveAt(index);
                }
            }

            bool IsEligible(SkillDefinition skill)
            {
                if (skill == null || string.IsNullOrEmpty(skill.skillID))
                    return false;
                if (skill.skillType != SkillType.Active && skill.skillType != SkillType.Mastery)
                    return false;

                string id = skill.skillID;
                if (id.IndexOf("_STANCE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (id.EndsWith("_S", StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }

            bool IsMoveSkill(SkillDefinition skill)
            {
                if (skill == null)
                    return false;

                if (!string.IsNullOrWhiteSpace(moveId) &&
                    string.Equals(skill.skillID, moveId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrWhiteSpace(moveId) &&
                    string.Equals(skill.skillName, moveId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrWhiteSpace(skill.skillTag) &&
                    string.Equals(skill.skillTag, moveId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (skill.tags != null && skill.tags.Exists(t => string.Equals(t, moveId, StringComparison.OrdinalIgnoreCase)))
                    return true;

                return string.Equals(skill.skillName, "Move", StringComparison.OrdinalIgnoreCase);
            }

            int GetNumericId(SkillDefinition skill)
            {
                if (skill == null || string.IsNullOrEmpty(skill.skillID))
                    return 0;

                int value = 0;
                foreach (char c in skill.skillID)
                {
                    if (char.IsDigit(c))
                        value = value * 10 + (c - '0');
                }

                return value;
            }
        }
    }
}
