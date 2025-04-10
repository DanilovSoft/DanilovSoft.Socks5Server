using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.Socks5Server;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowException(Exception exception) => throw exception;

    /// <exception cref="ArgumentOutOfRangeException"/>
    [DoesNotReturn]
    internal static void ThrowArgumentOutOfRange(string? paramName)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    /// <exception cref="ArgumentOutOfRangeException"/>
    [DoesNotReturn]
    internal static T ThrowArgumentOutOfRange<T>(string? paramName)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    /// <exception cref="ArgumentOutOfRangeException"/>
    [DoesNotReturn]
    internal static void ThrowArgumentOutOfRange(string? paramName, string? message)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    ///// <exception cref="ArgumentNullException"/>
    //[DoesNotReturn]
    //internal static void ThrowArgumentNullException(string? paramName)
    //{
    //    throw new ArgumentNullException(paramName);
    //}

    /// <exception cref="InvalidOperationException"/>
    [DoesNotReturn]
    internal static void ThrowInvalidOperationException(string? message)
    {
        throw new InvalidOperationException(message);
    }

    /// <exception cref="NotSupportedException"/>
    [DoesNotReturn]
    public static void ThrowNotSupportedException(string? message)
    {
        throw new NotSupportedException(message);
    }

    /// <exception cref="NotSupportedException"/>
    [DoesNotReturn]
    public static T ThrowNotSupportedException<T>()
    {
        throw new NotSupportedException();
    }

    /// <exception cref="ObjectDisposedException"/>
    [DoesNotReturn]
    internal static void ThrowObjectDisposedException(string? objectName)
    {
        throw new ObjectDisposedException(objectName);
    }

    internal static ObjectDisposedException ObjectDisposedException(string? objectName)
    {
        return new ObjectDisposedException(objectName);
    }

    [DoesNotReturn]
    public static void InvalidSocks5Version(byte version)
    {
        throw new InvalidOperationException($"Не верный номер версии. Получено {version}, ожидалось 1");
    }

    [DoesNotReturn]
    public static void ThrowUnreachable()
    {
        throw new UnreachableException();
    }
}
