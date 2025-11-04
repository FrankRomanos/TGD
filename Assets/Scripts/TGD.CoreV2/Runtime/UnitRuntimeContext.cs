// File: TGD.CoreV2/UnitRuntimeContext.cs
using UnityEngine;
using TGD.CoreV2.Rules;

namespace TGD.CoreV2
{
    /// 单位运行时上下文：统一承载 Stats/Cooldown 等，并提供便捷只读访问
    [DisallowMultipleComponent]
    public sealed class UnitRuntimeContext : MonoBehaviour
    {
        [Header("Core Refs")]
        [Tooltip("该单位的所有战斗数值（序列化容器）")]
        public StatsV2 stats = new StatsV2();
        public CooldownHubV2 cooldownHub;

        [Header("Fallbacks (for tests)")]
        [Tooltip("当 stats 为空时用于测试的默认 MoveRate")]
        public float fallbackMoveRate = 5f;
        [Tooltip("当 stats 为空时用于测试的默认攻击秒数")]
        public int fallbackAttackSeconds = AttackProfileRules.DefaultSeconds;
        [Tooltip("当 stats 为空时用于测试的默认攻击能量消耗")]
        public int fallbackAttackEnergyCost = AttackProfileRules.DefaultEnergyCost;
        [Tooltip("当 stats 为空时用于测试的默认移动耗能（能量/秒）")]
        public int fallbackMoveEnergyPerSecond = MoveProfileRules.DefaultEnergyPerSecond;
        [Tooltip("当 stats 为空时用于测试的默认移动时间预算（秒）")]
        public float fallbackMoveBaseSeconds = MoveProfileRules.DefaultSeconds;
        [Tooltip("当 stats 为空时用于测试的默认移动返还阈值（秒）")]
        public float fallbackMoveRefundThresholdSeconds = MoveProfileRules.DefaultRefundThresholdSeconds;
        [Tooltip("当 stats 为空时用于测试的默认预览步数（无路径时显示）")]
        public int fallbackMoveFallbackSteps = MoveProfileRules.DefaultFallbackSteps;
        [Tooltip("当 stats 为空时用于测试的默认步数上限")]
        public int fallbackMoveStepsCap = MoveProfileRules.DefaultStepsCap;
        [Tooltip("当 stats 为空时用于测试的默认保持角度（度）")]
        public float fallbackMoveKeepDeg = MoveProfileRules.DefaultKeepDeg;
        [Tooltip("当 stats 为空时用于测试的默认转向角度（度）")]
        public float fallbackMoveTurnDeg = MoveProfileRules.DefaultTurnDeg;
        [Tooltip("当 stats 为空时用于测试的默认转向速度（度/秒）")]
        public float fallbackMoveTurnSpeedDegPerSec = MoveProfileRules.DefaultTurnSpeedDegPerSec;
        [Tooltip("当 stats 为空时用于测试的默认移动冷却（秒）")]
        public float fallbackMoveCooldownSeconds = MoveProfileRules.DefaultCooldownSeconds;
        [Tooltip("当 stats 为空时用于测试的默认移动 ActionId")]
        public string fallbackMoveActionId = MoveProfileRules.DefaultActionId;

        [Header("Runtime State")]
        [SerializeField]
        MoveRateManager _moveRates = new MoveRateManager();

        public MoveRateManager MoveRates
        {
            get
            {
                if (_moveRates == null)
                    _moveRates = new MoveRateManager();
                return _moveRates;
            }
        }

        public bool Entangled => MoveRates.IsEntangled;

        UnitRuleSet _rules = new UnitRuleSet();
        [SerializeField]
        RuleRuntimeLedger _ruleLedger = new RuleRuntimeLedger();
        public UnitRuleSet Rules { get; } = new UnitRuleSet();

        public RuleRuntimeLedger RuleLedger
        {
            get
            {
                if (_ruleLedger == null)
                    _ruleLedger = new RuleRuntimeLedger();
                return _ruleLedger;
            }
        }

