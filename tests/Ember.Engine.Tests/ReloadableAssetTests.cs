using System;
using System.IO;
using System.Linq;
using Ember.Assets;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ReloadableAssetTests
{
    [Fact]
    public void ValidGlbReplacementBecomesCurrentAndCorruptReplacementKeepsItActive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-reimport-{Guid.NewGuid():N}.glb");
        File.Copy(FixturePath(), path);
        var initial = new ImportedSceneHandle(GltfSceneImporter.Load(path));
        using var slot = new ReloadableAsset<ImportedSceneHandle>(initial);

        try
        {
            var modified = ModelRoot.Load(path);
            modified.LogicalNodes.Single(node => node.Name == "TopLeftObj").Name = "ReloadedTopLeft";
            using (var output = File.Create(path)) modified.WriteGLB(output, new WriteSettings());

            var cleanupError = slot.Reload(() => new ImportedSceneHandle(GltfSceneImporter.Load(path)));
            Assert.Null(cleanupError);
            Assert.True(initial.IsDisposed);
            var replacement = slot.Current;
            Assert.Contains(replacement.Scene.Scene.Objects, item => item.Name == "ReloadedTopLeft");

            File.WriteAllBytes(path, [0x42, 0x41, 0x44]);
            Assert.ThrowsAny<Exception>(() => slot.Reload(() =>
                new ImportedSceneHandle(GltfSceneImporter.Load(path))));

            Assert.Same(replacement, slot.Current);
            Assert.False(replacement.IsDisposed);
            Assert.Contains(slot.Current.Scene.Scene.Objects, item => item.Name == "ReloadedTopLeft");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FailedReplacementFactoryDoesNotDisposeCurrentAsset()
    {
        var initial = new ProbeAsset();
        using var slot = new ReloadableAsset<ProbeAsset>(initial);

        Assert.Throws<InvalidDataException>(() => slot.Reload(() => throw new InvalidDataException("bad replacement")));

        Assert.Same(initial, slot.Current);
        Assert.False(initial.IsDisposed);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");

    private sealed class ImportedSceneHandle : IDisposable
    {
        public ImportedSceneHandle(ImportedGltfScene scene) => Scene = scene;
        public ImportedGltfScene Scene { get; }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class ProbeAsset : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
