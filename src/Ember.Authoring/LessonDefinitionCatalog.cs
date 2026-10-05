using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ember.Authoring;

/// <summary>Stable signals that lesson data may combine into step-completion requirements.</summary>
public static class LessonConditionFacts
{
    public const string ObjectAdded = "scene.object-added";
    public const string TrackedObjectPositionChanged = "scene.tracked-object-position-changed";
    public const string AnyObjectPositionChanged = "scene.any-object-position-changed";
    public const string DifferentObjectPositionChanged = "scene.different-object-position-changed";
    public const string PredictionCorrect = "play.prediction-correct";
    public const string PlayStartedAfterPrediction = "play.started-after-prediction";
    public const string PlayStoppedAfterStart = "play.stopped-after-start";
    public const string ReflectionCorrect = "play.reflection-correct";
    public const string TrackedMoveUndone = "history.tracked-move-undone";
    public const string OtherObjectPositionChangedAfterUndo = "scene.other-object-position-changed-after-undo";
    public const string ProjectSceneSaved = "file.project-scene-saved";
    public const string SameProjectSceneReopenedUnchanged = "file.same-project-scene-reopened-unchanged";

    internal static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        ObjectAdded,
        TrackedObjectPositionChanged,
        AnyObjectPositionChanged,
        DifferentObjectPositionChanged,
        PredictionCorrect,
        PlayStartedAfterPrediction,
        PlayStoppedAfterStart,
        ReflectionCorrect,
        TrackedMoveUndone,
        OtherObjectPositionChangedAfterUndo,
        ProjectSceneSaved,
        SameProjectSceneReopenedUnchanged
    };
}

/// <summary>A choice shown by a lesson step; correct choices add a named completion fact.</summary>
public sealed record LessonChoiceDefinition(string Id, string Label, string Feedback,
    bool IsCorrect, string? CompletionFact);

/// <summary>An editor action offered by a lesson, such as reopening or replaying it.</summary>
public sealed record LessonStepActionDefinition(string Id, string Label);

/// <summary>Text, completion requirements and optional controls for one lesson step.</summary>
public sealed record LessonStepDefinition(string Id, string Title, string Explanation, string Why,
    string CompletionFeedback, IReadOnlyList<string> Hints, IReadOnlyList<string> CompletionFacts,
    IReadOnlyList<LessonChoiceDefinition> Choices, LessonStepActionDefinition? Action);

/// <summary>Versioned, bundled teaching content that can be changed without changing editor code.</summary>
public sealed record LessonDefinition(string Id, int Tier, bool Available, string? RequiredFeature,
    string Title, string TransferTask,
    string CompletionTitle, string CompletionExplanation, string CompletionWhy,
    string CompletionHint, string CompletionMoreSpecificHint, LessonStepActionDefinition? CompletionAction,
    IReadOnlyList<LessonStepDefinition> Steps);

