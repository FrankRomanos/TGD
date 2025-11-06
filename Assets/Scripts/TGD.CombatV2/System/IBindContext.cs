using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// Provides a lightweight hook for globally hosted tools to receive the current
    /// unit runtime context and turn manager binding when factory mode is active.
    /// </summary>
    public interface IBindContext
    {
        void BindContext(UnitRuntimeContext ctx, TurnManagerV2 turnManager);
    }
}
