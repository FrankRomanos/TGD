using System;
using System.Collections.Generic;
using UnityEngine;
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
        Shield,
        Speed,
        Movement,
        CritRate,
        Mastery,
        Healing,
        ArmorPenetration,
        DamageReduction
    }

    public enum EffectType
    {
        None,
        Damage,
        Heal,
        GainResource,
        ScalingBuff,      // ✅ 每点资源提升属性
        ApplyStatus,      // Buff/Debuff（skillID 状态）
        ConditionalEffect,
        ModifySkill,
        ReplaceSkill,
        Move,                // 统一的技能调整入口
        ModifyAction,
        CooldownModifier,
        AttributeModifier,
        MasteryPosture
    }
    public enum CooldownTargetScope
    {
        Self,
        All,
        ExceptRed
    }


    public enum DamageSchool { Physical, Magical, True }
    [Serializable]
    public enum EffectCondition
    {
        None,
        AfterAttack,
        OnCriticalHit,
        OnCooldownEnd,
        AfterSkillUse,
        SkillStateActive,
        // 新增：下一次消耗指定资源时触发（当前/下一次皆可命中）
        OnNextSkillSpendResource,
        OnDamageTaken,
        OnEffectEnd
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
    public enum ScalingAttribute
    {
        Attack,
        Crit,
        Armor,
        HP,
        Speed,
        MoveSpeed
        // 后续可以继续扩展
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

    public enum SkillModifyType
    {
        None,
        Range,
        CooldownModify,
        CooldownReset,
        TimeCost,
        Damage,
        Heal,
        ResourceCost
    }

    public enum SkillModifyOperation
    {
        Add,
        Override,
        Multiply
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
        Teleport
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


    [Serializable]
    public class EffectDefinition
    {
        public EffectType effectType = EffectType.None;

        // ===== 通用字段 =====
        public TargetType target = TargetType.Self;
        public AttributeType attributeType;
        public ActionType targetActionType;  // ✅ 直接用已有的 ActionType
        public ModifierType modifierType;
        public ActionModifyType actionModifyType = ActionModifyType.None;
        public ActionType actionTypeOverride = ActionType.None;
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
        public string conditionSkillUseID;  // AfterSkillUse: 指定触发的技能ID（空/any = 任意技能）
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
        public CompareOp compareOp = CompareOp.Equal;
        public float compareValue;
        public List<EffectDefinition> onSuccess = new();



        // ===== Buff/Debuff =====
        public string statusSkillID;        // 传统 Buff/Debuff 用 skillID
        // ===== Skill References =====
        public string targetSkillID;        // 原技能ID/被修改技能
        public string replaceSkillID;       // 替换后技能ID
        public bool inheritReplacedCooldown = true; // 替换后是否沿用原冷却
        public int cooldownChangeSeconds = 0; // CooldownModifier: 秒数改动（可正可负）
        public CooldownTargetScope cooldownTargetScope = CooldownTargetScope.Self; // CooldownModifier: 影响范围
        // ===== Modify Skill =====
        public SkillModifyType skillModifyType = SkillModifyType.None;
        public SkillModifyOperation skillModifyOperation = SkillModifyOperation.Add;
        public bool modifyAffectsAllCosts = true;
        public CostResourceType modifyCostResource = CostResourceType.Energy;
        public bool resetCooldownToMax = true; // ModifySkill: 冷却重置时是否刷新为全新冷却

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

        // ===== ScalingBuff 专用 =====
        public string scalingValuePerResource;     // e.g. "p%", "0.2*Mastery"
        public int maxStacks = 0;                  // 0 = unlimited
        public ScalingAttribute scalingAttribute = ScalingAttribute.Attack;

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
