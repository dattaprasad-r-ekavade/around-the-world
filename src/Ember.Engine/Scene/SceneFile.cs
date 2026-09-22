using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>Versioned JSON persistence for scene identity, hierarchy, and transforms.</summary>
public static class SceneFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static SceneGraph Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A scene path is required.", nameof(path));
        var json = File.ReadAllText(path);
        SceneDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SceneDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Scene JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Scene JSON is invalid: {exception.Message}", exception);
        }

        return FromDocument(document);
    }

    public static void Save(SceneGraph scene, string path) => SaveAtomic(scene, path);

    /// <summary>Write beside the destination, then replace it only after serialization succeeds.</summary>
    public static void SaveAtomic(SceneGraph scene, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A scene path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Scene path has no parent directory.");
        Directory.CreateDirectory(directory);

        // Build and validate before touching the destination, so an invalid scene cannot
        // destroy the last valid save.
        var document = ToDocument(scene);
        var tempPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null);
            else File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static SceneGraph FromDocument(SceneDocument document)
    {
        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported scene version {document.Version}; expected {CurrentVersion}.");
        if (document.Objects is null)
            throw new InvalidDataException("Scene object list is missing.");

        var scene = new SceneGraph();
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var data in document.Objects)
        {
            ValidateData(data);
            if (parents.ContainsKey(data.Id))
                throw new InvalidDataException($"Duplicate scene object ID: {data.Id}.");
            var item = new SceneObject(data.Id, data.Name!)
            {
                Enabled = data.Enabled,
                Transform = ToTransform(data)
            };
            scene.Add(item);
            parents.Add(data.Id, data.ParentId);
        }

        foreach (var (id, parentId) in parents)
        {
            if (parentId is not null && scene.Find(parentId.Value) is null)
                throw new InvalidDataException($"Object {id} refers to missing parent {parentId}.");
            try
            {
                scene.SetParent(id, parentId);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException($"Invalid hierarchy at object {id}: {exception.Message}", exception);
            }
        }

        return scene;
    }

    private static SceneDocument ToDocument(SceneGraph scene)
    {
        var objects = scene.Objects
            .OrderBy(value => value.Id)
            .Select(value =>
            {
                var transform = value.Transform;
                var rotation = transform.Rotation;
                return new SceneObjectData
                {
                    Id = value.Id,
                    Name = value.Name,
                    Enabled = value.Enabled,
                    ParentId = value.ParentId,
                    Position = [transform.Position.X, transform.Position.Y, transform.Position.Z],
                    Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
                    Scale = [transform.Scale.X, transform.Scale.Y, transform.Scale.Z]
                };
            })
            .ToList();

        foreach (var data in objects) ValidateData(data);
        foreach (var data in objects)
            if (data.ParentId is not null && objects.All(item => item.Id != data.ParentId.Value))
                throw new InvalidDataException($"Object {data.Id} refers to missing parent {data.ParentId}.");

        ValidateAcyclic(objects);
        return new SceneDocument { Version = CurrentVersion, Objects = objects };
    }

    private static Transform ToTransform(SceneObjectData data) => new()
    {
        Position = new Vector3(data.Position![0], data.Position[1], data.Position[2]),
        Rotation = Quaternion.Normalize(new Quaternion(
            data.Rotation![0], data.Rotation[1], data.Rotation[2], data.Rotation[3])),
        Scale = new Vector3(data.Scale![0], data.Scale[1], data.Scale[2])
    };

    private static void ValidateData(SceneObjectData data)
    {
        if (data is null) throw new InvalidDataException("Scene contains a null object.");
        if (data.Id == Guid.Empty) throw new InvalidDataException("Scene object ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(data.Name)) throw new InvalidDataException($"Object {data.Id} has no name.");
        if (data.Position is null || data.Position.Length != 3)
            throw new InvalidDataException($"Object {data.Id} position must contain three values.");
        if (data.Rotation is null || data.Rotation.Length != 4)
            throw new InvalidDataException($"Object {data.Id} rotation must contain four values.");
        if (data.Scale is null || data.Scale.Length != 3)
            throw new InvalidDataException($"Object {data.Id} scale must contain three values.");

        if (data.Position.Any(value => !float.IsFinite(value))
            || data.Rotation.Any(value => !float.IsFinite(value))
            || data.Scale.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"Object {data.Id} contains a nonfinite transform value.");

        var rotationLength = MathF.Sqrt(data.Rotation.Sum(value => value * value));
        if (rotationLength < 0.000001f)
            throw new InvalidDataException($"Object {data.Id} has a zero-length rotation.");
    }

    private static void ValidateAcyclic(IReadOnlyList<SceneObjectData> objects)
    {
        var parents = objects.ToDictionary(value => value.Id, value => value.ParentId);
        foreach (var item in objects)
        {
            var visited = new HashSet<Guid>();
            var cursor = item.ParentId;
            while (cursor is not null)
            {
                if (!visited.Add(cursor.Value))
                    throw new InvalidDataException($"Object {item.Id} is part of a hierarchy cycle.");
                if (!parents.TryGetValue(cursor.Value, out cursor))
                    throw new InvalidDataException($"Object {item.Id} refers to missing parent.");
            }
        }
    }

    private sealed class SceneDocument
    {
        public int Version { get; set; }
        public List<SceneObjectData>? Objects { get; set; }
    }

    private sealed class SceneObjectData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public bool Enabled { get; set; } = true;
        public Guid? ParentId { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
    }
}
