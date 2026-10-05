using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Authoring;

public enum FirstCreationLessonStep
{
    AddObject,
    MoveObject,
    PredictPlay,
    Play,
    Reflect,
    Undo,
    TryAnotherObject,
    Save,
    Reopen,
    Complete,
    Custom
}

/// <summary>Tracks lesson facts from authored scene state, Play events, history and project files.</summary>
public sealed class FirstCreationLesson
{
    public const int TotalSteps = 9;

    private readonly HashSet<Guid> _startingObjectIds;
    private readonly LessonDefinition _definition;
    private readonly string _projectFilePath;
    private readonly Dictionary<Guid, TransformSnapshot> _variationStart = new();
    private readonly Dictionary<Guid, TransformSnapshot> _genericStart = new();
    private readonly HashSet<string> _completedFacts = new(StringComparer.Ordinal);
    private Guid? _lessonObjectId;
    private TransformSnapshot? _lessonObjectStart;
    private Guid? _firstMovedObjectId;
    private string? _savedScenePath;
    private string? _savedSceneJson;

    public FirstCreationLesson(SceneGraph startingScene, string projectFilePath)
        : this(startingScene, projectFilePath, FindBuiltInDefinition("first-creation"))
    {
    }

    public FirstCreationLesson(SceneGraph startingScene, string projectFilePath,
        LessonDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(startingScene);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("A project file path is required to start the lesson.", nameof(projectFilePath));

        _definition = definition;
        _projectFilePath = Path.GetFullPath(projectFilePath);
        _startingObjectIds = startingScene.Objects.Select(item => item.Id).ToHashSet();
        foreach (var item in startingScene.Objects)
            _genericStart[item.Id] = TransformSnapshot.From(item.Transform);
        ProjectFilePath = _projectFilePath;
    }

    public string ProjectFilePath { get; }
    public LessonDefinition Definition => _definition;
    public LessonStepDefinition? CurrentStep => CurrentStepIndex < StepCount
        ? _definition.Steps[CurrentStepIndex]
        : null;
    public string? CurrentStepId => CurrentStep?.Id;
    public bool IsComplete => CurrentStepIndex >= StepCount;
    public int StepCount => _definition.Steps.Count;
    public int StepNumber => IsComplete ? StepCount : CurrentStepIndex + 1;
    public string Title => CurrentStep?.Title ?? _definition.CompletionTitle;
    public string Explanation => CurrentStep?.Explanation ?? _definition.CompletionExplanation;
    public string Why => CurrentStep?.Why ?? _definition.CompletionWhy;
    public string Hint => CurrentStep?.Hints[0] ?? _definition.CompletionHint;
    public string MoreSpecificHint => CurrentStep?.Hints[1] ?? _definition.CompletionMoreSpecificHint;
    public IReadOnlyList<LessonChoiceDefinition> Choices =>
        CurrentStep?.Choices ?? Array.Empty<LessonChoiceDefinition>();
    public LessonStepActionDefinition? Action => CurrentStep?.Action;
    public string TransferTask => _definition.TransferTask;
    public string? Feedback { get; private set; }

    /// <summary>Legacy view for existing callers; data-driven lessons use CurrentStepId.</summary>
    public FirstCreationLessonStep Step => CurrentStep?.Id switch
    {
        null => FirstCreationLessonStep.Complete,
        "add-object" => FirstCreationLessonStep.AddObject,
        "move-object" => FirstCreationLessonStep.MoveObject,
        "predict-play" => FirstCreationLessonStep.PredictPlay,
        "play" => FirstCreationLessonStep.Play,
        "reflect" => FirstCreationLessonStep.Reflect,
        "undo" => FirstCreationLessonStep.Undo,
        "try-another-object" => FirstCreationLessonStep.TryAnotherObject,
        "save" => FirstCreationLessonStep.Save,
        "reopen" => FirstCreationLessonStep.Reopen,
        _ => FirstCreationLessonStep.Custom
    };

    private int CurrentStepIndex
    {
        get
        {
            for (var index = 0; index < _definition.Steps.Count; index++)
                if (_definition.Steps[index].CompletionFacts.Any(fact => !_completedFacts.Contains(fact)))
                    return index;
            return _definition.Steps.Count;
        }
    }

