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
    /// 10�������������ʱֱ�Ӷ��Լ��ͷţ��������̣���
    /// 1 �ƶ���Class������ skillID��
    /// 2 �չ����ڶ���
    /// 3 ��ͨ����С��
    /// 4 DeepBlue
    /// 5 DarkYellow
    /// 6 Green
    /// 7 LightBlue
    /// 8 Purple
    /// 9 Red�����У�
    /// 10 Red���ڶ����죬�����ࣩ
    /// </summary>
    public sealed class SkillBarController : BaseTurnUiBehaviour
    {
        [Header("UI")]
        public Transform slotsRoot;         // HorizontalLayoutGroup ����
        public GameObject slotPrefab;       // �����õ� UI_SkillSlot Ԥ��
        public int totalSlots = 10;

        [Header("Hotkeys (optional)")]
        public string[] hotkeys = { "Q", "W", "E", "R", "A", "S", "D", "F", "G", "H" };
        [Header("Skill Ordering")]
        public string moveSkillTag = "Move";

        [Header("Targeting")]
        public SkillTargetingController targetingController;


        // ����̬
        Unit _active;
        readonly List<SkillSlotView> _slots = new();
        readonly List<SkillDefinition> _ordered = new();
        readonly Dictionary<string, SkillDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        bool HasActive => _active != null;
        protected override void HandleTurnBegan(Unit u)
        {
            _active = u;
            targetingController ??= FindFirstObjectByTypeSafe<SkillTargetingController>();
            targetingController?.CancelSelection();
            BuildBar();
            Refresh();
            gameObject.SetActive(true);
        }

        protected override void HandleTurnEnded(Unit u)
        {
            targetingController?.CancelSelection();
            gameObject.SetActive(false);
            _active = null;
        }

        // ---------- ���� ----------
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
            // ���/����
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

        // ---------- ˢ�£���ȴ/�ɽ����� ----------
        void Update()
        {
      
            // Ȼ��
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

        // ---------- ������ѡ ----------
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

            // ְҵ���ܣ������� Unit.Skills���������ݿ�� ClassId
            var all = (u.Skills != null && u.Skills.Count > 0)
                ? new List<SkillDefinition>(u.Skills.Where(s => s != null))
                : new List<SkillDefinition>(SkillDatabase.GetSkillsForClass(u.ClassId));

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

            // ������Ž�����SK123 -> 123������Ϊ 0��
            int Num(SkillDefinition s)
            {
                if (s == null || string.IsNullOrEmpty(s.skillID)) return 0;
                int n = 0;
                for (int i = 0; i < s.skillID.Length; i++)
                    if (char.IsDigit(s.skillID[i])) n = n * 10 + (s.skillID[i] - '0');
                return n;
            }

            SkillDefinition FirstByColor(SkillColor col)
                => available.Where(s => s != null && s.skillColor == col)
                            .OrderBy(Num)
                            .FirstOrDefault();

            IEnumerable<SkillDefinition> Reds()
                   => available.Where(s => s != null && s.skillColor == SkillColor.Red)
                            .OrderBy(Num);

            // 1 �ƶ� / 2 �չ���ID ��������
            var byIdDesc = available.OrderByDescending(Num).ToList();
            var move = byIdDesc.ElementAtOrDefault(0);
            Push(move);

            byIdDesc = available.OrderByDescending(Num).ToList();
            var basic = byIdDesc.ElementAtOrDefault(0);

            // 3 ��ͨ��ID ��С
            var mastery = available.OrderBy(Num).FirstOrDefault();

            // 4~8 ��ɫ���������˳��
            var deepBlue = FirstByColor(SkillColor.DeepBlue);
            var darkYellow = FirstByColor(SkillColor.DarkYellow);
            var green = FirstByColor(SkillColor.Green);
            var lightBlue = FirstByColor(SkillColor.LightBlue);
            var purple = FirstByColor(SkillColor.Purple);

            // 9~10 �죨���� / �ڶ����죩
            var reds = Reds().ToList();
            var red1 = reds.ElementAtOrDefault(0);
            var red2 = reds.ElementAtOrDefault(1);

   
            Push(basic);
            Push(mastery);
            Push(deepBlue);
            Push(darkYellow);
            Push(green);
            Push(lightBlue);
            Push(purple);
            Push(red1);
            Push(red2);

            foreach (var extra in available.OrderBy(Num).ToList())
            {
                if (orderedOut.Count >= slotCount) break;
                Push(extra);
            }

            while (orderedOut.Count < slotCount)
                orderedOut.Add(null);
        }
    }
}
