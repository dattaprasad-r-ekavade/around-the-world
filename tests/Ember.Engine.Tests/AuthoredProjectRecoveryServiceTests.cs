using System;
using System.IO;
using System.Linq;
using System.Text;
using Ember.Authoring;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class AuthoredProjectRecoveryServiceTests
{
    [Fact]
    public void RecoveryRestoresCompleteAuthoredSnapshotToStagingWithoutChangingSourcesOrPlayerSave()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "Project");
            var worldDirectory = Path.Combine(projectRoot, "World");
            var sceneDirectory = Path.Combine(worldDirectory, "Scenes");
            var pathDirectory = Path.Combine(worldDirectory, "Paths");
            var contentDirectory = Path.Combine(projectRoot, "Assets");
            var recoveryDirectory = Path.Combine(directory, "Local", "Ember", "CharacterStudio", "AuthoringRecovery");
            var playerSavePath = Path.Combine(directory, "Local", "Ember", "RpgSlice", "world-save.json");
            var exteriorCell = Id("11111111-1111-1111-1111-111111111111");
            var interiorCell = Id("22222222-2222-2222-2222-222222222222");
            var exteriorSpawn = Id("33333333-3333-3333-3333-333333333333");
            var interiorSpawn = Id("44444444-4444-4444-4444-444444444444");

            var exteriorPath = Path.Combine(sceneDirectory, "Exterior.json");
            var interiorPath = Path.Combine(sceneDirectory, "House.json");
            var exterior = new SceneGraph();
            exterior.Add(new SceneObject(Id("55555555-5555-5555-5555-555555555555"), "HouseDoor")
            {
                Door = new WorldDoorComponent(interiorCell, interiorSpawn, Microsoft.Xna.Framework.Quaternion.Identity)
            });
            exterior.Add(new SceneObject(Id("66666666-6666-6666-6666-666666666666"), "TownSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(exteriorSpawn)
            });
            var interior = new SceneGraph();
            interior.Add(new SceneObject(Id("77777777-7777-7777-7777-777777777777"), "ReturnDoor")
            {
                Door = new WorldDoorComponent(exteriorCell, exteriorSpawn, Microsoft.Xna.Framework.Quaternion.Identity)
            });
            interior.Add(new SceneObject(Id("88888888-8888-8888-8888-888888888888"), "HouseSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(interiorSpawn)
            });
            SceneFile.SaveAtomic(exterior, exteriorPath);
            SceneFile.SaveAtomic(interior, interiorPath);

            var manifestPath = Path.Combine(worldDirectory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = exteriorCell, Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0), ScenePath = "Scenes/Exterior.json"
                },
                new WorldCellDefinition { Id = interiorCell, Kind = WorldCellKind.Interior, ScenePath = "Scenes/House.json" }
            ]);
            Directory.CreateDirectory(pathDirectory);
            var graph = new CellPathGraph
            {
                CellId = exteriorCell,
                Kind = WorldCellKind.Exterior,
                Nodes = [new CellPathNode(Id("99999999-9999-9999-9999-999999999999"), new NavigationPoint(0, 0, 0))]
            };
            CellPathGraphFile.SaveAtomic(Path.Combine(pathDirectory, "Exterior.paths.json"), graph);
            WorldPathNetworkFile.SaveAtomic(Path.Combine(pathDirectory, "world-paths.json"),
                new WorldPathNetwork { Cells = [graph] });

            var contentPath = Path.Combine(contentDirectory, "RpgContent.json");
            RpgContentJson.SaveAtomic(contentPath, new RpgContentSet());
            Directory.CreateDirectory(Path.GetDirectoryName(playerSavePath)!);
            var playerSave = "valid-player-save";
            File.WriteAllText(playerSavePath, playerSave);
            var inMemoryScene = SceneFile.Load(exteriorPath);
            inMemoryScene.Add(new SceneObject(Id("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "UnsavedEditorObject"));
            var inMemoryContent = RpgContentJson.ToJsonForRecovery(new RpgContentSet());
            var snapshot = AuthoredProjectRecoveryService.Capture(manifestPath, contentPath, recoveryDirectory,
                currentScenePath: exteriorPath,
                currentSceneJson: Encoding.UTF8.GetBytes(SceneFile.ToJson(inMemoryScene)),
                currentRpgContentJson: Encoding.UTF8.GetBytes(inMemoryContent));
            File.WriteAllText(exteriorPath, "newer scene edit");
            File.WriteAllText(contentPath, "newer RPG edit");
            var filesBeforeRestore = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

            var restored = AuthoredProjectRecoveryService.RestoreLatestToStaging(
                manifestPath, contentPath, recoveryDirectory);

            Assert.Equal(6, snapshot.Files.Count);
            Assert.Equal(snapshot.SnapshotId, restored.SnapshotId);
            Assert.True(File.Exists(restored.WorldManifestPath));
            Assert.True(File.Exists(restored.RpgContentPath));
            Assert.Equal(6, restored.RestoredFiles.Count);
            Assert.False(IsWithin(projectRoot, restored.StagingRoot));
            Assert.Empty(AuthoredProjectValidator.Validate(restored.WorldManifestPath, restored.RpgContentPath).Diagnostics);
            var stagedScene = SceneFile.Load(Path.Combine(restored.StagingRoot, Path.GetRelativePath(projectRoot, exteriorPath)));
            Assert.Contains(stagedScene.Objects, sceneObject => sceneObject.Name == "UnsavedEditorObject");
            Assert.Empty(RpgContentJson.ParseForValidation(File.ReadAllText(restored.RpgContentPath)).Diagnostics);

            var snapshotOnDisk = AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory);
            foreach (var file in snapshotOnDisk.Files)
            {
                var stagedPath = Path.Combine(restored.StagingRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(file.Content, File.ReadAllBytes(stagedPath));
            }
            var filesAfterRestore = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(filesBeforeRestore.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                filesAfterRestore.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            foreach (var (path, bytes) in filesBeforeRestore)
                Assert.Equal(bytes, filesAfterRestore[path]);
            Assert.Equal(playerSave, File.ReadAllText(playerSavePath));

            AuthoredProjectRecoveryService.ApplyValidatedStaging(restored);
            Assert.Contains(SceneFile.Load(exteriorPath).Objects, sceneObject => sceneObject.Name == "UnsavedEditorObject");
            Assert.Equal(File.ReadAllBytes(restored.RpgContentPath), File.ReadAllBytes(contentPath));
            Assert.Equal(playerSave, File.ReadAllText(playerSavePath));

            const string semanticallyInvalidDraft = """
                {
                  "actors": [], "items": [],
                  "dialogues": [{ "id": "dialogue.draft", "nodes": [{ "id": "hello", "speaker": "Guard", "speakerActorId": "actors.missing", "text": "Hello." }] }],
                  "quests": []
                }
                """;
            var invalidDraft = RpgContentJson.ParseForValidation(semanticallyInvalidDraft);
            Assert.NotEmpty(invalidDraft.Diagnostics);
            var invalidScene = SceneFile.Load(exteriorPath);
            invalidScene.Add(new SceneObject(Id("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "UnsavedInvalidDraftScene"));
            AuthoredProjectRecoveryService.Capture(manifestPath, contentPath, recoveryDirectory,
                currentScenePath: exteriorPath,
                currentSceneJson: Encoding.UTF8.GetBytes(SceneFile.ToJson(invalidScene)),
                currentRpgContentJson: Encoding.UTF8.GetBytes(RpgContentJson.ToJsonForRecovery(invalidDraft.Content)));
            File.WriteAllText(exteriorPath, "source scene sentinel");
            File.WriteAllText(contentPath, "source content sentinel");
            var sourceSceneBeforeInvalidApply = File.ReadAllBytes(exteriorPath);
            var sourceContentBeforeInvalidApply = File.ReadAllBytes(contentPath);
            var invalidStaging = AuthoredProjectRecoveryService.RestoreLatestToStaging(
                manifestPath, contentPath, recoveryDirectory);
            var invalidValidation = AuthoredProjectValidator.Validate(
                invalidStaging.WorldManifestPath, invalidStaging.RpgContentPath);
            Assert.Contains(invalidValidation.Diagnostics, diagnostic => diagnostic.Message.Contains("actors.missing", StringComparison.Ordinal));
            Assert.Throws<InvalidDataException>(() => AuthoredProjectRecoveryService.ApplyValidatedStaging(invalidStaging));
            Assert.Equal(sourceSceneBeforeInvalidApply, File.ReadAllBytes(exteriorPath));
            Assert.Equal(sourceContentBeforeInvalidApply, File.ReadAllBytes(contentPath));
            Assert.Equal(playerSave, File.ReadAllText(playerSavePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Guid Id(string value) => Guid.Parse(value);

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-authored-project-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