    private static LessonDefinition FindBuiltInDefinition(string id) =>
        LessonDefinitionCatalog.LoadBuiltIn().FirstOrDefault(lesson =>
            string.Equals(lesson.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Bundled lesson '{id}' was not found.");

    /// <summary>Call after normal editor actions; completion facts are inferred from authored state.</summary>
    public void ObserveScene(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_lessonObjectId is { } trackedId
            && scene.Find(trackedId) is null
            && !IsComplete)
        {
            RestartAfterLessonObjectRemoval(scene);
            Feedback = "The lesson object was removed. Add another object to continue; your other scene edits are unchanged.";
        }

        if (_lessonObjectId is null)
        {
            var added = scene.Objects.FirstOrDefault(item => !_startingObjectIds.Contains(item.Id));
            if (added is not null)
            {
                _lessonObjectId = added.Id;
                _lessonObjectStart = TransformSnapshot.From(added.Transform);
                RecordFact(LessonConditionFacts.ObjectAdded);
            }
        }

        if (_lessonObjectId is { } lessonObjectId
            && !_completedFacts.Contains(LessonConditionFacts.TrackedObjectPositionChanged)
            && _lessonObjectStart is { } start
            && scene.Find(lessonObjectId) is { } lessonObject
            && TransformSnapshot.From(lessonObject.Transform).Position != start.Position)
        {
            RecordFact(LessonConditionFacts.TrackedObjectPositionChanged);
        }

        ObserveGenericPositionFacts(scene);
        ObserveDifferentObjectAfterUndo(scene);
    }

    private void ObserveGenericPositionFacts(SceneGraph scene)
    {
        foreach (var item in scene.Objects)
        {
            var current = TransformSnapshot.From(item.Transform);
            if (!_genericStart.TryGetValue(item.Id, out var before))
            {
                _genericStart[item.Id] = current;
                continue;
            }
            if (current.Position == before.Position) continue;

            if (_firstMovedObjectId is null)
            {
                _firstMovedObjectId = item.Id;
                RecordFact(LessonConditionFacts.AnyObjectPositionChanged);
            }
            else if (_firstMovedObjectId != item.Id)
            {
                RecordFact(LessonConditionFacts.DifferentObjectPositionChanged);
            }
        }

        foreach (var removedId in _genericStart.Keys.Where(id => scene.Find(id) is null).ToArray())
            _genericStart.Remove(removedId);
    }

    private void ObserveDifferentObjectAfterUndo(SceneGraph scene)
    {
        if (!_completedFacts.Contains(LessonConditionFacts.TrackedMoveUndone)
            || _completedFacts.Contains(LessonConditionFacts.OtherObjectPositionChangedAfterUndo)
            || _lessonObjectId is not { } trackedId) return;

        foreach (var item in scene.Objects)
        {
            if (item.Id == trackedId) continue;
            var current = TransformSnapshot.From(item.Transform);
            if (_variationStart.TryGetValue(item.Id, out var before)
                && current.Position != before.Position)
            {
                RecordFact(LessonConditionFacts.OtherObjectPositionChangedAfterUndo);
                return;
            }

            // A new object becomes eligible only after its initial scene state is observed.
            if (!_variationStart.ContainsKey(item.Id)) _variationStart[item.Id] = current;
        }
    }

    /// <summary>Accepts a choice declared by the current lesson step.</summary>
    public bool AnswerChoice(string choiceId)
    {
        if (string.IsNullOrWhiteSpace(choiceId) || CurrentStep is not { } step) return false;
        var choice = step.Choices.FirstOrDefault(item =>
            string.Equals(item.Id, choiceId, StringComparison.Ordinal));
        if (choice is null) return false;
        if (!choice.IsCorrect)
        {
            Feedback = choice.Feedback;
            return false;
        }
        if (choice.CompletionFact is { } fact) RecordFact(fact);
        Feedback = choice.Feedback;
        return true;
    }

    /// <summary>Returns true only for the correct prediction that Play uses a temporary copy.</summary>
    public bool AnswerPlayPrediction(bool savedSceneChanges)
    {
        var choice = Choices.FirstOrDefault(item => item.IsCorrect != savedSceneChanges);
        return choice is not null && AnswerChoice(choice.Id);
    }

    /// <summary>Call with the actual editor Play state after each UI update.</summary>
    public void ObservePlayback(bool isPlaying)
    {
        if (!_completedFacts.Contains(LessonConditionFacts.PredictionCorrect)) return;
        if (isPlaying)
        {
            RecordFact(LessonConditionFacts.PlayStartedAfterPrediction);
            Feedback = "The temporary scene is running. Stop when you are ready to reflect.";
        }
        else if (_completedFacts.Contains(LessonConditionFacts.PlayStartedAfterPrediction))
        {
            RecordFact(LessonConditionFacts.PlayStoppedAfterStart);
        }
    }

    /// <summary>Returns true only for recognizing that stopping Play leaves authored edits intact.</summary>
    public bool AnswerPlayReflection(bool authoredSceneStayedUnchanged)
    {
        var choice = Choices.FirstOrDefault(item => item.IsCorrect == authoredSceneStayedUnchanged);
        return choice is not null && AnswerChoice(choice.Id);
    }

    /// <summary>Call after a real Undo; advances only if it restored the tracked object's position.</summary>
    public bool ObserveUndo(SceneGraph scene, bool undoSucceeded)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!CurrentStepRequires(LessonConditionFacts.TrackedMoveUndone)) return false;
        if (!undoSucceeded || _lessonObjectId is not { } id
            || _lessonObjectStart is not { } start
            || scene.Find(id) is not { } item
            || TransformSnapshot.From(item.Transform).Position != start.Position)
        {
            Feedback = "Undo the move you made so the object returns to its earlier position.";
            return false;
        }

