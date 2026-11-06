using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Tags;
using Robust.Roslyn.Shared;

namespace Robust.CompletionProviders;

/// <summary>
/// Provides completion suggestions when typing a new [Dependency] field.
/// Suggestion is provided after the Type has been specified, <c>[Dependency] private IEntityManager _</c>.
/// The completion automatically provides a name for the field and sets the value to <see langword="default"/>
/// with null suppression if needed.
/// </summary>
[ExportCompletionProvider(nameof(DependencyCompletionProvider), LanguageNames.CSharp)]
public sealed class DependencyCompletionProvider : CompletionProvider
{
    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        var document = context.Document;

        if (!document.SupportsSemanticModel || !document.SupportsSyntaxTree)
            return;

        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        // Get the syntax token at the cursor position.
        var token = root.FindToken(context.Position);

        // Since the user is still typing the line, we don't know if it's a field, property, method, or whatever.
        // So we get the incomplete syntax and deduce what we can.
        var memberSyntax = token.Parent?.AncestorsAndSelf().OfType<IncompleteMemberSyntax>().FirstOrDefault();

        // If the cursor isn't in some other sort of code, memberSyntax will be null.
        // If they've typed 'private' but not the field type yet, memberSyntax.Type will be null.
        if (memberSyntax?.Type is null)
            return;

        // Don't offer completions until the user specifies private/public.
        // This prevents showing suggestions too early, like
        // "[Dependency] priva" -> "[Dependency] _priva = default!".
        if (memberSyntax.Modifiers.Count < 1)
            return;

        // We only want to offer completions on [Dependency] fields.
        if (!DependencyHelper.HasDependencyAttribute(memberSyntax))
            return;

        // Private fields have a different expected name format.
        if (!TryGetAccessiblityModifier(memberSyntax, out var fieldAccessibility))
            return;

        // Get the suggested field name.
        var fieldName = DependencyHelper.GetDependencyFieldName(memberSyntax.Type, fieldAccessibility.Value);

        // Look up the symbol for the field's Type.
        // We don't do this earlier because it's fairly expensive.
        var model = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var typeSymbol = model?.GetSymbolInfo(memberSyntax.Type).Symbol as ITypeSymbol;

        // Evaluate the type to see if we need to add null supression (!) to the assignment.
        var nullSuppression = DefaultNeedsNullSuppression(typeSymbol) ? "!" : "";

        // Construct the final text of the suggestion.
        var suggestion = $"{fieldName} = default{nullSuppression};";

        var properties = ImmutableDictionary<string, string>.Empty.Add("Provider", nameof(DependencyCompletionProvider));

        var completion = CompletionItem.Create(
            suggestion,
            suggestion,
            suggestion,
            properties: properties,
            rules: CompletionItemRules.Default.WithMatchPriority(MatchPriority.Preselect),
            tags: [WellKnownTags.Snippet]
        );

        context.AddItem(completion);
    }

    public override async Task<CompletionDescription?> GetDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
    {
        return CompletionDescription.FromText("(Auto-complete dependency field)");
    }

    /// <summary>
    /// Attempts to get the accessibility modifier (private, protected, etc.) of a syntax node.
    /// </summary>
    /// <remarks>
    /// Because this uses syntax instead of semantic analysis, it only detects explicit modifiers.
    /// Implicit modifiers will not be found.
    /// </remarks>
    private static bool TryGetAccessiblityModifier(MemberDeclarationSyntax node, [NotNullWhen(true)] out Accessibility? accessibility)
    {
        if (node.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            accessibility = Accessibility.Public;
            return true;
        }
        if (node.Modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            accessibility = Accessibility.Protected;
            return true;
        }
        if (node.Modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            accessibility = Accessibility.Private;
            return true;
        }
        if (node.Modifiers.Any(SyntaxKind.InternalKeyword))
        {
            accessibility = Accessibility.Internal;
            return true;
        }

        accessibility = null;
        return false;
    }

    /// <summary>
    /// Checks whether a field of the specified type needs nullable suppression (!) in order to assign <see langword="null"/> to it.
    /// </summary>
    /// <returns><see langword="true"/> if the assignment requires nullable suppression, otherwise <see langword="false"/>.</returns>
    private static bool DefaultNeedsNullSuppression(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
            return true;

        return typeSymbol.IsReferenceType;
    }
}
