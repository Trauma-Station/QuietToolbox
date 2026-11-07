using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Robust.CompletionProviders.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class DependencyCompletionProviderTest
{
    private static async Task Verifier(string source, List<string> expected)
    {
        var test = new CompletionProviderTest<DependencyCompletionProvider>()
        {
            TestState =
            {
                Sources = { source }
            },
            ExpectedSuggestions = expected
        };

        await test.RunAsync();
    }

    [Test]
    // Private
    [TestCase("private", "IMyDependency", "_myDependency = default!;")]
    [TestCase("private", "InconvenientlyNamedThing", "_inconvenientlyNamedThing = default!;")]
    [TestCase("private", "MyDependencySystem", "_myDependency = default!;")]
    [TestCase("private", "SharedMyDependencySystem", "_myDependency = default!;")]
    [TestCase("private", "CommonMyDependencySystem", "_myDependency = default!;")]
    [TestCase("private", "ISharedMyDependencyManager", "_myDependencyMan = default!;")]
    [TestCase("private", "IEntityManager", "_entMan = default!;")]
    [TestCase("private", "IPrototypeManager", "_protoMan = default!;")]
    [TestCase("private", "IConfigurationManager", "_cfg = default!;")]
    [TestCase("private", "IGameTiming", "_timing = default!;")]
    [TestCase("private", "EntityQuery<ThingComponent>", "_thingQuery = default;")]
    [TestCase("private", "INullableDependency?", "_nullableDependency = default;")]
    [TestCase("private", "ISomeGeneric<SomeThing>", "_someThingSomeGeneric = default!;")]
    [TestCase("private", "ISomeGeneric<int>", "_intSomeGeneric = default!;")]
    // Protected
    [TestCase("protected", "IMyDependency", "MyDependency = default!;")]
    [TestCase("protected", "InconvenientlyNamedThing", "InconvenientlyNamedThing = default!;")]
    [TestCase("protected", "MyDependencySystem", "MyDependency = default!;")]
    [TestCase("protected", "SharedMyDependencySystem", "MyDependency = default!;")]
    [TestCase("protected", "CommonMyDependencySystem", "MyDependency = default!;")]
    [TestCase("protected", "ISharedMyDependencyManager", "MyDependencyMan = default!;")]
    [TestCase("protected", "IEntityManager", "EntMan = default!;")]
    [TestCase("protected", "IPrototypeManager", "ProtoMan = default!;")]
    [TestCase("protected", "IConfigurationManager", "Cfg = default!;")]
    [TestCase("protected", "IGameTiming", "Timing = default!;")]
    [TestCase("protected", "EntityQuery<ThingComponent>", "ThingQuery = default;")]
    [TestCase("protected", "INullableDependency?", "NullableDependency = default;")]
    [TestCase("protected", "ISomeGeneric<SomeThing>", "SomeThingSomeGeneric = default!;")]
    [TestCase("protected", "ISomeGeneric<int>", "IntSomeGeneric = default!;")]
    // Public
    [TestCase("public", "IMyDependency", "MyDependency = default!;")]
    [TestCase("public", "IEntityManager", "EntMan = default!;")]
    [TestCase("public", "EntityQuery<ThingComponent>", "ThingQuery = default;")]
    // Internal
    [TestCase("internal", "IMyDependency", "_myDependency = default!;")]
    [TestCase("internal", "IEntityManager", "_entMan = default!;")]
    [TestCase("internal", "EntityQuery<ThingComponent>", "_thingQuery = default;")]
    public async Task VerifyDependency(string accessMod, string typeName, string expected)
    {
        string source = $$"""
            public struct EntityQuery<T> { }

            public class Foo
            {
                [Dependency] {{accessMod}} {{typeName}} $$
            }
            """;

        await Verifier(source, [expected]);
    }

    [Test]
    [TestCase("private $$")]
    [TestCase("private IMyThing $$")]
    [TestCase("[Dependency] priv$$")]
    [TestCase("[Dependency] private $$")]
    [TestCase("[Dependency] private IMyThing _foo $$")]
    public async Task VerifyIgnored(string line)
    {
        string source = $$"""
            public class Foo
            {
                {{line}}
            }
        """;

        await Verifier(source, []);
    }
}
