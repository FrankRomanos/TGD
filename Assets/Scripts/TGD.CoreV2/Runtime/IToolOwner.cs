namespace TGD.CoreV2
{
    /// <summary>
    /// Represents any combat action tool capable of binding to a unit runtime context.
    /// </summary>
    public interface IToolOwner
    {
        /// <summary>
        /// Gets the unit runtime context that owns this tool.
        /// </summary>
        UnitRuntimeContext Ctx { get; }

        /// <summary>
        /// Binds the tool to the supplied unit runtime context.
        /// </summary>
        /// <param name="ctx">The runtime context to bind to.</param>
        void Bind(UnitRuntimeContext ctx);
    }
}
