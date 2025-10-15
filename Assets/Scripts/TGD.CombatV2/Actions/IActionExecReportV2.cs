namespace TGD.CombatV2
{
    [System.Flags]
    public enum ActionExecFlagsV2
    {
        None = 0,
        FreeMoveApplied = 1 << 0,
        RolledBackAtk = 1 << 1,
    }

    public enum ActionKindV2
    {
        Move,
        Attack,
    }

    /// <summary>
    /// 工具层向 CAM 上报的真实执行结果。
    /// </summary>
    public struct ActionExecReportV2
    {
        public ActionKindV2 kind;
        public float usedSecsMove;
        public float usedSecsAtk;
        public float refundedSecs;
        public int energyMoveNet;
        public int energyAtkNet;
        public ActionExecFlagsV2 flags;

        public override string ToString()
        {
            return $"used(move={usedSecsMove}, atk={usedSecsAtk}) refund={refundedSecs} " +
                   $"e(move={energyMoveNet}, atk={energyAtkNet}) flags={flags}";
        }
    }

    /// <summary>
    /// 工具统一接口，W3 由 CAM 驱动。
    /// </summary>
    public interface ICombatActionToolV2
    {
        ActionExecReportV2 Execute(ActionPlanV2 plan);
    }

    /// <summary>
    /// W2 计划快照。
    /// </summary>
    public struct ActionPlanV2
    {
        public ActionKindV2 kind;
        public TGD.HexBoard.Hex targetHex;
        public int planSecsMove;
        public int planSecsAtk;
        public int planEnergyMove;
        public int planEnergyAtk;
    }
}
