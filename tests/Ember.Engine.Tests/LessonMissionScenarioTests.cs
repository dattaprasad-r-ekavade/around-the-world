using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.Authoring;
using Ember.Project;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class LessonMissionScenarioTests
{
    private const int CurrentScenarioVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Fact]
    public void EveryBundledLessonHasACiReplayThatChecksItsCompletionSteps()
    {
        var definitions = LessonDefinitionCatalog.LoadBuiltIn()
            .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        var scenarioDirectory = Path.Combine(AppContext.BaseDirectory, "Lessons");
        Assert.True(Directory.Exists(scenarioDirectory),
            $"Lesson replay scripts were not copied to '{scenarioDirectory}'.");

        var scenarioFiles = Directory.GetFiles(scenarioDirectory, "*.mission-solution.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(scenarioFiles);

        var scenarios = scenarioFiles.Select(ReadScenario).ToArray();
        Assert.Equal(scenarios.Length, scenarios.Select(scenario => scenario.LessonId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(definitions.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
            scenarios.Select(scenario => scenario.LessonId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

        foreach (var scenario in scenarios)
        {
            Assert.True(definitions.TryGetValue(scenario.LessonId, out var definition),
                $"Replay '{scenario.LessonId}' has no bundled lesson definition.");
            ReplayScenario(scenario, definition!);
        }
    }

    private static LessonMissionScenario ReadScenario(string path)
    {
        LessonMissionScenario? scenario;
        try
        {
            scenario = JsonSerializer.Deserialize<LessonMissionScenario>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Mission replay '{path}' contains invalid JSON: {exception.Message}", exception);
        }

        if (scenario is null || scenario.SchemaVersion != CurrentScenarioVersion
            || string.IsNullOrWhiteSpace(scenario.LessonId)
            || scenario.InitialObjects is null || scenario.Actions is null
            || scenario.InitialObjects.Length == 0 || scenario.Actions.Length == 0)
            throw new InvalidDataException($"Mission replay '{path}' is missing its version, lesson ID, objects or actions.");

        return scenario;
    }

    private static void ReplayScenario(LessonMissionScenario scenario, LessonDefinition definition)
    {
        Assert.True(string.Equals(definition.Id, scenario.LessonId, StringComparison.OrdinalIgnoreCase),
            $"Mission replay '{scenario.LessonId}' does not match its lesson definition.");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "EmberMissionReplay", Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(temporaryRoot, "ember.project.json");
        var scenePath = Path.Combine(temporaryRoot, "Scenes", "Main.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        try
        {
            var scene = new SceneGraph();
            foreach (var initialObject in scenario.InitialObjects)
            {
                if (!Guid.TryParse(initialObject.Id, out var id) || string.IsNullOrWhiteSpace(initialObject.Name))
                    throw new InvalidDataException(
                        $"Mission replay '{scenario.LessonId}' has an invalid initial object '{initialObject.Id}'.");
                scene.Add(new SceneObject(id, initialObject.Name));
            }

            var history = new SceneCommandHistory();
            var lesson = new FirstCreationLesson(scene, projectPath, definition);
            for (var index = 0; index < scenario.Actions.Length; index++)
            {
                var action = scenario.Actions[index];
                ReplayAction(action, scene, history, lesson, projectPath, scenePath,
                    scenario.LessonId, index + 1);
                Assert.Equal(action.ExpectedStepId, lesson.CurrentStepId);
                Assert.Equal(action.ExpectedComplete, lesson.IsComplete);
            }

            Assert.True(lesson.IsComplete,
                $"Mission replay '{scenario.LessonId}' ended before the lesson's completion conditions were met.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void ReplayAction(LessonMissionAction action, SceneGraph scene,
        SceneCommandHistory history, FirstCreationLesson lesson,
        string projectPath, string scenePath, string lessonId, int actionNumber)
    {
        switch (action.Type)
        {
            case "add-object":
            {
                if (!Guid.TryParse(action.ObjectId, out var id) || string.IsNullOrWhiteSpace(action.Name))
                    throw InvalidAction(lessonId, actionNumber, "add-object requires a valid objectId and name");
                history.Execute(scene, new CreateSceneObjectCommand(new SceneObject(id, action.Name)));
                lesson.ObserveScene(scene);
                break;
            }
            case "move-object":
            {
                if (!Guid.TryParse(action.ObjectId, out var id) || action.Position is not { Length: 3 })
                    throw InvalidAction(lessonId, actionNumber, "move-object requires objectId and a three-value position");
                var item = scene.Find(id)
                    ?? throw InvalidAction(lessonId, actionNumber, $"scene object '{id}' does not exist");
                var before = CopyTransform(item.Transform);
                var after = CopyTransform(item.Transform);
                after.Position = new Vector3(action.Position[0], action.Position[1], action.Position[2]);
                history.Execute(scene, new TransformEditCommand(id, before, after));
                lesson.ObserveScene(scene);
                break;
            }
            case "answer-choice":
            {
                if (string.IsNullOrWhiteSpace(action.ChoiceId) || action.Accepted is null)
                    throw InvalidAction(lessonId, actionNumber, "answer-choice requires choiceId and accepted");
                Assert.Equal(action.Accepted.Value, lesson.AnswerChoice(action.ChoiceId));
                break;
            }
            case "playback":
                if (action.IsPlaying is null)
                    throw InvalidAction(lessonId, actionNumber, "playback requires isPlaying");
                lesson.ObservePlayback(action.IsPlaying.Value);
                break;
            case "undo":
            {
                if (action.Accepted is null)
                    throw InvalidAction(lessonId, actionNumber, "undo requires accepted");
                var succeeded = history.Undo(scene);
                Assert.Equal(action.Accepted.Value, lesson.ObserveUndo(scene, succeeded));
                break;
            }
            case "save-reopen":
                SceneFile.Save(scene, scenePath);
                history.MarkSaved();
                Assert.True(lesson.ObserveSaved(projectPath, scenePath, scene),
                    $"Mission replay '{lessonId}' did not accept a successful scene save.");
                var reopenedScene = SceneFile.Load(scenePath);
                Assert.True(lesson.ObserveReopened(projectPath, scenePath, reopenedScene),
                    $"Mission replay '{lessonId}' did not accept a valid scene reopen.");
                break;
            default:
                throw InvalidAction(lessonId, actionNumber, $"unsupported action '{action.Type}'");
        }
    }

    private static Transform CopyTransform(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static InvalidDataException InvalidAction(string lessonId, int actionNumber, string detail) =>
        new($"Mission replay '{lessonId}', action {actionNumber}: {detail}.");

    private sealed class LessonMissionScenario
    {
        public LessonMissionScenario() { }
        public int SchemaVersion { get; init; }
        public string LessonId { get; init; } = string.Empty;
        public LessonMissionObject[] InitialObjects { get; init; } = Array.Empty<LessonMissionObject>();
        public LessonMissionAction[] Actions { get; init; } = Array.Empty<LessonMissionAction>();
    }

    private sealed class LessonMissionObject
    {
        public LessonMissionObject() { }
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    private sealed class LessonMissionAction
    {
        public LessonMissionAction() { }
        public string Type { get; init; } = string.Empty;
        public string? ObjectId { get; init; }
        public string? Name { get; init; }
        public float[]? Position { get; init; }
        public string? ChoiceId { get; init; }
        public bool? Accepted { get; init; }
        public bool? IsPlaying { get; init; }
        public string? ExpectedStepId { get; init; }
        public bool ExpectedComplete { get; init; }
    }
}
