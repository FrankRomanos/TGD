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
        // File: TGD.CoreV2/UnitRuntimeContext.cs
        // 在类里其它便捷属性旁边新增：
        public bool Entangled => stats != null && stats.IsEntangled; // ★ 新增：对外只读，不暴露 StatsV2


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
                    _currentMoveRate = StatsMathV2.MR_MultiThenFlat(BaseMoveRate, new[] { 1f }, MoveRateFlatAdd);
                return _currentMoveRate;
            }
            set => _currentMoveRate = Mathf.Max(0.01f, value);
        }
        public float MoveRatePctAdd => stats != null ? stats.MoveRatePctAdd : 0f;
        public int MoveRateFlatAdd => stats != null ? stats.MoveRateFlatAdd : 0;
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
        public float DmgBonusA_P => stats != null ? stats.DmgBonusA_P : 0f;
        public float DmgBonusB_P => stats != null ? stats.DmgBonusB_P : 0f;
        public float DmgBonusC_P => stats != null ? stats.DmgBonusC_P : 0f;
        public float ReduceA_P => stats != null ? stats.ReduceA_P : 0f;
        public float ReduceB_P => stats != null ? stats.ReduceB_P : 0f;
        public float ReduceC_P => stats != null ? stats.ReduceC_P : 0f;

        // ========= 调试 =========
        [Header("Debug")]
        public bool debugLog = true;

        void OnValidate()
        {
            if (stats != null) stats.Clamp();
            _currentMoveRate = Mathf.Max(0.01f, StatsMathV2.MR_MultiThenFlat(BaseMoveRate, new[] { 1f }, MoveRateFlatAdd));
        }

        [ContextMenu("Debug/Print Snapshot")]
        public void PrintSnapshot()
        {
            if (!debugLog) return;
            Debug.Log(
                $"[UnitCtx] MR={MoveRate}  Energy={Energy}/{MaxEnergy}  " +
                $"ATK={Attack}  PrimaryP={PrimaryP:F3}  Crit={CritChance:P1} (Overflow={CritOverflow:P1})  CritMult={CritMult:F2}  " +
                $"Mastery={Mastery:F3}  BonusA={DmgBonusA_P:P0} BonusB={DmgBonusB_P:P0} BonusC={DmgBonusC_P:P0}  ReduceA={ReduceA_P:P0} ReduceB={ReduceB_P:P0} ReduceC={ReduceC_P:P0}",
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

