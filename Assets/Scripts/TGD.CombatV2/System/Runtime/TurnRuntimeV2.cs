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
        public bool HasSpentTimeThisTurn { get; private set; }
        public bool HasReachedIdle { get; private set; }
        public bool HasReorderedThisTurn { get; private set; }
        public bool DeferredFromIdle { get; private set; }
        public int DeferredIdlePhaseIndex { get; private set; }
        public int ActivePhaseIndex { get; private set; }

        public int BaseTimeForNext => _baseTimeForNext;
        int _baseTimeForNext;

        public readonly struct BeginSnapshot
        {
            public readonly int BasePrev;
            public readonly int BaseNew;
            public readonly int RemainPrev;
            public readonly int RemainAfterRebase;
            public readonly int Prepaid;
            public readonly int RemainAfterPrepaid;

            public BeginSnapshot(int basePrev, int baseNew, int remainPrev, int remainAfterRebase, int prepaid, int remainAfterPrepaid)
            {
                BasePrev = basePrev;
                BaseNew = baseNew;
                RemainPrev = remainPrev;
                RemainAfterRebase = remainAfterRebase;
                Prepaid = prepaid;
                RemainAfterPrepaid = remainAfterPrepaid;
            }
        }

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

        public int ResetBudget()
        {
            int baseNew = Mathf.Max(0, TurnTime);
            _baseTimeForNext = baseNew;
            RemainingTime = baseNew;
            return baseNew;
        }

        public BeginSnapshot BeginTurn()
        {
            int basePrev = Mathf.Max(0, _baseTimeForNext);
            int remainPrev = Mathf.Max(0, RemainingTime);
            if (basePrev > 0)
                remainPrev = Mathf.Min(remainPrev, basePrev);

            int baseNew = Mathf.Max(0, TurnTime);
            int delta = Mathf.Max(baseNew - basePrev, 0);
            int remainAfterRebase = Mathf.Clamp(remainPrev + delta, 0, baseNew);

            _baseTimeForNext = baseNew;

            int prepaid = Mathf.Clamp(PrepaidTime, 0, baseNew);
            int remainAfterPrepaid = Mathf.Max(0, remainAfterRebase - prepaid);

            RemainingTime = remainAfterPrepaid;
            PrepaidTime = 0;
            HasSpentTimeThisTurn = false;
            HasReachedIdle = false;
            HasReorderedThisTurn = false;
            DeferredFromIdle = false;
            DeferredIdlePhaseIndex = 0;

            return new BeginSnapshot(basePrev, baseNew, remainPrev, remainAfterRebase, prepaid, remainAfterPrepaid);
        }

        public void FinishTurn()
        {
            RemainingTime = 0;
            ActivePhaseIndex = 0;
        }

        public void SpendTime(int seconds)
        {
            RemainingTime = Mathf.Max(0, RemainingTime - Mathf.Max(0, seconds));
            if (seconds > 0)
                HasSpentTimeThisTurn = true;
        }

        public void RefundTime(int seconds)
        {
            if (seconds <= 0)
                return;

            RemainingTime = Mathf.Max(0, RemainingTime + seconds);
            if (_baseTimeForNext >= 0)
                RemainingTime = Mathf.Min(RemainingTime, _baseTimeForNext);
        }

        public void ApplyPrepaid(int seconds)
        {
            PrepaidTime = Mathf.Max(0, PrepaidTime + Mathf.Max(0, seconds));
        }

        public void ClearPrepaid()
        {
            PrepaidTime = 0;
        }
        public void SetActivePhaseIndex(int phaseIndex)
        {
            ActivePhaseIndex = Mathf.Max(phaseIndex, 0);
        }

        public void MarkIdleReached()
        {
            HasReachedIdle = true;
        }

        public void MarkReorderedFromIdle(int phaseIndex)
        {
            HasReorderedThisTurn = true;
            DeferredFromIdle = true;
            DeferredIdlePhaseIndex = Mathf.Max(0, phaseIndex);
            HasReachedIdle = false;
        }

        public void ClearDeferredIdle()
        {
            DeferredFromIdle = false;
            DeferredIdlePhaseIndex = 0;
        }
    }
}
