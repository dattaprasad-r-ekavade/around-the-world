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
    Complete
}

/// <summary>Tracks an optional first-creation lesson from changes to the real authored scene.</summary>
public sealed class FirstCreationLesson
{
    public const int TotalSteps = 9;

    private readonly HashSet<Guid> _startingObjectIds;
    private readonly string _projectFilePath;
    private readonly Dictionary<Guid, TransformSnapshot> _variationStart = new();
    private Guid? _lessonObjectId;
    private TransformSnapshot? _lessonObjectStart;
    private bool _hasMoved;
    private bool _predictionCorrect;
    private bool _hasPlayed;
    private bool _hasStopped;
    private bool _reflectionCorrect;
    private bool _undoRestoredMove;
    private bool _variedAnotherObject;
    private bool _saved;
    private bool _reopened;
    private string? _savedScenePath;
    private string? _savedSceneJson;

    public FirstCreationLesson(SceneGraph startingScene, string projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(startingScene);
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("A project file path is required to start the lesson.", nameof(projectFilePath));

        _projectFilePath = Path.GetFullPath(projectFilePath);
        _startingObjectIds = startingScene.Objects.Select(item => item.Id).ToHashSet();
        ProjectFilePath = _projectFilePath;
    }

    public string ProjectFilePath { get; }
    public FirstCreationLessonStep Step
    {
        get
        {
            if (_lessonObjectId is null) return FirstCreationLessonStep.AddObject;
            if (!_hasMoved) return FirstCreationLessonStep.MoveObject;
            if (!_predictionCorrect) return FirstCreationLessonStep.PredictPlay;
            if (!_hasPlayed || !_hasStopped) return FirstCreationLessonStep.Play;
            if (!_reflectionCorrect) return FirstCreationLessonStep.Reflect;
            if (!_undoRestoredMove) return FirstCreationLessonStep.Undo;
            if (!_variedAnotherObject) return FirstCreationLessonStep.TryAnotherObject;
            if (!_saved) return FirstCreationLessonStep.Save;
            if (!_reopened) return FirstCreationLessonStep.Reopen;
            return FirstCreationLessonStep.Complete;
        }
    }

    public int StepNumber => Step switch
    {
        FirstCreationLessonStep.AddObject => 1,
        FirstCreationLessonStep.MoveObject => 2,
        FirstCreationLessonStep.PredictPlay => 3,
        FirstCreationLessonStep.Play => 4,
        FirstCreationLessonStep.Reflect => 5,
        FirstCreationLessonStep.Undo => 6,
        FirstCreationLessonStep.TryAnotherObject => 7,
        FirstCreationLessonStep.Save => 8,
        FirstCreationLessonStep.Reopen => 9,
        _ => TotalSteps
    };

    public string Title => Step switch
    {
        FirstCreationLessonStep.AddObject => "Add an object",
        FirstCreationLessonStep.MoveObject => "Change its position",
        FirstCreationLessonStep.PredictPlay => "Make a prediction",
        FirstCreationLessonStep.Play => "Try the scene",
        FirstCreationLessonStep.Reflect => "What happened?",
        FirstCreationLessonStep.Undo => "Undo the change",
        FirstCreationLessonStep.TryAnotherObject => "Try it on something else",
        FirstCreationLessonStep.Save => "Keep your work",
        FirstCreationLessonStep.Reopen => "Check it after reopening",
        _ => "You made your first scene"
    };

    public string Explanation => Step switch
    {
        FirstCreationLessonStep.AddObject => "A scene object is one placed thing. Add a model or an empty object to this scene.",
        FirstCreationLessonStep.MoveObject => "Move changes where this placed object sits. The reusable model stays the same.",
        FirstCreationLessonStep.PredictPlay => "Before you press Play, decide whether it changes your saved scene or a temporary copy.",
        FirstCreationLessonStep.Play => "Press Play, try the scene, then press Stop to return to editing.",
        FirstCreationLessonStep.Reflect => "When you stopped Play, what happened to the scene you were editing?",
        FirstCreationLessonStep.Undo => "Undo reverses an edit. Undo the move and watch the object return to its earlier position.",
        FirstCreationLessonStep.TryAnotherObject => "Change a different object. The same Move tool works on every placed object.",
        FirstCreationLessonStep.Save => "Save writes the objects and their positions into your project scene.",
        FirstCreationLessonStep.Reopen => "Reopen this project to check that the saved objects and positions return.",
        _ => "You added, changed, tested, undid, saved and reopened a scene. Try the same idea on another object."
    };

