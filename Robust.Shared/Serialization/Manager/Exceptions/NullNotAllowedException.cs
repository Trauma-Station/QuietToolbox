using System;

namespace Robust.Shared.Serialization.Manager.Exceptions;

public sealed class NullNotAllowedException(string name) : Exception
{
    public NullNotAllowedException() : this("?") {}

    public readonly string Name = name;

    public override string Message => $"Null value provided for reading '{name}' but type was not nullable";
}