        // ========= 便捷只读访问（统一入口；外部系统只读这些） =========
        // —— 移动 —— 
        public int MoveRateMin => stats != null ? MoveRateRules.ResolveMin(stats) : MoveRateRules.DefaultMinInt;
        public int MoveRateMax => stats != null ? MoveRateRules.ResolveMax(stats) : MoveRateRules.DefaultMaxInt;
        public int MoveRate => stats != null
            ? Mathf.Clamp(stats.MoveRate, MoveRateMin, MoveRateMax)
            : Mathf.Clamp(Mathf.RoundToInt(fallbackMoveRate), MoveRateRules.DefaultMinInt, MoveRateRules.DefaultMaxInt);
        // ★ 新增：基础移速（可写，写回 Stats 或 fallback）
        public int BaseMoveRate
        {
            get => stats != null
                ? Mathf.Clamp(stats.MoveRate, MoveRateMin, MoveRateMax)
                : Mathf.Clamp(Mathf.RoundToInt(fallbackMoveRate), MoveRateRules.DefaultMinInt, MoveRateRules.DefaultMaxInt);
            set
            {
                int v = Mathf.Clamp(value, MoveRateRules.DefaultMinInt, MoveRateRules.DefaultMaxInt);
                if (stats != null)
                {
                    stats.MoveRate = Mathf.Clamp(v, MoveRateMin, MoveRateMax);
                }
                else
                {
                    fallbackMoveRate = Mathf.Clamp(v, MoveRateRules.DefaultMinInt, MoveRateRules.DefaultMaxInt);
                }
                _currentMoveRate = -1f;
            }
        }
        [SerializeField]
        float _currentMoveRate = -1f;
        public float CurrentMoveRate
        {
            get
            {
                if (_currentMoveRate <= 0f)
                {
                    _currentMoveRate = StatsMathV2.MR_MultiThenFlat(
                        BaseMoveRate,
                        new[] { MoveRates.NormalizedMultiplier },
                        MoveRateFlatAdd,
                        MoveRateMin,
                        MoveRateMax
                    );
                }
                return Mathf.Clamp(_currentMoveRate, MoveRateMin, MoveRateMax);
            }
            set => _currentMoveRate = Mathf.Clamp(value, MoveRateMin, MoveRateMax);
        }
        public int CurrentMoveRateDisplay => Mathf.Clamp(Mathf.FloorToInt(CurrentMoveRate), MoveRateMin, MoveRateMax);
        public float MoveRatePctAdd => MoveRates.PercentAdd;
        public int MoveRateFlatAdd => MoveRates.FlatAdd;
        //speed
        public int Speed => (stats != null) ? stats.Speed : 0;
        public int AttackSeconds => stats != null
            ? AttackProfileRules.ResolveSeconds(stats)
            : Mathf.Clamp(fallbackAttackSeconds, AttackProfileRules.MinSeconds, AttackProfileRules.MaxSeconds);
        public int AttackEnergyCost => stats != null
            ? AttackProfileRules.ResolveEnergy(stats)
            : Mathf.Clamp(fallbackAttackEnergyCost, 0, AttackProfileRules.MaxEnergyCost);
        public float AttackRefundThresholdSeconds => stats != null
            ? AttackProfileRules.ResolveRefundThreshold(stats)
            : AttackProfileRules.DefaultRefundThresholdSeconds;
        public float AttackFreeMoveCutoffSeconds => stats != null
            ? AttackProfileRules.ResolveFreeMoveCutoff(stats)
            : AttackProfileRules.DefaultFreeMoveCutoffSeconds;
        public int AttackMeleeRange => stats != null
            ? AttackProfileRules.ResolveMeleeRange(stats)
            : AttackProfileRules.DefaultMeleeRange;
        public float AttackKeepDeg => stats != null
            ? AttackProfileRules.ResolveKeepDeg(stats)
            : AttackProfileRules.DefaultKeepDeg;
        public float AttackTurnDeg => stats != null
            ? AttackProfileRules.ResolveTurnDeg(stats)
            : AttackProfileRules.DefaultTurnDeg;
        public float AttackTurnSpeedDegPerSec => stats != null
            ? AttackProfileRules.ResolveTurnSpeed(stats)
            : AttackProfileRules.DefaultTurnSpeedDegPerSec;
        public int MoveEnergyPerSecond => stats != null
            ? MoveProfileRules.ResolveEnergyPerSecond(stats)
            : Mathf.Clamp(fallbackMoveEnergyPerSecond, 0, MoveProfileRules.MaxEnergyPerSecond);
        public float MoveBaseSeconds => stats != null
            ? MoveProfileRules.ResolveBaseSeconds(stats)
            : Mathf.Clamp(fallbackMoveBaseSeconds, MoveProfileRules.MinSeconds, MoveProfileRules.MaxSeconds);
        public int MoveBaseSecondsCeil => Mathf.Max(1, Mathf.CeilToInt(MoveBaseSeconds));
        public float MoveRefundThresholdSeconds => stats != null
            ? MoveProfileRules.ResolveRefundThreshold(stats)
            : Mathf.Clamp(fallbackMoveRefundThresholdSeconds, 0.01f, 1f);
        public int MoveFallbackSteps => stats != null
            ? MoveProfileRules.ResolveFallbackSteps(stats)
            : Mathf.Clamp(fallbackMoveFallbackSteps, MoveProfileRules.MinFallbackSteps, MoveProfileRules.MaxFallbackSteps);
        public int MoveStepsCap => stats != null
            ? MoveProfileRules.ResolveStepsCap(stats)
            : Mathf.Clamp(fallbackMoveStepsCap, MoveProfileRules.MinStepsCap, MoveProfileRules.MaxStepsCap);
        public float MoveKeepDeg => stats != null
            ? MoveProfileRules.ResolveKeepDeg(stats)
            : Mathf.Repeat(Mathf.Max(0f, fallbackMoveKeepDeg), 360f);
        public float MoveTurnDeg => stats != null
            ? MoveProfileRules.ResolveTurnDeg(stats)
            : Mathf.Repeat(Mathf.Max(0f, fallbackMoveTurnDeg), 360f);
        public float MoveTurnSpeedDegPerSec => stats != null
            ? MoveProfileRules.ResolveTurnSpeed(stats)
            : Mathf.Max(0f, fallbackMoveTurnSpeedDegPerSec);
        public float MoveCooldownSeconds => stats != null
            ? MoveProfileRules.ResolveCooldownSeconds(stats)
            : Mathf.Max(0f, fallbackMoveCooldownSeconds);
        public string MoveActionId
        {
            get
            {
                if (stats != null)
                    return MoveProfileRules.ResolveActionId(stats);
                return string.IsNullOrWhiteSpace(fallbackMoveActionId)
                    ? MoveProfileRules.DefaultActionId
                    : fallbackMoveActionId.Trim();
            }
        }
        // —— 能量 ——
        public int Energy => stats != null ? stats.Energy : 0;
        public int MaxEnergy => stats != null ? stats.MaxEnergy : 0;

