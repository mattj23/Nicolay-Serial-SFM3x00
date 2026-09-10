using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Allows records and init-only properties to compile against target frameworks with older
    /// libraries that do not define this type.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
