namespace TGD.CombatV2
{
    public interface IActionExecReportV2
    {
        int UsedSeconds { get; }
        int RefundedSeconds { get; }
        void Consume();
    }

    public interface IActionEnergyReportV2
    {
        int ReportMoveEnergyNet { get; }
        int ReportAttackEnergyNet { get; }
    }

    public interface IBudgetGateSkippable
    {
        bool SkipBudgetGate { get; set; }
    }
}