    public string Why => Step switch
    {
        FirstCreationLessonStep.AddObject => "A scene is made from placed objects. Each one can have its own position and settings.",
        FirstCreationLessonStep.MoveObject => "Position lets you arrange a world while reusing the same model in different places.",
        FirstCreationLessonStep.PredictPlay => "Predicting first helps you notice whether a test changes your saved work.",
        FirstCreationLessonStep.Play => "Trying the scene shows how your change feels before you decide to keep it.",
        FirstCreationLessonStep.Reflect => "Comparing the result with your prediction teaches you how preview and editing differ.",
        FirstCreationLessonStep.Undo => "Undo makes experimentation safer because you can return to an earlier edit.",
        FirstCreationLessonStep.TryAnotherObject => "Trying the same tool on another object shows the idea works across the scene.",
        FirstCreationLessonStep.Save => "Saving keeps the scene data with the project so you can return to it later.",
        FirstCreationLessonStep.Reopen => "Reopening checks that your project really kept the scene you made.",
        _ => "You used the same edit, preview and save loop that creators use to build games and films."
    };

    public string Hint => Step switch
    {
        FirstCreationLessonStep.AddObject => "Look in the Add panel for a way to place something new.",
        FirstCreationLessonStep.MoveObject => "Select the object you added, then choose Move in the Inspector.",
        FirstCreationLessonStep.PredictPlay => "Think about what should happen to your saved scene when you test it.",
        FirstCreationLessonStep.Play => "Play and Stop are on the top bar. Stop returns to the authored scene.",
        FirstCreationLessonStep.Reflect => "Compare the scene before Play with the one you see after Stop.",
        FirstCreationLessonStep.Undo => "Use Undo on the top bar, then check where the object moved.",
        FirstCreationLessonStep.TryAnotherObject => "Select a different object, then choose Move in the Inspector.",
        FirstCreationLessonStep.Save => "Use Save on the top bar to keep your scene in this project.",
        FirstCreationLessonStep.Reopen => "Use Reopen this project below; it opens the current project without typing a path.",
        _ => "Replay the guide any time, or keep creating freely."
    };

    public string MoreSpecificHint => Step switch
    {
        FirstCreationLessonStep.AddObject => "Click Add empty object, or choose Browse for a model, preview it and click Add to scene.",
        FirstCreationLessonStep.MoveObject => "With your object selected, click one of the X, Y or Z direction buttons under Move.",
        FirstCreationLessonStep.PredictPlay => "Play tests a temporary copy. Your authored scene should still be there when you stop.",
        FirstCreationLessonStep.Play => "Press Play, try the scene, then press Stop to return to editing.",
        FirstCreationLessonStep.Reflect => "Play changes are temporary; stopping returns to the authored scene.",
        FirstCreationLessonStep.Undo => "Click Undo once and confirm the object returns to the position it had before the move.",
        FirstCreationLessonStep.TryAnotherObject => "Select another object, leave Move selected, then click one axis direction button.",
        FirstCreationLessonStep.Save => "Wait for the Saved message. If an error appears, fix it before reopening.",
        FirstCreationLessonStep.Reopen => "This reloads the project's startup scene and checks that the saved objects and positions return.",
        _ => "Replay the guide any time, or keep creating freely."
    };

    public string? Feedback { get; private set; }

