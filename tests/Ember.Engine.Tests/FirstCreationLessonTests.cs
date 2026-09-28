using System;
using System.IO;
using Ember.Authoring;
using Ember.Project;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class FirstCreationLessonTests
{
    [Fact]
    public void LessonAdvancesOnlyAfterAuthoredActionsPredictionPlayUndoVariationAndReopen()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "ember-first-creation", "Game", EngineProjectFile.DefaultFileName);
        var scenePath = Path.Combine(Path.GetDirectoryName(projectPath)!, "Scenes", "Main.json");
        var scene = new SceneGraph();
        var other = new SceneObject(Guid.NewGuid(), "Starter object");
        scene.Add(other);
        var lesson = new FirstCreationLesson(scene, projectPath);
        var history = new SceneCommandHistory();

        Assert.Equal(FirstCreationLessonStep.AddObject, lesson.Step);
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.AddObject, lesson.Step);

        var added = new SceneObject(Guid.NewGuid(), "New object");
        history.Execute(scene, new CreateSceneObjectCommand(added));
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.MoveObject, lesson.Step);

        var beforeMove = new Transform();
        var afterMove = new Transform { Position = new Vector3(25f, 0f, 0f) };
        history.Execute(scene, new TransformEditCommand(added.Id, beforeMove, afterMove));
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.PredictPlay, lesson.Step);

        Assert.False(lesson.AnswerPlayPrediction(savedSceneChanges: true));
        Assert.Equal(FirstCreationLessonStep.PredictPlay, lesson.Step);
        Assert.True(lesson.AnswerPlayPrediction(savedSceneChanges: false));
        lesson.ObservePlayback(isPlaying: true);
        lesson.ObservePlayback(isPlaying: false);
        Assert.Equal(FirstCreationLessonStep.Reflect, lesson.Step);

        Assert.False(lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: false));
        Assert.True(lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: true));
        Assert.Equal(FirstCreationLessonStep.Undo, lesson.Step);

        Assert.True(history.Undo(scene));
        Assert.True(lesson.ObserveUndo(scene, undoSucceeded: true));
        Assert.Equal(FirstCreationLessonStep.TryAnotherObject, lesson.Step);

        var otherBefore = new Transform();
        var otherRotated = new Transform
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.ToRadians(15f))
        };
        history.Execute(scene, new TransformEditCommand(other.Id, otherBefore, otherRotated));
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.TryAnotherObject, lesson.Step);

        var otherAfter = new Transform
        {
            Position = new Vector3(0f, 0f, 25f),
            Rotation = otherRotated.Rotation
        };
        history.Execute(scene, new TransformEditCommand(other.Id, otherRotated, otherAfter));
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.Save, lesson.Step);

        Assert.False(lesson.ObserveSaved(projectPath + ".other", scenePath, scene));
        Assert.True(lesson.ObserveSaved(projectPath, scenePath, scene));
        Assert.Equal(FirstCreationLessonStep.Reopen, lesson.Step);

        var reopened = SceneGraphCloner.Clone(scene);
        Assert.False(lesson.ObserveReopened(projectPath + ".other", scenePath, reopened));
        var reopenedObject = reopened.Find(other.Id)!;
        reopenedObject.Transform.Position += Vector3.One;
        Assert.False(lesson.ObserveReopened(projectPath, scenePath, reopened));
        Assert.Equal(FirstCreationLessonStep.Reopen, lesson.Step);

        Assert.True(lesson.ObserveReopened(projectPath, scenePath, SceneGraphCloner.Clone(scene)));
        Assert.Equal(FirstCreationLessonStep.Complete, lesson.Step);
    }

    [Fact]
    public void LessonDoesNotCountPlayThatHappenedBeforeThePrediction()
    {
        var scene = new SceneGraph();
        var lesson = new FirstCreationLesson(scene,
            Path.Combine(Path.GetTempPath(), "ember-first-creation", EngineProjectFile.DefaultFileName));
        var item = new SceneObject(Guid.NewGuid(), "New object");
        scene.Add(item);
        lesson.ObserveScene(scene);
        item.Transform.Position = Vector3.UnitX;
        lesson.ObserveScene(scene);

        lesson.ObservePlayback(isPlaying: true);
        lesson.ObservePlayback(isPlaying: false);
        Assert.Equal(FirstCreationLessonStep.PredictPlay, lesson.Step);
        lesson.AnswerPlayPrediction(savedSceneChanges: false);
        Assert.Equal(FirstCreationLessonStep.Play, lesson.Step);
    }

    [Fact]
    public void LessonRequiresPositionChangesAndRestartsWhenTrackedObjectIsRemoved()
    {
        var scene = new SceneGraph();
        var lesson = new FirstCreationLesson(scene,
            Path.Combine(Path.GetTempPath(), "ember-first-creation", EngineProjectFile.DefaultFileName));
        var item = new SceneObject(Guid.NewGuid(), "New object");
        scene.Add(item);
        lesson.ObserveScene(scene);

        item.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.ToRadians(15f));
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.MoveObject, lesson.Step);

        item.Transform.Position = Vector3.UnitX;
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.PredictPlay, lesson.Step);

        scene.Remove(item.Id);
        lesson.ObserveScene(scene);
        Assert.Equal(FirstCreationLessonStep.AddObject, lesson.Step);
        Assert.Contains("object was removed", lesson.Feedback, StringComparison.OrdinalIgnoreCase);
    }
}
