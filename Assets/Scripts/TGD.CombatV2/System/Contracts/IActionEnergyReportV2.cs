namespace TGD.CombatV2
{
    public interface IActionEnergyReportV2 : IActionExecReportV2
    {
        int EnergyUsed { get; }
    }
}
