using System;
using System.IO;
using System.Linq;
using Ember.Authoring;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneTemplateLibraryTests
{
    [Fact]
    public void NewTemplatesUseSafeUniqueNamesAndRevisionsKeepIdentity()
    {
        var projectDirectory = TemporaryDirectory();
        try
        {
            var library = new SceneTemplateLibrary(projectDirectory);
            var scene = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Small room");
            scene.Add(root);

            var first = library.SaveNew(scene, root.Id, "Small room");
            var second = library.SaveNew(scene, root.Id, "Small room");

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(1, first.Revision);
            Assert.Equal(1, second.Revision);
            Assert.Equal(2, library.ReadAll().Count(item => item.IsValid));

            var reservedName = library.SaveNew(scene, root.Id, "CON");
            var reservedEntry = library.ReadAll().Single(item => item.Template?.Id == reservedName.Id);
            Assert.StartsWith("template-CON", Path.GetFileName(reservedEntry.Path),
                StringComparison.OrdinalIgnoreCase);

            root.Name = "Updated small room";
            var revision = library.SaveRevision(scene, root.Id, first.Id, "Small room");

            Assert.Equal(first.Id, revision.Id);
            Assert.Equal(2, revision.Revision);
            Assert.Equal("Updated small room", library.ReadAll()
                .Single(item => item.Template?.Id == first.Id).Template!.Scene.Find(root.Id)!.Name);
            Assert.Throws<InvalidOperationException>(() =>
                library.SaveRevision(scene, Guid.NewGuid(), first.Id, "Wrong root"));
        }
        finally
        {
            DeleteDirectory(projectDirectory);
        }
    }

    [Fact]
    public void DamagedTemplatesRemainVisibleBesideValidTemplates()
    {
        var projectDirectory = TemporaryDirectory();
        try
        {
            var library = new SceneTemplateLibrary(projectDirectory);
            var scene = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room");
            scene.Add(root);
            library.SaveNew(scene, root.Id, "Room");
            Directory.CreateDirectory(library.DirectoryPath);
            File.WriteAllText(Path.Combine(library.DirectoryPath, "damaged.embertemplate.json"), "{");

            var entries = library.ReadAll();

            Assert.Equal(2, entries.Count);
            Assert.Single(entries, entry => entry.IsValid);
            Assert.Contains(entries, entry => !entry.IsValid && !string.IsNullOrWhiteSpace(entry.Error));
        }
        finally
        {
            DeleteDirectory(projectDirectory);
        }
    }

    [Fact]
    public void RelinkRequiresMatchingTemplateIdAndCopiesIntoProjectLibrary()
    {
        var projectDirectory = TemporaryDirectory();
        var externalDirectory = TemporaryDirectory();
        try
        {
            var sourceScene = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Hall");
            sourceScene.Add(root);
            var externalPath = Path.Combine(externalDirectory, "hall.json");
            var template = SceneTemplateFile.Save(sourceScene, root.Id, "Hall", externalPath);
            var library = new SceneTemplateLibrary(projectDirectory);

            var linked = library.Relink(template.Id, externalPath);

            Assert.Equal(template.Id, linked.Id);
            Assert.Equal(template.Revision, linked.Revision);
            Assert.True(File.Exists(Path.Combine(library.DirectoryPath,
                template.Id.ToString("N") + ".embertemplate.json")));
            Assert.Single(library.ReadAll(), entry => entry.Template?.Id == template.Id);
            Assert.Throws<InvalidDataException>(() => library.Relink(Guid.NewGuid(), externalPath));
        }
        finally
        {
            DeleteDirectory(projectDirectory);
            DeleteDirectory(externalDirectory);
        }
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(),
        $"ember-scene-template-library-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
