using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NetSerializer;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Serialization;

/// <summary>
/// Lets <see cref="EntityUid"/> be serialized using <c>GetEntity</c> and <c>GetNetEntity</c>.
/// </summary>
/// <remarks>
/// AutoGenerateComponentState generator makes shitcode that checks for like 8 different variants of EntityUid in collections.
/// This makes that entirely unnecessary.
/// </remarks>
internal sealed class NetEntityUidSerializer : IStaticTypeSerializer
{
    // yes this is static cry about it
    // there will only ever 1 be 1 instance across any threads it doesnt matter
    // if you want multiple for some fucked up reason, refactor NetSerializer rw methods to not be static.
    private static IEntityManager _ent;

    internal NetEntityUidSerializer(IEntityManager ent)
    {
        _ent = ent;
    }

    public bool Handles(Type type)
        => type == typeof(EntityUid);

    public IEnumerable<Type> GetSubtypes(Type type)
        => Type.EmptyTypes;

    public MethodInfo GetStaticWriter(Type type)
        => typeof(NetEntityUidSerializer)
            .GetMethod(nameof(WriteEntityUid), BindingFlags.Static | BindingFlags.Public)!;

    public MethodInfo GetStaticReader(Type type)
        => typeof(NetEntityUidSerializer)
            .GetMethod(nameof(ReadEntityUid), BindingFlags.Static | BindingFlags.Public)!;

    public static void WriteEntityUid(Stream stream, EntityUid value)
    {
        var id = _ent.GetNetEntity(value).Id;
        Primitives.WritePrimitive(stream, id);
    }

    public static void ReadEntityUid(Stream stream, out EntityUid value)
    {
        Primitives.ReadPrimitive(stream, out int id);
        value = _ent.GetEntity(new(id));
    }
}
