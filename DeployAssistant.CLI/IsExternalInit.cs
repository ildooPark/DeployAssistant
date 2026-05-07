// Polyfill required for C# 9 init-only setters and positional records when targeting net472.
// The compiler emits a modreq for this type, which is only included in .NET 5+.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
