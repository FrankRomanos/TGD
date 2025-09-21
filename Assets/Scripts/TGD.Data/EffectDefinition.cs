using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using static UnityEngine.UI.CanvasScaler;
namespace TGD.Data
{
    public enum TargetType
    {
        Self,
        Enemy,
        Allies,
        All
    }
    public enum AttributeType
    {
        Attack,
        Armor,
        Speed,
        Movement,
        CritRate,
        Mastery,
        Healing,
        ArmorPenetration,
        DamageReduction, 
        Strength,
        Agility,
        DamageIncrease,
        CritDamage,
        Threat,
        ThreatShred
    }

    public enum EffectType
    {
        None = 0,
        Damage = 1,
        Heal = 2,
        GainResource = 3,
        ScalingBuff = 4,      // ✅ 每点资源提升属性
        ModifyStatus = 6,
        ConditionalEffect = 7,
        ModifySkill = 8,
        ReplaceSkill = 9,
        Move = 10,                // 统一的技能调整入口
        ModifyAction = 11,
        CooldownModifier = 12,
        ModifyDamageSchool = 13,
        AttributeModifier = 14,
        MasteryPosture = 15,
        RandomOutcome = 16,
        Repeat = 17,
        ProbabilityModifier = 18,
        DotHotModifier = 19,
        ModifyResource = 20,
        Aura = 21,
        ModifyDefence = 22
    }
    public static class EffectTypeLegacy
    {
        public const int ApplyStatus = 5;
    }
    public enum CooldownTargetScope
    {
        Self,
        All,
        ExceptRed
    }
    

    public enum DamageSchool 
    { 
        Physical, 
        Magical, 
        True,
        Poison,
        Bleed,
        Fire,
        Frost
    }
    public enum DefenceModificationMode
    {
        Shield,
        DamageRedirect,
        Reflect,
        Immunity
    }

    [Serializable]
    public class DamageSchoolValueEntry
    {
        public DamageSchool school = DamageSchool.Physical;
        public string valueExpression;
        public float value;
    }

    [Serializable]
    public enum EffectCondition
    {
        None,
        AfterAttack,
        OnPerformAttack,
        OnPerformHeal,
        OnCriticalHit,
        OnCooldownEnd,
        AfterSkillUse,
        SkillStateActive,
        OnDotHotActive,
        OnNextSkillSpendResource,
        OnDamageTaken,
        OnEffectEnd,
        OnTurnBeginSelf,
        OnTurnBeginEnemy,
        OnTurnEndSelf,
        OnTurnEndEnemy
    }

