using System.Collections.Generic;
using UnityEngine;

namespace TGD.Data
{
    public enum CostResourceType
    {
        Energy, Discipline, Rage, Versatility, Gunpowder, Qi, Vision, Custom
    }

    [System.Serializable]
    public class SkillCost
    {
        public CostResourceType resourceType = CostResourceType.Energy;
        public int amount = 0;
    }

    // 技能颜色
    public enum SkillColor
    {
        DeepBlue,   // 深蓝
        DarkYellow, // 土黄
        Green,      // 绿色
        Purple,     // 紫色
        LightBlue,  // 淡蓝
        Red,        // 终极技能（不分级）
        None        // 无色（Buff/状态等）
    }

    public enum SkillType { Active, Passive, Mastery, State, None }
    public enum SkillTargetType { Single, AOE, Self, Line, Cone, None }

    [CreateAssetMenu(fileName = "SkillDefinition", menuName = "RPG/SkillDefinition")]
    public class SkillDefinition : ScriptableObject
    {
        public string skillID;
        public string skillName;
        public Sprite icon;
        public string classID;
        public string moduleID;
        public string variantKey;
        public string chainNextID;
        public bool resetOnTurnEnd;

        public SkillType skillType = SkillType.Active;

        public ActionType actionType = ActionType.None;
        public SkillTargetType targetType = SkillTargetType.None;

        public List<SkillCost> costs = new();

        public int timeCostSeconds = 0;
        public int cooldownSeconds = 0;
        public int cooldownRounds = 0; // 由 RecalculateDerived 计算
        public int range = 0;
        public float threat;
        public float shredMultiplier;
        public string namekey;
        public string descriptionKey;

        // === 新版：只保留颜色 & 当前等级 ===
        public SkillColor skillColor = SkillColor.None;
        [Range(1, 4)]
        public int skillLevel = 1; // 仅标记“当前等级”；真正的每级数值在 EffectDefinition 中 perLevel 填写

        // 技能效果
        public List<EffectDefinition> effects = new List<EffectDefinition>();

        public void RecalculateDerived(int baseTurnTimeSeconds = 6)
        {
            if (baseTurnTimeSeconds <= 0) baseTurnTimeSeconds = 6;
            cooldownRounds = Mathf.CeilToInt((float)cooldownSeconds / baseTurnTimeSeconds);
        }
    }
}

