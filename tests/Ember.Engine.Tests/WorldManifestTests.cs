using System;
using System.IO;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldManifestTests
{
    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(15.99f, 15.99f, 0, 0)]
    [InlineData(16f, -16f, 1, -1)]
    [InlineData(-0.01f, 0f, -1, 0)]
    [InlineData(-16f, -32f, -1, -2)]
    public void WorldPositionMapsToFloorBasedExteriorCell(float x, float z, int expectedX, int expectedZ)
    {
        var coordinate = ExteriorCellGrid.FromWorldPosition(new Vector3(x, 100f, z), 16f);

        Assert.Equal(new ExteriorCellCoordinate(expectedX, expectedZ), coordinate);
    }

    [Fact]
    public void WorldPositionMappingRejectsInvalidWidthAndOutOfRangeCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExteriorCellGrid.FromWorldPosition(Vector3.Zero, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExteriorCellGrid.FromWorldPosition(Vector3.Zero, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExteriorCellGrid.FromWorldPosition(new Vector3(float.MaxValue, 0f, 0f), float.Epsilon));
    }

    [Fact]
    public void SaveAndLoadPreservesExteriorCoordinatesAndInteriorSceneReferences()
    {
        var directory = TemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Scenes"));
            File.WriteAllText(Path.Combine(directory, "Scenes", "Exterior.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "Scenes", "Interior.json"), "{}");
            var exteriorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var interiorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var path = Path.Combine(directory, WorldManifest.DefaultFileName);

            WorldManifest.SaveAtomic(path,
            [
                new WorldCellDefinition
                {
                    Id = exteriorId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorX = -2,
                    ExteriorZ = 4,
                    ScenePath = "Scenes/Exterior.json"
                },
                new WorldCellDefinition
                {
                    Id = interiorId,
                    Kind = WorldCellKind.Interior,
                    ScenePath = "Scenes/Interior.json"
                }
            ]);

            var loaded = WorldManifest.Load(path);

            Assert.Equal(2, loaded.Cells.Count);
            Assert.Equal(exteriorId, loaded.Cells[0].Id);
            Assert.Equal((-2, 4), (loaded.Cells[0].ExteriorX, loaded.Cells[0].ExteriorZ));
            Assert.Equal(interiorId, loaded.FindCell(interiorId)!.Id);
            Assert.Equal(Path.Combine(directory, "Scenes", "Interior.json"), loaded.ResolveScenePath(interiorId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsDuplicateCellIdsAndExteriorCoordinates()
    {
        var directory = TemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "cell.json"), "{}");
            var manifestPath = Path.Combine(directory, "world.json");
            var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
            File.WriteAllText(manifestPath, $$"""
                {
                  "version": 1,
                  "cells": [
                    { "id": "{{id}}", "kind": "Exterior", "exteriorX": 0, "exteriorZ": 0, "scenePath": "cell.json" },
                    { "id": "{{id}}", "kind": "Interior", "scenePath": "cell.json" }
                  ]
                }
                """);

            var idError = Assert.Throws<InvalidDataException>(() => WorldManifest.Load(manifestPath));
            Assert.Contains("Duplicate world cell ID", idError.Message, StringComparison.Ordinal);

            var otherId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            File.WriteAllText(manifestPath, $$"""
                {
                  "version": 1,
                  "cells": [
                    { "id": "{{id}}", "kind": "Exterior", "exteriorX": 0, "exteriorZ": 0, "scenePath": "cell.json" },
                    { "id": "{{otherId}}", "kind": "Exterior", "exteriorX": 0, "exteriorZ": 0, "scenePath": "cell.json" }
                  ]
                }
                """);

            var coordinateError = Assert.Throws<InvalidDataException>(() => WorldManifest.Load(manifestPath));
            Assert.Contains("Duplicate exterior cell coordinates", coordinateError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsMissingAndEscapingSceneReferences()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "world.json");
            var id = Guid.Parse("55555555-5555-5555-5555-555555555555");
            File.WriteAllText(manifestPath, $$"""
                {
                  "version": 1,
                  "cells": [
                    { "id": "{{id}}", "kind": "Interior", "scenePath": "missing.json" }
                  ]
                }
                """);
            var missingError = Assert.Throws<InvalidDataException>(() => WorldManifest.Load(manifestPath));
            Assert.Contains("refers to missing scene", missingError.Message, StringComparison.Ordinal);

            File.WriteAllText(manifestPath, $$"""
                {
                  "version": 1,
                  "cells": [
                    { "id": "{{id}}", "kind": "Interior", "scenePath": "../outside.json" }
                  ]
                }
                """);
            var pathError = Assert.Throws<InvalidDataException>(() => WorldManifest.Load(manifestPath));
            Assert.Contains("cannot contain '..'", pathError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-world-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
