using System.Collections;
using TGD.CombatV2.Targeting;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.CombatV2
{
    public sealed class TestFullRoundAction : ChainActionBase, IFullRoundActionTool
    {
        [Header("Full Round")]
        [Min(1)] public int rounds = 2;
        [Tooltip("W2 阶段即时效果的日志内容")]
        public string immediateLog = "[Test] FullRound Immediate";
        [Tooltip("到期时在回合开始阶段触发的日志内容")]
        public string resolveLog = "[Test] FullRound Resolve";

        int _preparedSeconds;

        public override ActionKind Kind => ActionKind.FullRound;

        void Reset()
        {
            skillId = "FullRoundTest";
            timeCostSeconds = 0;
            energyCost = 0;
            targetRule = TargetRule.AnyClick;
            cooldownSeconds = 18;
            rounds = 2;
            immediateLog = "[Test] FullRound Immediate";
            resolveLog = "[Test] FullRound Resolve";
        }

        public int FullRoundRounds => Mathf.Max(1, rounds);

        public void PrepareFullRoundSeconds(int seconds)
        {
            _preparedSeconds = Mathf.Max(0, seconds);
        }

        public void TriggerFullRoundImmediate(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan)
        {
            string unitLabel = unit != null ? TurnManagerV2.FormatUnitLabel(unit) : "?";
            string targetLabel = plan.valid ? plan.target.ToString() : "None";
            Debug.Log($"[FullRound] Immediate U={unitLabel} id={Id} target={targetLabel} seconds={plan.plannedSeconds} msg={immediateLog}", this);
        }

        public void TriggerFullRoundResolution(Unit unit, TurnManagerV2 turnManager, FullRoundQueuedPlan plan)
        {
            string unitLabel = unit != null ? TurnManagerV2.FormatUnitLabel(unit) : "?";
            string targetLabel = plan.valid ? plan.target.ToString() : "None";
            Debug.Log($"[FullRound] Resolve U={unitLabel} id={Id} target={targetLabel} seconds={plan.plannedSeconds} msg={resolveLog}", this);
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            yield return base.OnConfirm(hex);
            SetExecReport(_preparedSeconds, 0, Mathf.Max(0, energyCost));
        }
    }
}
