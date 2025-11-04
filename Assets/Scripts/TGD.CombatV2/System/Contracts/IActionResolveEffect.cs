using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// Optional hook invoked during W4 resolve stage for actions that need to run their effects.
    /// </summary>
    public interface IActionResolveEffect
    {
        void OnResolve(Unit unit, Hex target);
    }
}
