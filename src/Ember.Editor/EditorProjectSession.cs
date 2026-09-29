using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Assets;
using Ember.Project;
using Ember.Scene;

namespace Ember.Editor;

internal sealed class EditorProjectSession
{
    private static readonly string RecentProjectsStorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ember", "CharacterStudio", "recent-projects.json");

    private IReadOnlyList<GltfAssetReference> _assetCatalog = Array.Empty<GltfAssetReference>();
    private string? _assetCatalogProjectPath;
    private bool _assetCatalogLoaded;

    public EngineProjectFile? Project { get; private set; }

    public void SetProject(EngineProjectFile? project)
    {
        Project = project;
        InvalidateAssetCatalog();
    }

    public IReadOnlyList<GltfAssetReference> GetAssetReferences(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (Project is null) return Array.Empty<GltfAssetReference>();

        if (!_assetCatalogLoaded
            || !string.Equals(_assetCatalogProjectPath, Project.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            _assetCatalog = EngineProjectAssetCatalog.ListAssets(
                Project, EnumerateProjectSceneAssetReferences(scene));
            _assetCatalogProjectPath = Project.FilePath;
            _assetCatalogLoaded = true;
        }

        return _assetCatalog;
    }

    public void InvalidateAssetCatalog()
    {
        _assetCatalogLoaded = false;
        _assetCatalogProjectPath = null;
    }

    public IReadOnlyList<string> LoadRecentProjects() =>
        EngineProjectWorkspace.LoadRecent(RecentProjectsStorePath);

    public void RecordRecentProject(string projectPath) =>
        EngineProjectWorkspace.RecordRecent(RecentProjectsStorePath, projectPath);

    internal static IEnumerable<GltfAssetReference> EnumerateProjectSceneAssetReferences(SceneGraph scene)
    {
        var references = scene.Objects.SelectMany(item =>
        {
            var values = new List<GltfAssetReference>();
            if (item.GltfAsset is { } asset) values.Add(asset);
            if (item.StaticMeshLod is { } lod)
            {
                values.Add(lod.NearAsset);
                values.Add(lod.FarAsset);
            }
            return values;
        });
        foreach (var group in references.GroupBy(asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase))
            yield return group.First();
    }
}
