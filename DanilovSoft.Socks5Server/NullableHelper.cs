using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DanilovSoft.Socks5Server;

public static class NullableHelper
{
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(location1))]
    public static T Exchange<T>([NotNullIfNotNull(nameof(value))] ref T location1, T value) where T : class?
    {
        var refCopy = location1;
        location1 = value;
        return refCopy;
    }

    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(location1))]
    public static T? Exchange<T>([NotNullIfNotNull(nameof(value))] ref T? location1, T? value) where T : struct
    {
        var refCopy = location1;
        location1 = value;
        return refCopy;
    }
}