        // —— 攻击/主属性/暴击（按 StatsV2 的定义直通暴露，不造额外公式） —— 
        public int Attack => stats != null ? stats.Attack : 0;
        public float PrimaryP => stats != null ? stats.PrimaryP : 0f;          // 主属性换算后的百分比（小数）
        public float CritChance => stats != null ? stats.CritChance : 0f;        // [0,1] 已做上限
        public float CritOverflow => stats != null ? stats.CritOverflow : 0f;      // 超帽部分（小数，可>0）
        public float CritMult => stats != null ? stats.CritMult : 2f;          // 例如 2.0 = 200%
        public float Mastery => stats != null ? stats.Mastery : 0f;           // 可>1 的精通

        // —— 其它常用桶/减伤直通（需要就用，没用可以忽略） —— 
        public float DamageBonusPct => stats != null ? stats.DamageBonusPct : 0f;
        public float DamageReducePct => stats != null ? stats.DamageReducePct : 0f;

        // ========= 调试 =========
        [Header("Debug")]
        public bool debugLog = true;

        void Awake()
        {
            MoveRates.ResetRuntime();
        }

        void OnValidate()
        {
            if (stats != null) stats.Clamp();
            MoveRates.Clamp();
            _currentMoveRate = Mathf.Max(0.01f, StatsMathV2.MR_MultiThenFlat(
                BaseMoveRate,
                new[] { MoveRates.NormalizedMultiplier },
                MoveRateFlatAdd,
                MoveRateMin,
                MoveRateMax));
            fallbackAttackSeconds = Mathf.Clamp(fallbackAttackSeconds, AttackProfileRules.MinSeconds, AttackProfileRules.MaxSeconds);
            fallbackAttackEnergyCost = Mathf.Clamp(fallbackAttackEnergyCost, 0, AttackProfileRules.MaxEnergyCost);
            fallbackMoveEnergyPerSecond = Mathf.Clamp(fallbackMoveEnergyPerSecond, 0, MoveProfileRules.MaxEnergyPerSecond);
            fallbackMoveBaseSeconds = Mathf.Clamp(fallbackMoveBaseSeconds, MoveProfileRules.MinSeconds, MoveProfileRules.MaxSeconds);
            fallbackMoveRefundThresholdSeconds = Mathf.Clamp(fallbackMoveRefundThresholdSeconds, 0.01f, 1f);
            fallbackMoveFallbackSteps = Mathf.Clamp(fallbackMoveFallbackSteps, MoveProfileRules.MinFallbackSteps, MoveProfileRules.MaxFallbackSteps);
            fallbackMoveStepsCap = Mathf.Clamp(fallbackMoveStepsCap, MoveProfileRules.MinStepsCap, MoveProfileRules.MaxStepsCap);
            fallbackMoveKeepDeg = Mathf.Repeat(Mathf.Max(0f, fallbackMoveKeepDeg), 360f);
            fallbackMoveTurnDeg = Mathf.Repeat(Mathf.Max(0f, fallbackMoveTurnDeg), 360f);
            fallbackMoveTurnSpeedDegPerSec = Mathf.Max(0f, fallbackMoveTurnSpeedDegPerSec);
            if (string.IsNullOrWhiteSpace(fallbackMoveActionId))
                fallbackMoveActionId = MoveProfileRules.DefaultActionId;
            if (_rules == null)
                _rules = new UnitRuleSet();
        }

        [ContextMenu("Debug/Print Snapshot")]
        public void PrintSnapshot()
        {
            if (!debugLog) return;
            Debug.Log(
                $"[UnitCtx] MR={MoveRate}  Energy={Energy}/{MaxEnergy}  " +
                $"ATK={Attack}  PrimaryP={PrimaryP:F3}  Crit={CritChance:P1} (Overflow={CritOverflow:P1})  CritMult={CritMult:F2}  " +
                $"Mastery={Mastery:F3}  DmgBonus={DamageBonusPct:P0}  DmgReduce={DamageReducePct:P0}",
                this
            );
        }
        [SerializeField] bool _permanentSlowApplied = false;
        public bool TryApplyPermanentSlowOnce(float mult, out int before, out int after)
        {
            before = MoveRate; after = before;
            if (_permanentSlowApplied) return false;

            mult = Mathf.Clamp(mult, 0.1f, 1f);
            int target = Mathf.Max(1, Mathf.FloorToInt(before * mult));
            if (BaseMoveRate > target) BaseMoveRate = target;
            after = MoveRate;
            _permanentSlowApplied = true;
            return true;
        }
    }
}

