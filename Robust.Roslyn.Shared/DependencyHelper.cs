using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Robust.Roslyn.Shared;

public static class DependencyHelper
{
    /// <summary>
    /// Mapping of special cases of name suggestions for specific types.
    /// Key is the triggering typename without the leading 'I' for interfaces or any side prefix (like 'Shared' or 'Server').
    /// Value is the resulting name suggestion, formatted as public/protected field name (PascalCase rather than _privateField).
    /// </summary>
    private static readonly Dictionary<string, string> SpecialNameMappings = new()
    {
        {"EntityManager", "EntMan"},
        {"PrototypeManager", "ProtoMan"},
        {"ConfigurationManager", "Cfg"},
        {"GameTiming", "Timing"},
        {"RobustRandom", "Random"},
    };

    private const string DependencyAttributeName = "Dependency";
    private const string EntityQueryName = "EntityQuery";
    private const string ComponentSuffix = "Component";
    private const string SystemSuffix = "System";
    private readonly static string[] SidePrefixes = ["Shared", "Server", "Client"];

    /// <summary>
    /// Checks if <paramref name="node"/> has the [Dependency] attribute.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if <paramref name="node"/> has the [Dependency] attribute, otherwise <see langword="false"/>.
    /// </returns>
    public static bool HasDependencyAttribute(IncompleteMemberSyntax node)
    {
        foreach (var attrList in node.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (attr.Name.ToString() == DependencyAttributeName)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Generates a suggested name for a dependency field of the specified type.
    /// </summary>
    /// <param name="typeSyntax">The Type of the dependency field.</param>
    /// <param name="isPrivate">
    ///     The accessiblity of the field, such as private or protected.
    ///     Determines the format of the returned name.
    /// </param>
    /// <returns>The suggested name.</returns>
    /// <seealso cref="SpecialNameMappings"/>
    public static string GetDependencyFieldName(TypeSyntax typeSyntax, Accessibility accessibility)
    {
        // If the type has a nullable annotation (?), remove it.
        if (typeSyntax is NullableTypeSyntax nullableSyntax)
            typeSyntax = nullableSyntax.ElementType;

        var typeName = typeSyntax is GenericNameSyntax genericSyntax
            ? GetGenericFieldName(genericSyntax)
            : RemoveLeadingIFromInterface(typeSyntax.ToString());

        // Remove leading side prefixes.
        foreach (var prefix in SidePrefixes)
        {
            if (typeName.StartsWith(prefix))
                typeName = typeName.Substring(prefix.Length);
        }

        // Check for a special match.
        if (SpecialNameMappings.TryGetValue(typeName, out var special))
            typeName = special;

        // Remove trailing 'System' from EntitySystem names.
        if (typeName.EndsWith(SystemSuffix))
            typeName = typeName.Substring(0, typeName.Length - SystemSuffix.Length);

        return accessibility switch
        {
            // Add leading '_' and change first letter to lowercase.
            Accessibility.Private or Accessibility.Internal => $"_{char.ToLowerInvariant(typeName[0])}{typeName.Substring(1)}",
            // Just use the name.
            _ => typeName,
        };
    }

    private static string RemoveLeadingIFromInterface(string typeName)
    {
        if (typeName[0] == 'I' && typeName.Length > 1 && char.IsUpper(typeName[1]))
            return typeName.Substring(1);
        return typeName;
    }

    /// <summary>
    /// Generates a suggested name for a dependency field with a generic type.
    /// </summary>
    /// <example>
    ///     SomeGeneric<SomeThing> -> SomeThingSomeGeneric
    /// </example>
    /// <example>
    ///     EntityQuery<SomeThingComponent> -> SomeThingQuery
    /// </example>
    /// <returns>The suggested name.</returns>
    private static string GetGenericFieldName(GenericNameSyntax genericSyntax)
    {
        var typeArgNames = genericSyntax.TypeArgumentList.Arguments.Select(GetTypeArgName);
        var outerTypeName = RemoveLeadingIFromInterface(genericSyntax.Identifier.ToString());

        // Special case for EntityQuery - transform "EntityQuery<MyThingComponent>" to "MyThingQuery".
        if (outerTypeName == EntityQueryName)
            outerTypeName = "Query";

        // Concatenate the type arg names and put the generic type name on the end.
        return $"{string.Join("", typeArgNames)}{outerTypeName}";
    }

    /// <summary>
    /// Generates a suggested name for a type argument to be included in a generic dependency field name.
    /// </summary>
    public static string GetTypeArgName(TypeSyntax arg)
    {
        var name = arg.ToString();

        // Remove trailing "Component" from component name.
        if (name.EndsWith(ComponentSuffix))
            name = name.Substring(0, name.Length - ComponentSuffix.Length);

        // Capitalize the first letter.
        return $"{char.ToUpperInvariant(name[0])}{name.Substring(1)}";
    }
}
