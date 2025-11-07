using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// <summary>
    /// Provides a common entry point for action tools that render cursor highlights while aiming.
    /// The combat manager assigns the shared highlighter before a tool enters its aim phase.
    /// </summary>
    public interface ICursorUser
    {
        void SetCursorHighlighter(IHexHighlighter highlighter);
    }
}
