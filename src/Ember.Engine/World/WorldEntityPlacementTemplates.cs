using Ember.Scene;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.World;

public sealed record PlacementTemplateTransform
{
    public float[] Position { get; init; } = [0f, 0f, 0f];
    public float[] Rotation { get; init; } = [0f, 0f, 0f, 1f];
    public float[] Scale { get; init; } = [1f, 1f, 1f];

    public static PlacementTemplateTransform From(Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var rotation = transform.Rotation;
        return new PlacementTemplateTransform
        {
            Position = [transform.Position.X, transform.Position.Y, transform.Position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Scale = [transform.Scale.X, transform.Scale.Y, transform.Scale.Z]
        };
    }

    public Transform ToTransform()
    {
        Validate();
        return new Transform
        {
            Position = new Vector3(Position[0], Position[1], Position[2]),
            Rotation = new Quaternion(Rotation[0], Rotation[1], Rotation[2], Rotation[3]),
            Scale = new Vector3(Scale[0], Scale[1], Scale[2])
        };
    }

    public void Validate()
    {
        if (Position is null || Position.Length != 3 || Position.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Placement template position must contain three finite values.");
        if (Rotation is null || Rotation.Length != 4 || Rotation.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Placement template rotation must contain four finite values.");
        var rotationLength = MathF.Sqrt(Rotation.Sum(value => value * value));
        if (rotationLength < 0.000001f)
            throw new InvalidDataException("Placement template rotation cannot be zero.");
        if (Scale is null || Scale.Length != 3 || Scale.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Placement template scale must contain three finite values.");
    }
}

public sealed record WorldEntityPlacementTemplate
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public WorldEntityKind Kind { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public PlacementTemplateTransform Transform { get; init; } = new();

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Placement template ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Placement template name is required.");
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException($"Placement template '{Name}' has an unknown entity kind.");
        if (string.IsNullOrWhiteSpace(DefinitionId))
            throw new InvalidDataException($"Placement template '{Name}' needs a definition ID.");
        if (Transform is null) throw new InvalidDataException($"Placement template '{Name}' has no default transform.");
        Transform.Validate();
    }
}

/// <summary>Versioned reusable RPG placement templates authored by the scene tool.</summary>
public sealed class WorldEntityPlacementTemplateSet
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<WorldEntityPlacementTemplate> _templates = new();
    public int Count => _templates.Count;
    public IReadOnlyList<WorldEntityPlacementTemplate> All => _templates.AsReadOnly();

    public WorldEntityPlacementTemplate? Get(Guid id) =>
        _templates.FirstOrDefault(template => template.Id == id);

    public void Add(WorldEntityPlacementTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        if (_templates.Any(existing => existing.Id == template.Id))
            throw new InvalidDataException($"Placement template ID {template.Id} already exists.");
        if (_templates.Any(existing => string.Equals(existing.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Placement template name '{template.Name}' already exists.");
        _templates.Add(template);
    }

    public void Replace(WorldEntityPlacementTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        var index = _templates.FindIndex(existing => existing.Id == template.Id);
        if (index < 0) throw new KeyNotFoundException($"Placement template {template.Id} does not exist.");
        if (_templates.Where((_, candidateIndex) => candidateIndex != index)
            .Any(existing => string.Equals(existing.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Placement template name '{template.Name}' already exists.");
        _templates[index] = template;
    }

    public void Validate()
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in _templates)
        {
            if (template is null) throw new InvalidDataException("Placement template set contains a null entry.");
            template.Validate();
            if (!ids.Add(template.Id)) throw new InvalidDataException($"Duplicate placement template ID {template.Id}.");
            if (!names.Add(template.Name)) throw new InvalidDataException($"Duplicate placement template name '{template.Name}'.");
        }
    }

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(new TemplateDocument
        {
            Version = CurrentVersion,
            Templates = _templates.OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.Id).ToList()
        }, JsonOptions);
    }

    public static WorldEntityPlacementTemplateSet FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<TemplateDocument>(json, JsonOptions)
            ?? throw new JsonException("Placement template document is empty.");
        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported placement-template version {document.Version}; expected {CurrentVersion}.");
        if (document.Templates is null) throw new InvalidDataException("Placement template list is missing.");
        var result = new WorldEntityPlacementTemplateSet();
        foreach (var template in document.Templates) result.Add(template);
        return result;
    }

    public static WorldEntityPlacementTemplateSet Load(string path) => FromJson(File.ReadAllText(path));

    public void SaveAtomic(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Placement-template file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(ToJson());
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class TemplateDocument
    {
        public int Version { get; init; }
        public List<WorldEntityPlacementTemplate>? Templates { get; init; }
    }
}

public static class WorldEntityPlacementTemplateSystem
{
    public static SceneObject CreatePlacement(SceneGraph scene, WorldEntityPlacementTemplate template)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        var transform = template.Transform.ToTransform();
        var placement = SceneObjectFactory.CreateWorldEntityPlacement(scene, template.Kind,
            template.DefinitionId, template.Name, transform.Position);
        placement.Transform = transform;
        placement.WorldEntity = new WorldEntityPlacementComponent(template.Kind,
            template.DefinitionId, placement.WorldEntity!.InstanceId, template.Id);
        return placement;
    }

    /// <summary>Refreshes template-owned values and leaves every explicitly overridden field and instance ID intact.</summary>
    public static int ApplyTemplate(SceneGraph scene, WorldEntityPlacementTemplate template)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        var defaults = template.Transform.ToTransform();
        var updated = 0;
        foreach (var sceneObject in scene.Objects)
        {
            var placement = sceneObject.WorldEntity;
            if (placement?.TemplateId != template.Id) continue;

            if ((placement.TemplateOverrides & PlacementTemplateOverrideFlags.Definition) == 0)
                sceneObject.WorldEntity = new WorldEntityPlacementComponent(template.Kind,
                    template.DefinitionId, placement.InstanceId, template.Id, placement.TemplateOverrides);

            var overrides = placement.TemplateOverrides;
            sceneObject.Transform = new Transform
            {
                Position = (overrides & PlacementTemplateOverrideFlags.Position) != 0
                    ? sceneObject.Transform.Position : defaults.Position,
                Rotation = (overrides & PlacementTemplateOverrideFlags.Rotation) != 0
                    ? sceneObject.Transform.Rotation : defaults.Rotation,
                Scale = (overrides & PlacementTemplateOverrideFlags.Scale) != 0
                    ? sceneObject.Transform.Scale : defaults.Scale
            };
            updated++;
        }
        return updated;
    }

    public static void SetOverrides(SceneObject sceneObject, PlacementTemplateOverrideFlags overrides)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        var placement = sceneObject.WorldEntity
            ?? throw new InvalidOperationException("Scene object has no RPG placement to mark as a template instance.");
        if (placement.TemplateId is null)
            throw new InvalidOperationException("Scene object is not an instance of a placement template.");
        sceneObject.WorldEntity = new WorldEntityPlacementComponent(placement.Kind,
            placement.DefinitionId, placement.InstanceId, placement.TemplateId, overrides);
    }
}

public sealed class WorldEntityPlacementTemplateApplyCommand(WorldEntityPlacementTemplate template) : ISceneCommand
{
    private List<PlacementSnapshot>? _before;

    public void Apply(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_before is null)
        {
            _before = scene.Objects
                .Where(item => item.WorldEntity?.TemplateId == template.Id)
                .Select(item => new PlacementSnapshot(item.Id, item.WorldEntity, CopyTransform(item.Transform)))
                .ToList();
        }
        WorldEntityPlacementTemplateSystem.ApplyTemplate(scene, template);
    }

    public void Revert(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_before is null) throw new InvalidOperationException("Cannot undo a template update before it was applied.");
        foreach (var snapshot in _before)
        {
            var sceneObject = scene.Find(snapshot.ObjectId)
                ?? throw new InvalidOperationException($"Cannot undo missing template instance {snapshot.ObjectId}.");
            sceneObject.WorldEntity = snapshot.Placement;
            sceneObject.Transform = CopyTransform(snapshot.Transform);
        }
    }

    private static Transform CopyTransform(Transform transform) => new()
    {
        Position = transform.Position,
        Rotation = transform.Rotation,
        Scale = transform.Scale
    };

    private sealed record PlacementSnapshot(Guid ObjectId,
        WorldEntityPlacementComponent? Placement, Transform Transform);
}
