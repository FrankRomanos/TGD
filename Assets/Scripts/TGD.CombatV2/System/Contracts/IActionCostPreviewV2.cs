namespace TGD.CombatV2
{
    public interface IActionCostPreviewV2
    {
        bool TryPeekCost(out int seconds, out int energy);
    }
}
