#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Shim for C# 9 init accessors on older Unity runtimes.
    /// </summary>
    public static class IsExternalInit { }
}
#endif