    /// <summary>Call after normal editor actions; completion is inferred from authored object state.</summary>
    public void ObserveScene(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_lessonObjectId is { } trackedId
            && scene.Find(trackedId) is null
            && Step != FirstCreationLessonStep.Complete)
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
                Feedback = "Object added. Select it and change its position.";
            }
            return;
        }

        if (!_hasMoved && scene.Find(_lessonObjectId.Value) is { } lessonObject
            && _lessonObjectStart is { } start
            && TransformSnapshot.From(lessonObject.Transform).Position != start.Position)
        {
            _hasMoved = true;
            Feedback = "You changed this object's position. Next, predict what Play will do.";
        }

        if (!_undoRestoredMove || _variedAnotherObject) return;
        foreach (var item in scene.Objects)
        {
            if (item.Id == _lessonObjectId.Value) continue;
            var current = TransformSnapshot.From(item.Transform);
            if (_variationStart.TryGetValue(item.Id, out var before)
                && current.Position != before.Position)
            {
                _variedAnotherObject = true;
                Feedback = "You changed a different object too. The same tool works across the scene.";
                return;
            }

            // A new object is eligible for the variation only after it has appeared in the scene.
            if (!_variationStart.ContainsKey(item.Id)) _variationStart[item.Id] = current;
        }
    }

    /// <summary>Returns true only for the correct prediction that Play uses a temporary copy.</summary>
    public bool AnswerPlayPrediction(bool savedSceneChanges)
    {
        if (Step != FirstCreationLessonStep.PredictPlay) return false;
        if (savedSceneChanges)
        {
            Feedback = "Play is temporary. Your authored scene remains available when you stop.";
            return false;
        }

        _predictionCorrect = true;
        Feedback = "Right: Play tests a temporary copy. Now try it and stop.";
        return true;
    }

    /// <summary>Call with the actual editor Play state after each UI update.</summary>
    public void ObservePlayback(bool isPlaying)
    {
        if (!_predictionCorrect) return;
        if (isPlaying)
        {
            _hasPlayed = true;
            Feedback = "The temporary scene is running. Stop when you are ready to reflect.";
        }
        else if (_hasPlayed)
        {
            _hasStopped = true;
            Feedback = "You returned to the authored scene. Think about what remained.";
        }
    }

    /// <summary>Returns true only for recognizing that stopping Play leaves authored edits intact.</summary>
    public bool AnswerPlayReflection(bool authoredSceneStayedUnchanged)
    {
        if (Step != FirstCreationLessonStep.Reflect) return false;
        if (!authoredSceneStayedUnchanged)
        {
            Feedback = "Stopping Play restores the authored scene; try that answer again.";
            return false;
        }

        _reflectionCorrect = true;
        Feedback = "Yes. The test run was temporary, so your authored scene stayed intact.";
        return true;
    }

    /// <summary>Call after a real Undo; advances only if it restored the lesson object's move.</summary>
    public bool ObserveUndo(SceneGraph scene, bool undoSucceeded)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (Step != FirstCreationLessonStep.Undo) return false;
        if (!undoSucceeded || _lessonObjectId is not { } id
            || _lessonObjectStart is not { } start
            || scene.Find(id) is not { } item
            || TransformSnapshot.From(item.Transform).Position != start.Position)
        {
            Feedback = "Undo the move you made so the object returns to its earlier position.";
            return false;
        }

        _undoRestoredMove = true;
        _variationStart.Clear();
        foreach (var other in scene.Objects)
            if (other.Id != id) _variationStart[other.Id] = TransformSnapshot.From(other.Transform);
        Feedback = "The move was undone. Try a direction on a different object.";
        return true;
    }

    /// <summary>Call only after the normal project save operation succeeds.</summary>
    public bool ObserveSaved(string projectFilePath, string scenePath, SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (Step != FirstCreationLessonStep.Save) return false;
        if (!SamePath(projectFilePath, _projectFilePath)
            || string.IsNullOrWhiteSpace(scenePath))
        {
            Feedback = "Finish the different-object change, then save this project scene.";
            return false;
        }

        _savedScenePath = Path.GetFullPath(scenePath);
        _savedSceneJson = CaptureLessonSceneData(scene);
        _saved = true;
        Feedback = "Saved. Reopen this project to check that the scene comes back.";
        return true;
    }

    /// <summary>Completes only when the same saved project scene reopens with the authored data intact.</summary>
    public bool ObserveReopened(string projectFilePath, string scenePath, SceneGraph reopenedScene)
    {
        ArgumentNullException.ThrowIfNull(reopenedScene);
        if (Step != FirstCreationLessonStep.Reopen || !SamePath(projectFilePath, _projectFilePath)
            || string.IsNullOrWhiteSpace(scenePath)
            || !SamePath(scenePath, _savedScenePath)
            || !string.Equals(CaptureLessonSceneData(reopenedScene), _savedSceneJson, StringComparison.Ordinal))
        {
            Feedback = "Open the same project and scene you just saved; the lesson checks their saved object data.";
            return false;
        }

        _reopened = true;
        Feedback = "Your scene reopened with the same objects and positions. You can replay the guide or keep creating.";
        return true;
    }

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
        _hasMoved = false;
        _predictionCorrect = false;
        _hasPlayed = false;
        _hasStopped = false;
        _reflectionCorrect = false;
        _undoRestoredMove = false;
        _variedAnotherObject = false;
        _saved = false;
        _reopened = false;
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