        RecordFact(LessonConditionFacts.TrackedMoveUndone);
        _variationStart.Clear();
        foreach (var other in scene.Objects)
            if (other.Id != id) _variationStart[other.Id] = TransformSnapshot.From(other.Transform);
        return true;
    }

    /// <summary>Call only after the normal project save operation succeeds.</summary>
    public bool ObserveSaved(string projectFilePath, string scenePath, SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!CurrentStepRequires(LessonConditionFacts.ProjectSceneSaved)) return false;
        if (!SamePath(projectFilePath, _projectFilePath)
            || string.IsNullOrWhiteSpace(scenePath))
        {
            Feedback = "Finish the lesson change, then save this project scene.";
            return false;
        }

        _savedScenePath = Path.GetFullPath(scenePath);
        _savedSceneJson = CaptureLessonSceneData(scene);
        RecordFact(LessonConditionFacts.ProjectSceneSaved);
        return true;
    }

    /// <summary>Completes only when the same saved project scene reopens with its data intact.</summary>
    public bool ObserveReopened(string projectFilePath, string scenePath, SceneGraph reopenedScene)
    {
        ArgumentNullException.ThrowIfNull(reopenedScene);
        if (!CurrentStepRequires(LessonConditionFacts.SameProjectSceneReopenedUnchanged)
            || !SamePath(projectFilePath, _projectFilePath)
            || string.IsNullOrWhiteSpace(scenePath)
            || !SamePath(scenePath, _savedScenePath)
            || !string.Equals(CaptureLessonSceneData(reopenedScene), _savedSceneJson, StringComparison.Ordinal))
        {
            Feedback = "Open the same project and scene you just saved; the lesson checks their saved object data.";
            return false;
        }

        RecordFact(LessonConditionFacts.SameProjectSceneReopenedUnchanged);
        return true;
    }

    private void RecordFact(string fact)
    {
        var currentIndex = CurrentStepIndex;
        _completedFacts.Add(fact);
        var nextIndex = CurrentStepIndex;
        if (nextIndex > currentIndex)
            Feedback = _definition.Steps[Math.Min(nextIndex - 1, StepCount - 1)].CompletionFeedback;
    }

    private bool CurrentStepRequires(string fact) =>
        CurrentStep?.CompletionFacts.Contains(fact, StringComparer.Ordinal) == true;

    private static bool SamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }

    private void RestartAfterLessonObjectRemoval(SceneGraph scene)
    {
        _startingObjectIds.UnionWith(scene.Objects.Select(item => item.Id));
        _lessonObjectId = null;
        _lessonObjectStart = null;
        _variationStart.Clear();
        _genericStart.Clear();
        _completedFacts.Clear();
        _firstMovedObjectId = null;
        foreach (var item in scene.Objects)
            _genericStart[item.Id] = TransformSnapshot.From(item.Transform);
        _savedScenePath = null;
        _savedSceneJson = null;
    }

    private static string CaptureLessonSceneData(SceneGraph scene) => JsonSerializer.Serialize(
        scene.Objects.OrderBy(item => item.Id).Select(item => new SavedObject(
            item.Id, item.Name, item.Enabled, item.ParentId,
            item.Transform.Position.X, item.Transform.Position.Y, item.Transform.Position.Z,
            item.Transform.Rotation.X, item.Transform.Rotation.Y,
            item.Transform.Rotation.Z, item.Transform.Rotation.W,
            item.Transform.Scale.X, item.Transform.Scale.Y, item.Transform.Scale.Z,
            item.GltfAsset?.AssetId, item.GltfAsset?.SourcePath,
            item.StaticMeshLod?.NearAsset.AssetId, item.StaticMeshLod?.NearAsset.SourcePath,
            item.StaticMeshLod?.FarAsset.AssetId, item.StaticMeshLod?.FarAsset.SourcePath,
            item.StaticMeshLod?.EnterFarDistance, item.StaticMeshLod?.ExitFarDistance,
            item.CharacterSettings?.ClipName, item.CharacterSettings?.CrossfadeClipName,
            item.CharacterSettings?.Speed, item.CharacterSettings?.Loop,
            item.CharacterSettings?.BlendAmount)).ToArray());

    private sealed record SavedObject(Guid Id, string Name, bool Enabled, Guid? ParentId,
        float PositionX, float PositionY, float PositionZ,
        float RotationX, float RotationY, float RotationZ, float RotationW,
        float ScaleX, float ScaleY, float ScaleZ,
        Guid? AssetId, string? AssetPath,
        Guid? NearAssetId, string? NearAssetPath, Guid? FarAssetId, string? FarAssetPath,
        float? EnterFarDistance, float? ExitFarDistance,
        string? ClipName, string? CrossfadeClipName, float? AnimationSpeed,
        bool? AnimationLoop, float? BlendAmount);

    private readonly record struct TransformSnapshot(Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        public static TransformSnapshot From(Transform transform) =>
            new(transform.Position, transform.Rotation, transform.Scale);
    }
}