/// <summary>Loads and validates bundled lesson files before any lesson can affect a project.</summary>
public static class LessonDefinitionCatalog
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    private static readonly HashSet<string> SupportedActions = new(StringComparer.Ordinal)
    {
        "reopen-project",
        "replay-lesson"
    };
    private static readonly Lazy<IReadOnlyList<LessonDefinition>> BuiltIn = new(LoadBuiltInCore);

    public static IReadOnlyList<LessonDefinition> LoadBuiltIn() => BuiltIn.Value;

    private static IReadOnlyList<LessonDefinition> LoadBuiltInCore()
    {
        var assembly = typeof(LessonDefinitionCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".lesson.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resourceNames.Length == 0)
            throw new InvalidDataException($"No bundled lesson files were found in '{assembly.GetName().Name}'.");

        var definitions = new List<LessonDefinition>(resourceNames.Length);
        var sourceById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Bundled lesson file '{resourceName}' could not be opened.");
            var definition = Load(stream, resourceName);
            if (sourceById.TryGetValue(definition.Id, out var firstSource))
                throw new InvalidDataException(
                    $"Lesson file '{resourceName}' repeats ID '{definition.Id}' already used by '{firstSource}'.");
            sourceById.Add(definition.Id, resourceName);
            definitions.Add(definition);
        }

        return definitions.AsReadOnly();
    }

    public static LessonDefinition LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Load(stream, fullPath);
    }

    public static LessonDefinition Parse(string json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return Load(stream, sourceName);
    }

    private static LessonDefinition Load(Stream stream, string sourceName)
    {
        LessonDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LessonDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException($"Lesson file '{sourceName}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Lesson file '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Lesson file '{sourceName}' uses schema version {document.SchemaVersion}; expected {CurrentSchemaVersion}.");
        RequireId(document.Id, $"Lesson file '{sourceName}'");
        RequireText(document.Title, $"Lesson file '{sourceName}'", "title");
        RequireText(document.TransferTask, $"Lesson file '{sourceName}'", "transferTask");
        if (document.Tier < 1)
            throw new InvalidDataException($"Lesson file '{sourceName}' has an invalid tier; expected a positive number.");
        if (document.Steps is null || document.Steps.Length == 0)
            throw new InvalidDataException($"Lesson file '{sourceName}' must contain at least one step.");
        var completionAction = ValidateCompletionCopy(document.Completion, sourceName);

        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<LessonStepDefinition>(document.Steps.Length);
        foreach (var step in document.Steps)
        {
            if (step is null)
                throw new InvalidDataException($"Lesson file '{sourceName}' contains a null step.");
            ValidateId(step.Id, $"Lesson file '{sourceName}', step list entry", "step ID");
            if (!stepIds.Add(step.Id))
                throw StepError(sourceName, step.Id, "step IDs must be unique");
            RequireText(step.Title, sourceName, step.Id, "title");
            RequireText(step.Explanation, sourceName, step.Id, "explanation");
            RequireText(step.Why, sourceName, step.Id, "why");
            RequireText(step.CompletionFeedback, sourceName, step.Id, "completionFeedback");
            if (step.Hints is null || step.Hints.Length != 2
                || step.Hints.Any(string.IsNullOrWhiteSpace))
                throw StepError(sourceName, step.Id, "exactly two non-empty hints are required");
            if (step.CompletionFacts is null || step.CompletionFacts.Length == 0)
                throw StepError(sourceName, step.Id, "at least one completion fact is required");
            if (step.CompletionFacts.Any(fact => !LessonConditionFacts.Supported.Contains(fact)))
            {
                var unknown = step.CompletionFacts.First(fact => !LessonConditionFacts.Supported.Contains(fact));
                throw StepError(sourceName, step.Id, $"unsupported completion fact '{unknown}'");
            }
            if (step.CompletionFacts.Distinct(StringComparer.Ordinal).Count() != step.CompletionFacts.Length)
                throw StepError(sourceName, step.Id, "completion facts must not repeat");

            var choices = ValidateChoices(sourceName, step);
            var action = ValidateAction(sourceName, step);
            steps.Add(new LessonStepDefinition(step.Id, step.Title, step.Explanation, step.Why,
                step.CompletionFeedback,
                Array.AsReadOnly(step.Hints), Array.AsReadOnly(step.CompletionFacts), choices, action));
        }

        if (document.RequiredFeature is not null
            && !Regex.IsMatch(document.RequiredFeature, @"^[A-Z][A-Z0-9]*\.[0-9]+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException(
                $"Lesson file '{sourceName}' has an invalid requiredFeature; use a roadmap feature ID such as 'M1.2'.");
        if (!document.Available && document.RequiredFeature is null)
            throw new InvalidDataException(
                $"Lesson file '{sourceName}' is unavailable but does not name the feature gate required to unlock it.");

        return new LessonDefinition(document.Id, document.Tier, document.Available,
            document.RequiredFeature, document.Title, document.TransferTask,
            document.Completion!.Title!, document.Completion.Explanation!, document.Completion.Why!,
            document.Completion.Hints![0], document.Completion.Hints[1], completionAction, steps.AsReadOnly());
    }

    private static IReadOnlyList<LessonChoiceDefinition> ValidateChoices(string sourceName,
        LessonStepDocument step)
    {
        var completionFacts = step.CompletionFacts
            ?? throw StepError(sourceName, step.Id, "completion facts are missing");
        if (step.Choices is null || step.Choices.Length == 0)
            return Array.Empty<LessonChoiceDefinition>();

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var correctCount = 0;
        var choices = new List<LessonChoiceDefinition>(step.Choices.Length);
        foreach (var choice in step.Choices)
        {
            if (choice is null)
                throw StepError(sourceName, step.Id, "choice cannot be null");
            RequireId(choice.Id, sourceName, step.Id, "choice ID");
            if (!ids.Add(choice.Id))
                throw StepError(sourceName, step.Id, $"choice ID '{choice.Id}' is repeated");
            RequireText(choice.Label, sourceName, step.Id, "choice label");
            RequireText(choice.Feedback, sourceName, step.Id, "choice feedback");
            if (choice.IsCorrect)
            {
                correctCount++;
                if (string.IsNullOrWhiteSpace(choice.CompletionFact)
                    || !completionFacts.Contains(choice.CompletionFact, StringComparer.Ordinal))
                    throw StepError(sourceName, step.Id,
                        $"correct choice '{choice.Id}' must add one of this step's completion facts");
            }
            else if (choice.CompletionFact is not null)
            {
                throw StepError(sourceName, step.Id,
                    $"incorrect choice '{choice.Id}' cannot add a completion fact");
            }

            choices.Add(new LessonChoiceDefinition(choice.Id, choice.Label, choice.Feedback,
                choice.IsCorrect, choice.CompletionFact));
        }

        if (choices.Count < 2 || correctCount != 1)
            throw StepError(sourceName, step.Id, "a question needs at least two choices and exactly one correct answer");
        return choices.AsReadOnly();
    }

    private static LessonStepActionDefinition? ValidateAction(string sourceName, LessonStepDocument step) =>
        ValidateAction(sourceName, step.Id, step.Action);

    private static LessonStepActionDefinition? ValidateAction(string sourceName, string stepId,
        ActionDocument? action)
    {
        if (action is null) return null;
        RequireId(action.Id, sourceName, stepId, "action ID");
        RequireText(action.Label, sourceName, stepId, "action label");
        if (!SupportedActions.Contains(action.Id))
            throw StepError(sourceName, stepId, $"unsupported action '{action.Id}'");
        return new LessonStepActionDefinition(action.Id, action.Label);
    }

    private static LessonStepActionDefinition? ValidateCompletionCopy(CompletionDocument? completion,
        string sourceName)
    {
        if (completion is null)
            throw new InvalidDataException($"Lesson file '{sourceName}' is missing completion text.");
        RequireText(completion.Title, sourceName, "complete", "title");
        RequireText(completion.Explanation, sourceName, "complete", "explanation");
        RequireText(completion.Why, sourceName, "complete", "why");
        if (completion.Hints is null || completion.Hints.Length != 2
            || completion.Hints.Any(string.IsNullOrWhiteSpace))
            throw StepError(sourceName, "complete", "exactly two non-empty hints are required");
        return ValidateAction(sourceName, "complete", completion.Action);
    }

    private static void RequireText(string? value, string sourceName, string stepId, string field) =>
        RequireText(value, $"Lesson file '{sourceName}', step '{stepId}'", field);

    private static void RequireText(string? value, string context, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{context} requires a non-empty {field}.");
    }

    private static void RequireId(string? value, string context) =>
        ValidateId(value, context, "ID");

    private static void RequireId(string? value, string sourceName, string stepId, string field) =>
        ValidateId(value, $"Lesson file '{sourceName}', step '{stepId}'", field);

    private static void ValidateId(string? value, string context, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64
            || value[0] is < 'a' or > 'z'
            || value.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidDataException($"{context} has an invalid {field}; use lowercase letters, digits and hyphens.");
    }

    private static InvalidDataException StepError(string sourceName, string stepId, string message) =>
        new($"Lesson file '{sourceName}', step '{stepId}': {message}.");

    private sealed class LessonDocument
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; } = string.Empty;
        public int Tier { get; set; }
        public bool Available { get; set; } = true;
        public string? RequiredFeature { get; set; }
        public string Title { get; set; } = string.Empty;
        public string TransferTask { get; set; } = string.Empty;
        public CompletionDocument? Completion { get; set; }
        public LessonStepDocument[]? Steps { get; set; }
    }

    private sealed class CompletionDocument
    {
        public string Title { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string Why { get; set; } = string.Empty;
        public string[]? Hints { get; set; }
        public ActionDocument? Action { get; set; }
    }

    private sealed class LessonStepDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string Why { get; set; } = string.Empty;
        public string CompletionFeedback { get; set; } = string.Empty;
        public string[]? Hints { get; set; }
        public string[]? CompletionFacts { get; set; }
        public ChoiceDocument[]? Choices { get; set; }
        public ActionDocument? Action { get; set; }
    }

    private sealed class ChoiceDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Feedback { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
        public string? CompletionFact { get; set; }
    }

    private sealed class ActionDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
