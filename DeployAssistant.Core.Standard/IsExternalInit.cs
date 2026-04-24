// Polyfill required for C# 9 init-only setters when targeting netstandard2.0.
// The compiler emits a modreq for this type, which is only included in .NET 5+.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
