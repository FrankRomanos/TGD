using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public sealed class TestFullRoundAction : ChainTestActionBase, IFullRoundActionTool
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
            actionId = "FullRoundTest";
            timeCostSeconds = 0;
            energyCost = 0;
            targetMode = TargetMode.AnyClick;
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

        public void TriggerFullRoundImmediate(Unit unit, TurnManagerV2 turnManager)
        {
            string unitLabel = unit != null ? TurnManagerV2.FormatUnitLabel(unit) : "?";
            Debug.Log($"[FullRound] Immediate U={unitLabel} id={Id} msg={immediateLog}", this);
        }

        public void TriggerFullRoundResolution(Unit unit, TurnManagerV2 turnManager)
        {
            string unitLabel = unit != null ? TurnManagerV2.FormatUnitLabel(unit) : "?";
            Debug.Log($"[FullRound] Resolve U={unitLabel} id={Id} msg={resolveLog}", this);
        }

        public override IEnumerator OnConfirm(Hex hex)
        {
            yield return base.OnConfirm(hex);
            SetExecReport(_preparedSeconds, 0, Mathf.Max(0, energyCost));
        }
    }
}
