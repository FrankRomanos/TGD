using System.Collections.Generic;
using TGD.CoreV2;
using TGD.HexBoard;
using UnityEngine;

namespace TGD.CombatV2
{
    /// <summary>
    /// 每个单位在回合系统中的运行时快照。
    /// </summary>
    public sealed class TurnRuntimeV2
    {
        public Unit Unit { get; }
        public bool IsPlayer { get; }
        public UnitRuntimeContext Context { get; private set; }

        public int PrepaidTime { get; private set; }
        public int RemainingTime { get; private set; }

        public Dictionary<string, int> CustomResources { get; } = new();
        public Dictionary<string, int> CustomResourceMax { get; } = new();

        public TurnRuntimeV2(Unit unit, UnitRuntimeContext context, bool isPlayer)
        {
            Unit = unit;
            Context = context;
            IsPlayer = isPlayer;
            ResetBudget();
        }

        public void Bind(UnitRuntimeContext context)
        {
            Context = context;
            ResetBudget();
        }

        public int TurnTime
        {
            get
            {
                if (Context != null && Context.stats != null)
                    return Mathf.Max(0, Context.stats.TurnTime);
                return StatsMathV2.BaseTurnSeconds;
            }
        }

        public void ResetBudget()
        {
            int tt = TurnTime;
            RemainingTime = Mathf.Clamp(tt, 0, tt);
        }

        public void BeginTurn()
        {
            int tt = TurnTime;
            int prepaid = Mathf.Clamp(PrepaidTime, 0, tt);
            int baseBudget = Mathf.Clamp(RemainingTime, 0, tt);
            if (baseBudget <= 0)
                baseBudget = tt;
            RemainingTime = Mathf.Clamp(baseBudget - prepaid, 0, tt);
            PrepaidTime = 0;
        }

        public void FinishTurn()
        {
            RemainingTime = 0;
        }

        public void SpendTime(int seconds)
        {
            RemainingTime = Mathf.Max(0, RemainingTime - Mathf.Max(0, seconds));
        }

        public void RefundTime(int seconds)
        {
            RemainingTime = Mathf.Max(0, RemainingTime + Mathf.Max(0, seconds));
            RemainingTime = Mathf.Min(RemainingTime, TurnTime);
        }

        public void ApplyPrepaid(int seconds)
        {
            PrepaidTime = Mathf.Max(0, PrepaidTime + Mathf.Max(0, seconds));
        }

        public void ClearPrepaid()
        {
            PrepaidTime = 0;
        }
    }
}
