// Assets/Scripts/TGD.UI/SkillBarController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TGD.Combat;
using TGD.Data;

namespace TGD.UI
{
    /// <summary>
        [Header("Skill Ordering")]
        public string moveSkillTag = "Move";

        [Header("Targeting")]
        public SkillTargetingController targetingController;

            targetingController ??= FindFirstObjectByTypeSafe<SkillTargetingController>();
            targetingController?.CancelSelection();
            targetingController?.CancelSelection();
            PickSkillsFor(_active, moveSkillTag, totalSlots, _ordered, _byId);
                        RequestSkill(s);
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

        // 运行态
        Unit _active;
        readonly List<SkillSlotView> _slots = new();
        readonly List<SkillDefinition> _ordered = new();
        readonly Dictionary<string, SkillDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        bool HasActive => _active != null;
        protected override void HandleTurnBegan(Unit u)
        {
            _active = u;
            BuildBar();
            Refresh();
            gameObject.SetActive(true);
        }

        protected override void HandleTurnEnded(Unit u)
        {
            gameObject.SetActive(false);
            _active = null;
        }

        // ---------- 构建 ----------
        void BuildBar()
        {
            EnsureSlotViews();
            PickSkillsFor(_active, _ordered, _byId);

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
                    int idx = i;   // 闭包捕获
                    view.button.onClick.RemoveAllListeners();
                    view.button.onClick.AddListener(() =>
                    {
                        if (_active == null || combat == null) return;
                        combat.ExecuteSkill(_active, s.skillID, _active); // 先对自己，等你接目标系统
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
      
            // 然后
            if (!HasActive) return;
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

                return;
            }

            combat.ExecuteSkill(_active, skill, _active);
        }

        static void PickSkillsFor(Unit u, string moveTag, int slotCount, List<SkillDefinition> orderedOut,
            if (u == null)
                return;

            slotCount = Mathf.Max(0, slotCount);
            if (slotCount == 0)
                return;
            if (all.Count == 0)
                return;

            var available = new List<SkillDefinition>(all);

            bool HasMoveTag(SkillDefinition skill)
                => skill != null && !string.IsNullOrWhiteSpace(moveTag) &&
                   (string.Equals(skill.skillTag, moveTag, System.StringComparison.OrdinalIgnoreCase) ||
                    (skill.tags != null && skill.tags.Exists(t => string.Equals(t, moveTag, System.StringComparison.OrdinalIgnoreCase))));

            void Push(SkillDefinition s)
            {
                if (s == null || orderedOut.Count >= slotCount) return;
                if (string.IsNullOrEmpty(s.skillID)) return;
                if (byIdOut.ContainsKey(s.skillID)) return;
                orderedOut.Add(s);
                byIdOut[s.skillID] = s;
                available.Remove(s);
            }

            if (!string.IsNullOrWhiteSpace(moveTag))
            {
                var taggedMove = available.FirstOrDefault(HasMoveTag);
                if (taggedMove != null)
                    Push(taggedMove);
            }
            SkillDefinition FirstByColor(SkillColor col)
                => available.Where(s => s != null && s.skillColor == col)
                            .OrderBy(Num)
                            .FirstOrDefault();
                => available.Where(s => s != null && s.skillColor == SkillColor.Red)
                            .OrderBy(Num);
            var byIdDesc = available.OrderByDescending(Num).ToList();
            Push(move);

            byIdDesc = available.OrderByDescending(Num).ToList();
            var basic = byIdDesc.ElementAtOrDefault(0);
            var mastery = available.OrderBy(Num).FirstOrDefault();
            foreach (var extra in available.OrderBy(Num).ToList())
            {
                if (orderedOut.Count >= slotCount) break;
                Push(extra);
            }

            while (orderedOut.Count < slotCount)
                orderedOut.Add(null);
                      .FirstOrDefault();

            IEnumerable<SkillDefinition> Reds()
                => all.Where(s => s != null && s.skillColor == SkillColor.Red)
                      .OrderBy(s => Num(s));

            // 1 移动 / 2 普攻：ID 最大的两个
            var byIdDesc = all.OrderByDescending(Num).ToList();
            var move = byIdDesc.ElementAtOrDefault(0);
            var basic = byIdDesc.ElementAtOrDefault(1);

            // 3 精通：ID 最小
            var mastery = all.OrderBy(Num).FirstOrDefault();

            // 4~8 五色（按你给的顺序）
            var deepBlue = FirstByColor(SkillColor.DeepBlue);
            var darkYellow = FirstByColor(SkillColor.DarkYellow);
            var green = FirstByColor(SkillColor.Green);
            var lightBlue = FirstByColor(SkillColor.LightBlue);
            var purple = FirstByColor(SkillColor.Purple);

            // 9~10 红（大招 / 第二个红）
            var reds = Reds().ToList();
            var red1 = reds.ElementAtOrDefault(0);
            var red2 = reds.ElementAtOrDefault(1);

            // 去重并写入
            void Push(SkillDefinition s)
            {
                if (s == null) return;
                if (byIdOut.ContainsKey(s.skillID)) return;
                orderedOut.Add(s);
                byIdOut[s.skillID] = s;
            }

            Push(move);
            Push(basic);
            Push(mastery);
            Push(deepBlue);
            Push(darkYellow);
            Push(green);
            Push(lightBlue);
            Push(purple);
            Push(red1);
            Push(red2);

            // 不足 10 用 null 占位，外层会置灰
            while (orderedOut.Count < 10) orderedOut.Add(null);
        }
    }
}
