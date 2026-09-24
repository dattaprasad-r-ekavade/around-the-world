using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellWorkspaceTests
{
    [Fact]
    public void CreateWorldAndCellsWritesLoadableScenesWithStableIdsAfterRename()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "world.json");
            var world = WorldCellWorkspace.CreateWorld(manifestPath, exteriorCellWidth: 48f);
            Assert.Empty(world.Cells);

            var coordinate = new ExteriorCellCoordinate(-2, 4);
            var exterior = WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Exterior,
                "North Gate", coordinate);
            var interior = WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Interior, "Inn Cell");
            Assert.Equal("North Gate", WorldCellWorkspace.GetCellName(exterior));
            Assert.Equal("Inn Cell", WorldCellWorkspace.GetCellName(interior));
            var objectId = Guid.NewGuid();
            var scene = new SceneGraph();
            scene.Add(new SceneObject(objectId, "Preserved object"));
            SceneFile.SaveAtomic(scene, WorldManifest.Load(manifestPath).ResolveScenePath(exterior.Id));

            var renamed = WorldCellWorkspace.RenameCell(manifestPath, exterior.Id, "North Market Gate");
            var loaded = WorldManifest.Load(manifestPath);

            Assert.Equal(exterior.Id, renamed.Id);
            Assert.Equal(coordinate, renamed.ExteriorCoordinate);
            Assert.Equal("North Market Gate", WorldCellWorkspace.GetCellName(renamed));
            Assert.NotEqual(exterior.ScenePath, renamed.ScenePath);
            Assert.False(File.Exists(Path.Combine(directory, exterior.ScenePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(objectId, Assert.Single(SceneFile.Load(loaded.ResolveScenePath(renamed.Id)).Objects).Id);
            var sameName = WorldCellWorkspace.RenameCell(manifestPath, exterior.Id, "North Market Gate");
            Assert.Equal(renamed.Id, sameName.Id);
            Assert.Equal(renamed.ScenePath, sameName.ScenePath);
            Assert.Equal(interior.Id, loaded.FindCell(interior.Id)!.Id);
            Assert.Equal(WorldCellKind.Interior, loaded.FindCell(interior.Id)!.Kind);
            Assert.Null(loaded.FindCell(interior.Id)!.ExteriorCoordinate);
            Assert.Equal(48f, loaded.ExteriorCellWidth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DuplicateExteriorCoordinateDoesNotAddCellOrLeaveSceneFile()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "world.json");
            WorldCellWorkspace.CreateWorld(manifestPath);
            WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Exterior,
                "First", new ExteriorCellCoordinate(3, -1));

            var exception = Assert.Throws<InvalidDataException>(() =>
                WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Exterior,
                    "Duplicate", new ExteriorCellCoordinate(3, -1)));

            Assert.Contains("Duplicate exterior cell coordinates", exception.Message, StringComparison.Ordinal);
            var manifest = WorldManifest.Load(manifestPath);
            Assert.Single(manifest.Cells);
            Assert.False(File.Exists(Path.Combine(directory, "Cells", "Exterior", "duplicate.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DuplicateCellNamesReceiveSeparateScenePathsAndInvalidKindsAreRejected()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "world.json");
            WorldCellWorkspace.CreateWorld(manifestPath);

            var first = WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Interior, "The Inn");
            var second = WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Interior, "The Inn");

            Assert.NotEqual(first.Id, second.Id);
            Assert.NotEqual(first.ScenePath, second.ScenePath);
            Assert.True(File.Exists(WorldManifest.Load(manifestPath).ResolveScenePath(first.Id)));
            Assert.True(File.Exists(WorldManifest.Load(manifestPath).ResolveScenePath(second.Id)));
            Assert.Throws<ArgumentException>(() =>
                WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Exterior, "Missing coordinates"));
            Assert.Throws<ArgumentException>(() =>
                WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Interior, "Bad interior",
                    new ExteriorCellCoordinate(0, 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-world-cells-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
