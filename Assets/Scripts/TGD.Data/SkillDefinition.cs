using System.Collections.Generic;
using UnityEngine;

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
        public CostResourceType resourceType = CostResourceType.Energy;
        public int amount = 0;
    }
    [System.Serializable]
    public enum SkillCostConditionType
    {
        Resource,
        Distance,
        PerformHeal,
        PerformAttack
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


    // ������ɫ
    public enum SkillColor
    {
        DeepBlue,   // ����
        DarkYellow, // ����
        Green,      // ��ɫ
        Purple,     // ��ɫ
        LightBlue,  // ����
        Red,        // �ռ����ܣ����ּ���
        None        // ��ɫ��Buff/״̬�ȣ�
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
        public List<SkillUseCondition> useConditions = new();
        public int timeCostSeconds = 0;
        public int cooldownSeconds = 0;
        public int cooldownTurns = 0; // �� RecalculateDerived ����
        public int range = 0;
        public float threat;
        public float shredMultiplier;
        public string namekey;
        public string descriptionKey;

        // === �°棺ֻ������ɫ & ��ǰ�ȼ� ===
        public SkillColor skillColor = SkillColor.None;
        [Range(1, 4)]
        public int skillLevel = 1; // ����ǡ���ǰ�ȼ�����������ÿ����ֵ�� EffectDefinition �� perLevel ��д
        public SkillDurationSettings skillDuration = new SkillDurationSettings();
        private void OnValidate()
        {
            if (skillDuration == null)
                skillDuration = new SkillDurationSettings();
        }


        // ����Ч��
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

