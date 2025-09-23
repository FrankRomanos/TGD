namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides a shim for init-only setters on older runtime targets that do not expose
    /// System.Runtime.CompilerServices.IsExternalInit. Unity's current runtime predates
    /// the official type, so we declare it here to unblock the compiler when using the
    /// C# 9 init accessor syntax.
    /// </summary>
    internal static class IsExternalInit
    {

    }
}
