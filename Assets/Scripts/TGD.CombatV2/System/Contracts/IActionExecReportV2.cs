namespace TGD.CombatV2
{
    public interface IActionExecReportV2
    {
        int UsedSeconds { get; }
        int RefundedSeconds { get; }
        void Consume();
    }

    public interface IBudgetGateSkippable
    {
        bool SkipBudgetGate { get; set; }
    }
}