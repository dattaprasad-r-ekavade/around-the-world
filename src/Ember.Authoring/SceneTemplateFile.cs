using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Scene;

namespace Ember.Authoring;

/// <summary>A versioned, validated snapshot of one scene hierarchy saved as a reusable template.</summary>
public sealed class SceneTemplateSnapshot
{
    internal SceneTemplateSnapshot(Guid id, int revision, string name, Guid rootObjectId, SceneGraph scene)
    {
        Id = id;
        Revision = revision;
        Name = name;
        RootObjectId = rootObjectId;
        Scene = scene;
    }

    public Guid Id { get; }
    public int Revision { get; }
    public string Name { get; }
    public Guid RootObjectId { get; }
    public SceneGraph Scene { get; }
}

/// <summary>Atomic project-file persistence for reusable scene hierarchy snapshots.</summary>
public static class SceneTemplateFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    /// <summary>
    /// Save the selected object and all descendants. Saving over a valid template keeps its ID and
    /// advances its revision; a new destination receives a new ID at revision one.
    /// </summary>
    public static SceneTemplateSnapshot Save(SceneGraph scene, Guid rootObjectId, string name, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var templateId = Guid.NewGuid();
        var revision = 1;
        if (File.Exists(path))
        {
            var existing = Load(path);
            templateId = existing.Id;
            revision = checked(existing.Revision + 1);
        }

        var snapshot = Capture(scene, rootObjectId, name.Trim(), templateId, revision);
        var contentJson = SceneFile.ToJson(snapshot.Scene);
        using var contentDocument = JsonDocument.Parse(contentJson);
        var document = new TemplateDocument
        {
            Version = CurrentVersion,
            Id = snapshot.Id,
            Revision = snapshot.Revision,
            Name = snapshot.Name,
            RootObjectId = snapshot.RootObjectId,
            Scene = contentDocument.RootElement.Clone()
        };
        var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document, JsonOptions));
        AtomicFile.Write(path, stream => stream.Write(bytes));
        return snapshot;
    }

    public static SceneTemplateSnapshot Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        TemplateDocument document;
        try
        {
            document = JsonSerializer.Deserialize<TemplateDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Scene template document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Scene template JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported scene-template version {document.Version}; expected {CurrentVersion}.");
        if (document.Id == Guid.Empty) throw new InvalidDataException("Scene template ID cannot be empty.");
        if (document.Revision < 1) throw new InvalidDataException("Scene template revision must be positive.");
        if (string.IsNullOrWhiteSpace(document.Name)) throw new InvalidDataException("Scene template name is required.");
        if (document.Scene.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Scene template hierarchy is missing.");

        SceneGraph scene;
        try
        {
            scene = SceneFile.FromJson(document.Scene.GetRawText());
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Scene template hierarchy is invalid: {exception.Message}", exception);
        }

        return ValidateAndCreate(document.Id, document.Revision, document.Name,
            document.RootObjectId, scene);
    }

    private static SceneTemplateSnapshot Capture(SceneGraph source, Guid rootObjectId, string name,
        Guid templateId, int revision)
    {
        if (templateId == Guid.Empty) throw new ArgumentException("Template ID cannot be empty.", nameof(templateId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));

        var includedIds = GetSubtreeIds(source, rootObjectId);
        if (includedIds.Any(id => source.Find(id)!.TemplateInstance is not null))
            throw new InvalidDataException("Saving a scene template from an existing template instance is not supported yet.");
        var snapshot = SceneFile.FromJson(SceneFile.ToJson(source));
        snapshot.SetParent(rootObjectId, null);
        foreach (var id in snapshot.Objects.Select(item => item.Id).Where(id => !includedIds.Contains(id)).ToArray())
            snapshot.Remove(id);

        // These settings belong to the containing scene, not to the selected hierarchy. Keeping
        // them leaks unrelated gameplay defaults and audio assets into reusable template files.
        snapshot.PlaySettings = new ScenePlaySettings();
        snapshot.SetAudioAssets(Array.Empty<SceneAudioAssetReference>());

        return ValidateAndCreate(templateId, revision, name, rootObjectId, snapshot);
    }

    private static SceneTemplateSnapshot ValidateAndCreate(Guid id, int revision, string name,
        Guid rootObjectId, SceneGraph scene)
    {
        if (id == Guid.Empty) throw new InvalidDataException("Scene template ID cannot be empty.");
        if (revision < 1) throw new InvalidDataException("Scene template revision must be positive.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Scene template name is required.");
        if (rootObjectId == Guid.Empty) throw new InvalidDataException("Scene template root ID cannot be empty.");
        var root = scene.Find(rootObjectId)
            ?? throw new InvalidDataException($"Scene template root object {rootObjectId} is missing.");
        if (root.ParentId is not null)
            throw new InvalidDataException("Scene template root cannot have a parent outside the saved hierarchy.");

        var subtreeIds = GetSubtreeIds(scene, rootObjectId);
        if (subtreeIds.Count != scene.Objects.Count)
            throw new InvalidDataException("Scene template contains objects outside its root hierarchy.");

        return new SceneTemplateSnapshot(id, revision, name.Trim(), rootObjectId, scene);
    }

    private static HashSet<Guid> GetSubtreeIds(SceneGraph scene, Guid rootObjectId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Find(rootObjectId) is null)
            throw new InvalidDataException($"Cannot save a template because root object {rootObjectId} is missing.");

        var childrenByParent = scene.Objects
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
        var included = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootObjectId);
        while (pending.TryPop(out var id))
        {
            if (!included.Add(id))
                throw new InvalidDataException("Scene hierarchy contains a cycle while saving a template.");
            if (childrenByParent.TryGetValue(id, out var children))
                foreach (var child in children) pending.Push(child);
        }
        return included;
    }

    private sealed class TemplateDocument
    {
        public int Version { get; init; }
        public Guid Id { get; init; }
        public int Revision { get; init; }
        public string? Name { get; init; }
        public Guid RootObjectId { get; init; }
        public JsonElement Scene { get; init; }
    }
}