    [Serializable]
    public enum ResourceType
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
        posture
    }
    public enum ResourceModifyType
    {
        Gain,
        ConvertMax,
        Lock,
        Overdraft,
        PayLate
    }

    public enum AuraEffectCategory
    {
        Damage,
        Heal,
        Buff,
        Debuff
    }
    public enum AuraRangeMode
    {
        Within,
        Between
    }

    [Serializable]
    public enum CompareOp
    {
        Equal,
        Greater,
        GreaterEqual,
        Less,
        LessEqual
    }
    [Serializable]
    public enum ConditionTarget
    {
        Caster,
        PrimaryTarget,
        SecondaryTarget,
        Any
    }

    [Serializable]
    public enum ScalingAttribute
    {
        Attack,
        Crit,
        Armor,
        HP,
        Speed,
        MoveSpeed
    }
    public enum ModifierType
    {
        Percentage,  // % 提升
        Flat         // 固定值
    }
    public enum ActionModifyType
    {
        None,
        Damage,
        ActionType
    }
    public enum DamageSchoolModifyType
    {
        Damage,
        DamageSchoolType
    }

    public enum SkillModifyType
    {
        None,
        Range,
        CooldownModify,
        CooldownReset,
        TimeCost,
        Damage,
        Heal,
        ResourceCost,
        Duration,
        AddCost,
        ForbidUse,
        BuffPower
    }

    public enum SkillModifyOperation
    {
        Minus,
        Override,
        Multiply
    }
    public enum StatusModifyType
    {
        ApplyStatus,
        ReplaceStatus,
        DeleteStatus
    }

    public enum MoveSubject
    {
        Caster,
        PrimaryTarget,
        SecondaryTarget
    }

    public enum MoveDirection
    {
        Forward,
        Backward,
        Left,
        Right,
        TowardTarget,
        AwayFromTarget,
        AbsoluteOffset
    }

    public enum MoveExecution
    {
        Step,
        Dash,
        Teleport,
        Pull,
        Push,
        SwapPositions
    }

    [System.Flags]
    public enum EffectFieldMask
    {
        None = 0,
        Probability = 1 << 0,  // 概率
        Duration = 1 << 1,  // 持续（回合）
        Target = 1 << 2,  // 作用目标
        Condition = 1 << 3,  // 触发条件
        Crit = 1 << 4,  // 可暴击
        School = 1 << 5,  // 伤害学派（仅 Damage 用）
        PerLevel = 1 << 6,  // 等级分段编辑开关
        Stacks = 1 << 7,    // Buff 层数
    }
    public enum ImmunityScope
    {
        All,
        DamageOnly,
        OnlySkill
    }

    public enum ProbabilityModifierMode
    {
        None,
        DoubleRollBest,
        DoubleRollWorst
    }

    public enum RepeatCountSource
    {
        Fixed,
        Expression,
        ResourceValue,
        ResourceSpent
    }

    public enum DotHotOperation
    {
        TriggerDots = 0,
        TriggerHots = 1,
        ConvertDamageToDot = 2,
        None = 3
    }

    [Serializable]
    public class RandomOutcomeEntry
    {
        public string label;
        public string description;
        public int weight = 1;
        public ProbabilityModifierMode probabilityMode = ProbabilityModifierMode.None;
        [SerializeReference]
        public List<EffectDefinition> effects = new List<EffectDefinition>();
    }

    [Serializable]
    public class EffectDefinition
    {
        public EffectType effectType = EffectType.None;

        // ===== 通用字段 =====
        public TargetType target = TargetType.Self;
        public AttributeType attributeType;
        public ImmunityScope immunityScope = ImmunityScope.All;
        public ActionType targetActionType;  // ✅ 直接用已有的 ActionType
        public ModifierType modifierType;
        public ActionModifyType actionModifyType = ActionModifyType.None;
        public ActionType actionTypeOverride = ActionType.None;
        public string actionFilterTag = string.Empty;
        public string valueExpression;
        public float value;            // Damage/Heal 等常规效果
        public float duration;         // 持续时间（回合）
        public string probability;     // 概率（字符串，允许 "p%"）

        public EffectCondition condition = EffectCondition.None;
        public EffectFieldMask visibleFields =
    EffectFieldMask.Probability |
    EffectFieldMask.Duration |
    EffectFieldMask.Target |
    EffectFieldMask.Condition |
    EffectFieldMask.PerLevel;   // 默认全开；你可按需改默认


        // —— 条件参数 ——
        public string conditionSkillStateID; // SkillStateActive: 指定需要激活的状态 skillID
        public bool conditionSkillStateCheckStacks = false;
        public CompareOp conditionSkillStateStackCompare = CompareOp.Equal;
        public int conditionSkillStateStacks = 0;
        public string conditionSkillUseID;  // AfterSkillUse: 指定触发的技能ID（空/any = 任意技能）
        public ConditionTarget conditionTarget = ConditionTarget.Any;
        public TargetType conditionDotTarget = TargetType.Enemy;
        public List<string> conditionDotSkillIDs = new();
        public bool conditionDotUseStacks = false;
        // —— 条件参数（仅当 condition == OnNextSkillSpendResource 时使用）——
        public ResourceType conditionResourceType;  // 例如 Discipline
        public int conditionMinAmount = 1;  // 最小花费（默认≥1）
        public bool consumeStatusOnTrigger = true; // 命中后是否消耗所在的状态（一般 true）

        // ====== NEW: Damage/Heal 专用的小字段（很轻量）======
        public DamageSchool damageSchool = DamageSchool.Physical; // 仅 Damage 用
        public bool canCrit = true;                                // Damage/Heal 都可用

        // —— 按等级覆盖（用于五色技能的 1~4 级）——
        public bool perLevel = false;                     // 勾上后，以下数值/概率数组生效
        public bool perLevelDuration = true;              // 控制持续回合是否按等级拆分（默认保持旧逻辑为 true）
        public string[] valueExprLevels = new string[4];  // L1~L4 的“数值/公式”，如 "atk*0.6"
        public int[] durationLevels = new int[4];     // L1~L4 的持续回合
        public string[] probabilityLvls = new string[4];  // L1~L4 的概率（"p" 或 "35"）
        public int[] stackCountLevels = new int[4];       // L1~L4 的层数
        public int stackCount = 1;

        // ===== Resource / Condition =====
        public ResourceType resourceType = ResourceType.Discipline;
        public ResourceModifyType resourceModifyType = ResourceModifyType.Gain;
        public bool resourceStateEnabled = true;
        public CompareOp compareOp = CompareOp.Equal;
        public float compareValue;
        [SerializeReference]
        public List<EffectDefinition> onSuccess = new();

        // ===== Random Outcome =====
        public int randomRollCount = 1;
        public bool randomAllowDuplicates = true;
        public List<RandomOutcomeEntry> randomOutcomes = new List<RandomOutcomeEntry>();

        // ===== Repeat Effect =====
        public RepeatCountSource repeatCountSource = RepeatCountSource.Fixed;
        public int repeatCount = 1;
        public string repeatCountExpression;
        public ResourceType repeatResourceType = ResourceType.Discipline;
        public bool repeatConsumeResource = true;
        public int repeatMaxCount = 0;
        [SerializeReference]
        public List<EffectDefinition> repeatEffects = new List<EffectDefinition>();

        // ===== Probability Modifier =====
        public ProbabilityModifierMode probabilityModifierMode = ProbabilityModifierMode.None;

        // ===== DoT / HoT Modifier =====
        public DotHotOperation dotHotOperation = DotHotOperation.TriggerDots;
        [FormerlySerializedAs("dotHotTriggerCount")]
        public int dotHotBaseTriggerCount = 0;
        public int dotHotTriggerCount = 1;
        public bool dotHotAffectsAllies = false;
        public bool dotHotAffectsEnemies = true;
        public bool dotHotShowStacks = false;
        public int dotHotMaxStacks = 1;
        [SerializeReference]
        public List<EffectDefinition> dotHotAdditionalEffects = new List<EffectDefinition>();

        // ===== Buff/Debuff =====
        public string statusSkillID;        // 传统 Buff/Debuff 用 skillID
        public StatusModifyType statusModifyType = StatusModifyType.ApplyStatus;
        public List<string> statusModifySkillIDs = new();
        public string statusModifyReplacementSkillID;
        public bool statusModifyShowStacks = false;
        public int statusModifyStacks = 0;
        public int statusModifyMaxStacks = -1;
        // ===== Skill References =====
        public string targetSkillID;        // 原技能ID/被修改技能
        public string replaceSkillID;       // 替换后技能ID
        public bool inheritReplacedCooldown = true; // 替换后是否沿用原冷却
        public int cooldownChangeSeconds = 0; // CooldownModifier: 秒数改动（可正可负）
        public CooldownTargetScope cooldownTargetScope = CooldownTargetScope.Self; // CooldownModifier: 影响范围
        // ===== Modify Skill =====
        public SkillModifyType skillModifyType = SkillModifyType.None;
        public SkillModifyOperation skillModifyOperation = SkillModifyOperation.Minus;
        public bool modifyAffectsAllCosts = true;
        public CostResourceType modifyCostResource = CostResourceType.Energy;
        public bool resetCooldownToMax = true; // ModifySkill: 冷却重置时是否刷新为全新冷却
        public bool modifyLimitEnabled = false;
        public string modifyLimitExpression;
        public float modifyLimitValue;

        // ===== Modify Damage School =====
        public DamageSchoolModifyType damageSchoolModifyType = DamageSchoolModifyType.Damage;
        public bool damageSchoolFilterEnabled = false;
        public DamageSchool damageSchoolFilter = DamageSchool.Physical;

        // ===== Move Effect =====
        public MoveSubject moveSubject = MoveSubject.Caster;
        public MoveExecution moveExecution = MoveExecution.Step;
        public MoveDirection moveDirection = MoveDirection.Forward;
        public int moveDistance = 1;
        public int moveMaxDistance = 0;
        public Vector2Int moveOffset = Vector2Int.zero;
        public bool forceMovement = true;
        public bool allowPartialMove = false;
        public bool moveIgnoreObstacles = false;
        public bool moveStopAdjacentToTarget = true;

        // ===== Aura =====
        public AuraRangeMode auraRangeMode = AuraRangeMode.Within;
        public float auraRadius = 0f;
        public float auraMinRadius = 0f;
        public float auraMaxRadius = 0f;
        public AuraEffectCategory auraCategories = AuraEffectCategory.Damage;
        public TargetType auraTarget = TargetType.Allies;
        public bool auraAffectsImmune = false;
        public int auraDuration = 0;
        public EffectCondition auraOnEnter = EffectCondition.None;
        public EffectCondition auraOnExit = EffectCondition.None;
        public int auraHeartSeconds = 6;
        [SerializeReference]
        public List<EffectDefinition> auraAdditionalEffects = new();

        // ===== Modify Defence =====
        public DefenceModificationMode defenceMode = DefenceModificationMode.Shield;
        public bool defenceShieldUsePerSchool = false;
        public List<DamageSchoolValueEntry> defenceShieldBreakdown = new();
        public float defenceShieldMaxValue = 0f;
        public string defenceShieldMaxExpression;
        public string defenceRedirectExpression;
        public float defenceRedirectRatio = 1f;
        public ConditionTarget defenceRedirectTarget = ConditionTarget.Caster;
        public bool defenceReflectUseIncomingDamage = true;
        public string defenceReflectRatioExpression;
        public float defenceReflectRatio = 0f;
        public string defenceReflectFlatExpression;
        public float defenceReflectFlatDamage = 0f;
        public DamageSchool defenceReflectDamageSchool = DamageSchool.Physical;
        public List<string> defenceImmuneSkillIDs = new();

        // ===== ScalingBuff 专用 =====
        public string scalingValuePerResource;     // e.g. "p%", "0.2*Mastery"
        public int maxStacks = 0;                  // 0 = unlimited (shared by effects that support stacking caps)
        public ScalingAttribute scalingAttribute = ScalingAttribute.Attack;
        public SkillModifyOperation scalingOperation = SkillModifyOperation.Minus;

        // ===== Mastery: Posture Engine =====
        public MasteryPostureSettings masteryPosture = new MasteryPostureSettings();

        // —— 解析当前技能等级应使用的表达式/持续/概率 ——
        // 注意：这里返回的是 string/int/string，表达式留给你的公式求值器去算
        public string ResolveValueExpression(SkillDefinition skill)
        {
            if (perLevel && valueExprLevels != null && valueExprLevels.Length >= 4)
            {
                int idx = Mathf.Clamp(skill.skillLevel - 1, 0, 3);
                var s = valueExprLevels[idx];
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return valueExpression; // 回退到通用单值
        }

        public int ResolveDuration(SkillDefinition skill)
        {
            bool followSkillDuration = (visibleFields & EffectFieldMask.Duration) == 0;
            if (followSkillDuration && skill != null)
                return skill.ResolveDuration();

            int level = skill != null ? skill.skillLevel : 1;

            if (ShouldUsePerLevelDuration() && durationLevels != null && durationLevels.Length >= 4)
            {
                int idx = Mathf.Clamp(level - 1, 0, 3);
                if (durationLevels[idx] != 0) return durationLevels[idx];
            }
            return (int)duration; // fallback
        }

        public string ResolveProbability(SkillDefinition skill)
        {
            if (perLevel && probabilityLvls != null && probabilityLvls.Length >= 4)
            {
                int idx = Mathf.Clamp(skill.skillLevel - 1, 0, 3);
                var s = probabilityLvls[idx];
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return probability; // 回退
        }
        /// <summary>
        /// 兼容旧数据：若显式关闭 <see cref="perLevelDuration"/> 则不再读取等级数组。
        /// </summary>
        private bool ShouldUsePerLevelDuration()
        {
            if (perLevelDuration)
                return true;

            if (!perLevel || durationLevels == null)
                return false;

            for (int i = 0; i < durationLevels.Length; i++)
            {
                if (durationLevels[i] != 0)
                    return true;
            }

            return false;
        }

    }
}
