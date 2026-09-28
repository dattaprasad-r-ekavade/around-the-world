using System;
using System.IO;
using Ember.Authoring;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneTemplateFileTests
{
    [Fact]
    public void SavesAndReloadsOnlyTheSelectedHierarchyWithItsStableObjectIds()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var scene = new SceneGraph();
            var outside = new SceneObject(Guid.NewGuid(), "Workshop")
            {
                Transform = new Transform { Position = new Vector3(8f, 0f, 0f) }
            };
            var root = new SceneObject(Guid.NewGuid(), "Workbench")
            {
                Transform = new Transform { Position = new Vector3(1f, 2f, 3f) }
            };
            var child = new SceneObject(Guid.NewGuid(), "Lamp")
            {
                Transform = new Transform { Position = new Vector3(0f, 4f, 0f) }
            };
            var unrelated = new SceneObject(Guid.NewGuid(), "Window");
            scene.Add(outside);
            scene.Add(root);
            scene.Add(child);
            scene.Add(unrelated);
            scene.SetParent(root.Id, outside.Id);
            scene.SetParent(child.Id, root.Id);

            var saved = SceneTemplateFile.Save(scene, root.Id, "Workbench set", path);
            var loaded = SceneTemplateFile.Load(path);

            Assert.Equal(saved.Id, loaded.Id);
            Assert.Equal(1, loaded.Revision);
            Assert.Equal("Workbench set", loaded.Name);
            Assert.Equal(root.Id, loaded.RootObjectId);
            Assert.Equal(2, loaded.Scene.Objects.Count);
            Assert.Null(loaded.Scene.Find(root.Id)!.ParentId);
            Assert.Equal(root.Id, loaded.Scene.Find(child.Id)!.ParentId);
            Assert.Equal(new Vector3(1f, 2f, 3f), loaded.Scene.Find(root.Id)!.Transform.Position);
            Assert.Null(loaded.Scene.Find(outside.Id));
            Assert.Null(loaded.Scene.Find(unrelated.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SavingOverTemplateKeepsItsIdAndAdvancesTheRevision()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var scene = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "First name");
            scene.Add(root);
            var first = SceneTemplateFile.Save(scene, root.Id, "First", path);
            root.Name = "Updated name";

            var updated = SceneTemplateFile.Save(scene, root.Id, "Updated", path);
            var loaded = SceneTemplateFile.Load(path);

            Assert.Equal(first.Id, updated.Id);
            Assert.Equal(first.Id, loaded.Id);
            Assert.Equal(2, updated.Revision);
            Assert.Equal(2, loaded.Revision);
            Assert.Equal("Updated", loaded.Name);
            Assert.Equal("Updated name", loaded.Scene.Find(root.Id)!.Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FailedSaveDoesNotReplaceTheLastValidTemplate()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var scene = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Valid");
            scene.Add(root);
            var first = SceneTemplateFile.Save(scene, root.Id, "Valid template", path);
            root.Name = " ";

            Assert.Throws<InvalidDataException>(() =>
                SceneTemplateFile.Save(scene, root.Id, "Broken replacement", path));

            var loaded = SceneTemplateFile.Load(path);
            Assert.Equal(first.Id, loaded.Id);
            Assert.Equal(1, loaded.Revision);
            Assert.Equal("Valid", loaded.Scene.Find(root.Id)!.Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsUnsupportedTemplateVersionsAndMissingRoots()
    {
        var path = TemporaryTemplatePath();
        try
        {
            File.WriteAllText(path,
                "{\"version\":99,\"id\":\"4c8ca221-b1ca-4ee7-b40f-8f891858b68a\",\"revision\":1,\"name\":\"bad\",\"rootObjectId\":\"e9eef4a6-659a-49f8-9d49-647a537ae7d9\",\"scene\":{}}");

            Assert.Throws<InvalidDataException>(() => SceneTemplateFile.Load(path));

            var scene = new SceneGraph();
            Assert.Throws<InvalidDataException>(() =>
                SceneTemplateFile.Save(scene, Guid.NewGuid(), "Missing", path + ".new"));
            Assert.False(File.Exists(path + ".new"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".new")) File.Delete(path + ".new");
        }
    }

    private static string TemporaryTemplatePath() => Path.Combine(Path.GetTempPath(),
        $"ember-scene-template-{Guid.NewGuid():N}.json");
}
