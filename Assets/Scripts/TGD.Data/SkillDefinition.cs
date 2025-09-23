using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TGD.Core;

namespace TGD.Data
{
    public enum CostResourceType
    {
        HP,
        Energy,
        Discipline,
        Iron,
        Rage,
        Versatility,
        Gunpowder,
        point,
        combo,
        punch,
        qi,
        vision,
        posture,
        Custom
    }

    [System.Serializable]
    public class SkillCost
    {
        private static readonly IReadOnlyDictionary<string, float> EmptyVariables = new Dictionary<string, float>();

        public CostResourceType resourceType = CostResourceType.Energy;
        public int amount = 0;
        public string amountExpression;

        public bool HasExpression => !string.IsNullOrWhiteSpace(amountExpression);

        public float ResolveAmount(IReadOnlyDictionary<string, float> variables = null)
        {
            if (HasExpression)
            {
                var scope = variables ?? EmptyVariables;
                if (Formula.TryEvaluate(amountExpression, scope, out float value))
                    return value;

                if (float.TryParse(amountExpression, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return value;
            }

            return amount;
        }

        public string GetAmountLabel()
        {
            if (HasExpression)
                return amountExpression;

            return amount.ToString(CultureInfo.InvariantCulture);
        }
    }
    [System.Serializable]
    public enum SkillCostConditionType
    {
        Resource,
        Distance,
        PerformHeal,
        PerformAttack,
        SkillStateActive
    }

    [System.Serializable]
    public enum SkillConditionLogicOperator
    {
        And,
        Or
    }

    [System.Serializable]
    public class SkillUseCondition
    {
        public SkillCostConditionType conditionType = SkillCostConditionType.Resource;
        public ConditionTarget target = ConditionTarget.Caster;
        public ResourceType resourceType = ResourceType.Discipline;   // Resource type to evaluate
        public CompareOp compareOp = CompareOp.Equal;                  // Comparison operator
        public float compareValue = 0f;                                // Threshold value
        public string compareValueExpression;                          // Expression based threshold
        public int minDistance = 0;                                    // Minimum distance requirement
        public int maxDistance = 0;                                    // Optional maximum distance
        public bool requireLineOfSight = true;                         // Distance requires unobstructed path
        public string skillID;                                         // Skill state identifier for SkillStateActive

        public bool useSecondCondition = false;                       // Whether a secondary condition is evaluated
        public SkillConditionLogicOperator secondConditionLogic = SkillConditionLogicOperator.And; // How primary/secondary combine
        public SkillCostConditionType secondConditionType = SkillCostConditionType.Resource;
        public ConditionTarget secondTarget = ConditionTarget.Caster;
        public ResourceType secondResourceType = ResourceType.Discipline;
        public CompareOp secondCompareOp = CompareOp.Equal;
        public float secondCompareValue = 0f;
        public string secondCompareValueExpression;
        public int secondMinDistance = 0;
        public int secondMaxDistance = 0;
        public bool secondRequireLineOfSight = true;
        public string secondSkillID;
    }
    public enum StatusAccumulatorSource
    {
        DamageTaken,
        HealingTaken
    }

    public enum StatusAccumulatorContributor
    {
        CasterOnly,
        Allies,
        Any
    }

    public enum StatusAccumulatorAmount
    {
        PostMitigation
    }

    [System.Serializable]
    public class StatusAccumulatorSettings
    {
        public const string DamageVariableKey = "damage_accu";
        public const string HealVariableKey = "heal_accu";

        public bool enabled = false;
        public StatusAccumulatorSource source = StatusAccumulatorSource.DamageTaken;
        public StatusAccumulatorContributor from = StatusAccumulatorContributor.Allies;
        public StatusAccumulatorAmount amount = StatusAccumulatorAmount.PostMitigation;
        public bool includeDotHot = true;
        public DamageSchool damageSchool = DamageSchool.Physical;
        public int windowSeconds = 12;
        public string variableKey;
        public string GetVariableKey()
        {
            if (!string.IsNullOrWhiteSpace(variableKey))
                return variableKey;

            return GetVariableKey(source);
        }

        public static string GetVariableKey(StatusAccumulatorSource accumulatorSource)
        {
            return accumulatorSource == StatusAccumulatorSource.DamageTaken
                ? DamageVariableKey
                : HealVariableKey;
        }
    }

    [System.Serializable]
    public class StatusSkillMetadata
    {
        public StatusAccumulatorSettings accumulatorSettings = new StatusAccumulatorSettings();

        public void EnsureInitialized()
        {
            if (accumulatorSettings == null)
                accumulatorSettings = new StatusAccumulatorSettings();
        }
    }
    [System.Serializable]
    public class SkillDurationSettings
    {
        public int duration = 0;
        public bool perLevel = false;
        public int[] durationLevels = new int[4];

        public SkillDurationSettings()
        {
        }

        public SkillDurationSettings(SkillDurationSettings other)
        {
            if (other == null)
            {
                duration = 0;
                perLevel = false;
                durationLevels = new int[4];
                return;
            }

            duration = other.duration;
            perLevel = other.perLevel;
            durationLevels = other.durationLevels != null
                ? (int[])other.durationLevels.Clone()
                : new int[4];
        }

        public int Resolve(int level)
        {
            if (perLevel && durationLevels != null && durationLevels.Length >= 4)
            {
                int idx = Mathf.Clamp(level - 1, 0, 3);
                int value = durationLevels[idx];
                if (value != 0)
                    return value;
            }

            return duration;
        }
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
    public enum SkillTargetType
    {
        Single = 0,
        AOE = 1,
        Self = 2,
        Line = 3,
        Cone = 4,
        None = 5,
        Multi = 6
    }

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
        public string skillTag = "none";
        public List<string> tags = new();
        public SkillType skillType = SkillType.Active;
        [Tooltip("Fraction of this class mastery that converts into the shared mastery stat ('p').")]
        [Min(0f)]
        public float masteryStatConversionRatio = 1f;

        public ActionType actionType = ActionType.None;
        public SkillTargetType targetType = SkillTargetType.None;
        public int multiTargetCount = 1;
        public List<SkillCost> costs = new();
        public List<SkillUseCondition> useConditions = new();
        public int timeCostSeconds = 0;
        public int cooldownSeconds = 0;
        public int cooldownTurns = 0; // 由 RecalculateDerived 计算
        public int range = 0;
        public float threat;
        public float shredMultiplier;
        public string namekey;
        public string descriptionKey;

        // === 新版：只保留颜色 & 当前等级 ===
        public SkillColor skillColor = SkillColor.None;
        [Range(1, 4)]
        public int skillLevel = 1; // 仅标记“当前等级”；真正的每级数值在 EffectDefinition 中 perLevel 填写
        public SkillDurationSettings skillDuration = new SkillDurationSettings();
        public StatusSkillMetadata statusMetadata = new StatusSkillMetadata();
        private void OnValidate()
        {
            if (skillDuration == null)
                skillDuration = new SkillDurationSettings();
            if (statusMetadata == null)
                statusMetadata = new StatusSkillMetadata();
            else
                statusMetadata.EnsureInitialized();
            if (string.IsNullOrWhiteSpace(skillTag))
                skillTag = "none";
            if (tags == null)
                tags = new List<string>();
            else
            {
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = tags.Count - 1; i >= 0; i--)
                {
                    string value = tags[i];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        tags.RemoveAt(i);
                        continue;
                    }

                    string trimmed = value.Trim();
                    if (unique.Contains(trimmed))
                    {
                        tags.RemoveAt(i);
                        continue;
                    }

                    unique.Add(trimmed);
                    tags[i] = trimmed;
                }
            }
            if (multiTargetCount < 1)
                multiTargetCount = 1;
            if (masteryStatConversionRatio <= 0f)
                masteryStatConversionRatio = 1f;
        }


        // 技能效果
        public List<EffectDefinition> effects = new List<EffectDefinition>();

        public int ResolveDuration()
        {
            return skillDuration != null ? skillDuration.Resolve(skillLevel) : 0;
        }

        public int ResolveDuration(int level)
        {
            return skillDuration != null ? skillDuration.Resolve(level) : 0;
        }

        public void RecalculateCooldownSecondToTurn(int baseTurnTimeSeconds = 6)
        {
            if (baseTurnTimeSeconds <= 0) baseTurnTimeSeconds = 6;
            cooldownTurns = Mathf.CeilToInt((float)cooldownSeconds / baseTurnTimeSeconds);
        }
    }
}

