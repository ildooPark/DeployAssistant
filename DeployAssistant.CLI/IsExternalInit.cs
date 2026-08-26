// Polyfill required for C# 9 init-only setters and positional records when targeting net472.
// The compiler emits a modreq for this type, which is only included in .NET 5+.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System.Runtime.CompilerServices
{
    // net472 lacks this attribute; internal + IVT so the test assembly's module
    // initializer (config isolation) can use it.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
