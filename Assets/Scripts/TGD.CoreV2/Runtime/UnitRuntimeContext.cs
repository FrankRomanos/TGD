// File: TGD.CoreV2/UnitRuntimeContext.cs
using UnityEngine;

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

        // ========= 便捷只读访问（统一入口；外部系统只读这些） =========
        // —— 移动 —— 
        public int MoveRate => (stats != null) ? stats.MoveRate : Mathf.Max(1, Mathf.RoundToInt(fallbackMoveRate));
        // ★ 新增：基础移速（可写，写回 Stats 或 fallback）
        public int BaseMoveRate
        {
            get => stats != null ? Mathf.Max(1, stats.MoveRate)
                                 : Mathf.Max(1, Mathf.RoundToInt(fallbackMoveRate));
            set
            {
                int v = Mathf.Max(1, value);
                if (stats != null) stats.MoveRate = v;
                else fallbackMoveRate = v;
            }
        }
        [SerializeField]
        float _currentMoveRate = -1f;
        public float CurrentMoveRate
        {
            get
            {
                if (_currentMoveRate <= 0f)
                    _currentMoveRate = StatsMathV2.MR_MultiThenFlat(BaseMoveRate, new[] { MoveRates.NormalizedMultiplier }, MoveRateFlatAdd);
                return _currentMoveRate;
            }
            set => _currentMoveRate = Mathf.Max(0.01f, value);
        }
        public float MoveRatePctAdd => MoveRates.PercentAdd;
        public int MoveRateFlatAdd => MoveRates.FlatAdd;
        //speed
        public int Speed => (stats != null) ? stats.Speed : 0;
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
            _currentMoveRate = Mathf.Max(0.01f, StatsMathV2.MR_MultiThenFlat(BaseMoveRate, new[] { MoveRates.NormalizedMultiplier }, MoveRateFlatAdd));
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

