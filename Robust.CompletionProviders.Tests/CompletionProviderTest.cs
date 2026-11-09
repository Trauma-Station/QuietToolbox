using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Robust.CompletionProviders.Tests;

// At the time of writing, Microsoft does not ship a test harness for CompletionProviders, so we're gonna roll our own.

/// <summary>
/// A basic test harness for CSharp <see cref="CompletionProvider"/>s.
/// One source file in <see cref="TestState"/> must contain the <see cref="PositionMarker"/>.
/// Suggested code completions will be generated using the marker as the insertion caret in the document.
/// The resulting suggestions are compared with <see cref="ExpectedSuggestions"/> to verify the
/// functionality of the <see cref="CompletionProvider"/>.
/// Changes to the document text can be verified by providing <see cref="ExpectedResults"/>.
/// </summary>
/// <remarks>
/// In order to filter for completions from the tested provider, provided completions must have an entry
/// in their Properties dictionary with the key "Provider" and a value matching nameof(T).
/// </remarks>
/// <typeparam name="T">The <see cref="CompletionProvider"/> being tested.</typeparam>
public sealed class CompletionProviderTest<T> where T : CompletionProvider, new()
{
    /// <summary>
    /// This character sequence should appear in the input source exactly once to indicate
    /// the position of the insertion caret.
    /// </summary>
    public const string PositionMarker = "$$";

    /// <summary>
    /// The initial state of the test <see cref="Solution"/> used for the test.
    /// </summary>
    /// <remarks>
    /// Source files should be added to <see cref="TestState.Sources"/>.
    /// </remarks>
    public SolutionState TestState { get; set; } = new("TestSolution", LanguageNames.CSharp, "", "cs");

    /// <summary>
    /// The display names of the suggestions that should be generated.
    /// </summary>
    public List<string> ExpectedSuggestions { get; init; } = [];

    /// <summary>
    /// A mapping of suggestion display names to the expected altered source text after applying them.
    /// </summary>
    public Dictionary<string, string>? ExpectedResults { get; init; } = [];

    /// <summary>
    /// Runs the test and verifies expected results.
    /// </summary>
    public async Task RunAsync()
    {
        var host = MefHostServices.Create(
            MefHostServices.DefaultAssemblies.Concat([typeof(T).Assembly])
        );
        var workspace = new AdhocWorkspace(host);

        // Create our base test project.
        var projectId = ProjectId.CreateNewId("TestProject");
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestProject.dll",
            LanguageNames.CSharp
        );

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        // Add our Source files to the project.
        var cursorPosition = 0;
        DocumentId? cursorDocId = null;
        foreach (var (name, content) in TestState.Sources)
        {
            var sourceText = content;
            var docId = DocumentId.CreateNewId(projectId, name);

            // Check if this file has the position marker.
            if (TryExtractPositionMarker(content, out var info))
            {
                // Only one file with a position marker is allowed.
                if (cursorDocId is not null)
                {
                    throw new InvalidOperationException("Position markers found in multiple Source files.");
                }
                cursorDocId = docId;
                cursorPosition = info.Value.Position;
                sourceText = info.Value.NewSource;
            }

            solution = solution.AddDocument(docId, name, sourceText);
        }

        // If we didn't find the position marker, we can't run the test.
        if (cursorDocId is null)
            throw new InvalidOperationException("Position marker not found in any Source file.");

        // Add required core references
        solution = solution.AddMetadataReference(
            projectId,
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        );

        // Add additional projects.
        foreach (var (name, state) in TestState.AdditionalProjects)
        {
            var id = ProjectId.CreateNewId(name);
            solution = solution.AddProject(id, name, name, LanguageNames.CSharp);

            // Add documents for the additional project.
            foreach (var (filename, content) in state.Sources)
            {
                var docId = DocumentId.CreateNewId(id, filename);
                solution = solution.AddDocument(docId, filename, content);
            }
        }

        // Add additional project references to the main test project.
        foreach (var name in TestState.AdditionalProjectReferences)
        {
            var referencedId = solution.Projects.Single(p => p.Name == name).Id;
            solution = solution.AddProjectReference(projectId, new ProjectReference(referencedId));
        }

        if (!workspace.TryApplyChanges(solution))
            Assert.Fail("Failed to setup test workspace.");

        var project = workspace.CurrentSolution.GetProject(projectId)!;
        var document = project.GetDocument(cursorDocId)!;

        var completionService = CompletionService.GetService(document);

        // Get the code completion suggestions that would be given with the cursor at the position marker.
        var suggestions = await completionService!.GetCompletionsAsync(document, cursorPosition);

        // Filter to just suggestions from our tested provider.
        var filteredSuggestions = suggestions.ItemsList.Where(item => item.Properties.TryGetValue("Provider", out var provider) && provider == typeof(T).Name);

        var names = filteredSuggestions.Select(item => item.DisplayText);

        // Verify that the suggested names are what the test expects.
        Assert.That(names, Is.EquivalentTo(ExpectedSuggestions));

        // If we're not testing how the document is changed by applying any suggestions, we're done.
        if (ExpectedResults is null)
            return;

        foreach (var (itemName, result) in ExpectedResults)
        {
            // Find the completion suggestion matching the name given by the test.
            var item = filteredSuggestions.First(item => item.DisplayText == itemName);

            // Apply the change to the text of the document.
            var change = await completionService.GetChangeAsync(document, item);
            var oldText = await document.GetTextAsync();
            var newText = oldText.WithChanges(change.TextChanges);

            // Verify that the resulting text matches what the test expects.
            Assert.That(newText.ToString(), Is.EqualTo(result));
        }
    }

    /// <summary>
    /// Attempts to remove the <see cref="PositionMarker"/> from <paramref name="source"/> and
    /// return the altered source and the position in the original source where the marker was.
    /// </summary>
    /// <param name="source">A source code file possibly containing a <see cref="PositionMarker"/>.</param>
    /// <param name="results">A tuple containing the input source with the marker removed and the position of the marker.</param>
    /// <returns><see langword="true"/> if <see cref="PositionMarker"/> was found in the file, otherwise <see langword="false"/>.</returns>
    private static bool TryExtractPositionMarker(SourceText source, [NotNullWhen(true)] out (SourceText NewSource, int Position)? results)
    {
        results = null;

        var text = source.ToString();
        var position = text.IndexOf(PositionMarker);
        if (position < 0)
            return false;
        var newSource = text.Remove(position, PositionMarker.Length);

        results = (SourceText.From(newSource), position);
        return true;
    }
}
