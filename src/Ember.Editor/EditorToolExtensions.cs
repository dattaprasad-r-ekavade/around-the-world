using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Ember.Scene;

namespace Ember.Editor;

/// <summary>A tool panel supplied by an optional editor module.</summary>
public interface IEditorToolExtension
{
    string Id { get; }
    string DisplayName { get; }
    void Draw(EditorToolContext context);
}

/// <summary>Safe editor operations made available to optional tool modules.</summary>
public sealed class EditorToolContext
{
    private readonly Action<SceneObject> _addSceneObject;

    internal EditorToolContext(SceneGraph scene, bool isPlaying, string? projectFilePath,
        string? scenePath, Action<SceneObject> addSceneObject)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        IsPlaying = isPlaying;
        ProjectFilePath = projectFilePath;
        ScenePath = scenePath;
        _addSceneObject = addSceneObject ?? throw new ArgumentNullException(nameof(addSceneObject));
    }

    public SceneGraph Scene { get; }
    public bool IsPlaying { get; }
    public string? ProjectFilePath { get; }
    public string? ScenePath { get; }

    public void AddSceneObject(SceneObject sceneObject) =>
        _addSceneObject(sceneObject ?? throw new ArgumentNullException(nameof(sceneObject)));
}

public sealed record EditorToolExtensionLoadResult(
    IReadOnlyList<IEditorToolExtension> Extensions,
    IReadOnlyList<string> Errors);

/// <summary>Loads optional editor tools from an app-local Modules directory.</summary>
public static class EditorToolExtensionLoader
{
    public static EditorToolExtensionLoadResult Load(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var modulesDirectory = Path.Combine(Path.GetFullPath(applicationDirectory), "Modules");
        if (!Directory.Exists(modulesDirectory))
            return new EditorToolExtensionLoadResult(Array.Empty<IEditorToolExtension>(), Array.Empty<string>());

        var extensions = new List<IEditorToolExtension>();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modulePath in Directory.EnumerateFiles(modulesDirectory, "Ember.Editor.*.dll")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var loadContext = new EditorModuleLoadContext(modulePath);
                var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(modulePath));
                foreach (var type in assembly.GetTypes()
                             .Where(type => !type.IsAbstract && !type.IsInterface
                                 && typeof(IEditorToolExtension).IsAssignableFrom(type)))
                {
                    if (Activator.CreateInstance(type) is not IEditorToolExtension extension)
                        throw new InvalidOperationException($"Could not construct editor tool '{type.FullName}'.");
                    if (string.IsNullOrWhiteSpace(extension.Id) || string.IsNullOrWhiteSpace(extension.DisplayName))
                        throw new InvalidDataException($"Editor tool '{type.FullName}' needs an ID and display name.");
                    if (!ids.Add(extension.Id))
                        throw new InvalidDataException($"More than one editor tool uses ID '{extension.Id}'.");
                    extensions.Add(extension);
                }
            }
            catch (Exception exception)
            {
                errors.Add($"Could not load editor module '{Path.GetFileName(modulePath)}': {exception.Message}");
            }
        }

        return new EditorToolExtensionLoadResult(extensions.AsReadOnly(), errors.AsReadOnly());
    }

    private sealed class EditorModuleLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public EditorModuleLoadContext(string modulePath)
            : base($"Ember.Editor.Module:{Path.GetFileNameWithoutExtension(modulePath)}", isCollectible: false) =>
            _resolver = new AssemblyDependencyResolver(Path.GetFullPath(modulePath));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (sharedAssembly is not null) return sharedAssembly;

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return resolvedPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolvedPath);
        }
    }
}
