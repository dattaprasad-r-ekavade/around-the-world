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
    public const int CurrentVersion = 2;

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
        if (document.Version != 1 && document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported scene version {document.Version}; expected 1 or {CurrentVersion}.");
        if (document.Objects is null)
            throw new InvalidDataException("Scene object list is missing.");
        foreach (var data in document.Objects) ValidateData(data, document.Version);
        ValidateAssetReferences(document.Objects);
        ValidateAttachmentIds(document.Objects);

        var scene = new SceneGraph();
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var data in document.Objects)
        {
            if (parents.ContainsKey(data.Id))
                throw new InvalidDataException($"Duplicate scene object ID: {data.Id}.");
            var item = new SceneObject(data.Id, data.Name!)
            {
                Enabled = data.Enabled,
                Transform = ToTransform(data),
                GltfAsset = ToGltfAsset(data),
                CharacterSettings = ToCharacterSettings(data.Character)
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
                    GltfAssetId = value.GltfAsset?.AssetId,
                    GltfAssetPath = value.GltfAsset?.SourcePath,
                    Character = ToCharacterData(value.CharacterSettings),
                    Position = [transform.Position.X, transform.Position.Y, transform.Position.Z],
                    Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
                    Scale = [transform.Scale.X, transform.Scale.Y, transform.Scale.Z]
                };
            })
            .ToList();

        foreach (var data in objects) ValidateData(data, CurrentVersion);
        foreach (var data in objects)
            if (data.ParentId is not null && objects.All(item => item.Id != data.ParentId.Value))
                throw new InvalidDataException($"Object {data.Id} refers to missing parent {data.ParentId}.");

        ValidateAssetReferences(objects);
        ValidateAttachmentIds(objects);
        ValidateAcyclic(objects);
        return new SceneDocument { Version = CurrentVersion, Objects = objects };
    }

    private static GltfCharacterSettings? ToCharacterSettings(SceneCharacterData? data)
    {
        if (data is null) return null;
        var settings = new GltfCharacterSettings
        {
            ClipName = data.ClipName,
            Time = data.Time,
            Speed = data.Speed,
            Loop = data.Loop,
            IsPlaying = data.IsPlaying,
            CrossfadeClipName = data.CrossfadeClipName,
            BlendAmount = data.BlendAmount
        };
        foreach (var attachment in data.Attachments!)
            settings.Attachments.Add(new GltfBoneAttachmentReference(
                attachment.Id, attachment.BoneName!, ToMatrix(attachment.LocalOffset!)));
        return settings;
    }

    private static SceneCharacterData? ToCharacterData(GltfCharacterSettings? settings)
    {
        if (settings is null) return null;
        return new SceneCharacterData
        {
            ClipName = settings.ClipName,
            Time = settings.Time,
            Speed = settings.Speed,
            Loop = settings.Loop,
            IsPlaying = settings.IsPlaying,
            CrossfadeClipName = settings.CrossfadeClipName,
            BlendAmount = settings.BlendAmount,
            Attachments = settings.Attachments.Select(attachment => new SceneAttachmentData
            {
                Id = attachment.Id,
                BoneName = attachment.BoneName,
                LocalOffset = ToArray(attachment.LocalOffset)
            }).ToList()
        };
    }

    private static GltfAssetReference? ToGltfAsset(SceneObjectData data) =>
        data.GltfAssetId is { } assetId
            ? new GltfAssetReference(assetId, data.GltfAssetPath!)
            : null;

    private static void ValidateAssetReferences(IEnumerable<SceneObjectData> objects)
    {
        var pathsById = new Dictionary<Guid, string>();
        foreach (var data in objects)
        {
            if (data.GltfAssetId is not { } assetId) continue;
            var reference = new GltfAssetReference(assetId, data.GltfAssetPath!);
            if (pathsById.TryGetValue(assetId, out var existingPath)
                && !string.Equals(existingPath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"GLB asset ID {assetId} refers to more than one source path.");
            }

            pathsById[assetId] = reference.SourcePath;
        }
    }

    private static Transform ToTransform(SceneObjectData data) => new()
    {
        Position = new Vector3(data.Position![0], data.Position[1], data.Position[2]),
        Rotation = Quaternion.Normalize(new Quaternion(
            data.Rotation![0], data.Rotation[1], data.Rotation[2], data.Rotation[3])),
        Scale = new Vector3(data.Scale![0], data.Scale[1], data.Scale[2])
    };

    private static void ValidateData(SceneObjectData data, int documentVersion)
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
        if (data.GltfAssetId.HasValue != (data.GltfAssetPath is not null))
            throw new InvalidDataException($"Object {data.Id} must provide both GLB asset ID and source path.");
        if (data.GltfAssetId == Guid.Empty)
            throw new InvalidDataException($"Object {data.Id} has an empty GLB asset ID.");
        if (data.GltfAssetId.HasValue)
        {
            try { _ = new GltfAssetReference(data.GltfAssetId.Value, data.GltfAssetPath!); }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Object {data.Id} has an invalid GLB asset reference: {exception.Message}", exception);
            }
        }

        if (data.Position.Any(value => !float.IsFinite(value))
            || data.Rotation.Any(value => !float.IsFinite(value))
            || data.Scale.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"Object {data.Id} contains a nonfinite transform value.");

        var rotationLength = MathF.Sqrt(data.Rotation.Sum(value => value * value));
        if (rotationLength < 0.000001f)
            throw new InvalidDataException($"Object {data.Id} has a zero-length rotation.");

        if (data.Character is { } character)
        {
            if (documentVersion < 2)
                throw new InvalidDataException($"Object {data.Id} character settings require scene version 2.");
            if (!data.GltfAssetId.HasValue)
                throw new InvalidDataException($"Object {data.Id} has character settings but no GLB asset reference.");
            if (character.ClipName is not null && string.IsNullOrWhiteSpace(character.ClipName))
                throw new InvalidDataException($"Object {data.Id} has an empty animation clip name.");
            if (character.CrossfadeClipName is not null
                && (string.IsNullOrWhiteSpace(character.CrossfadeClipName) || character.ClipName is null))
                throw new InvalidDataException($"Object {data.Id} has invalid crossfade clip settings.");
            if (!float.IsFinite(character.Time) || character.Time < 0f
                || !float.IsFinite(character.Speed)
                || !float.IsFinite(character.BlendAmount) || character.BlendAmount < 0f || character.BlendAmount > 1f)
                throw new InvalidDataException($"Object {data.Id} has invalid character playback values.");
            if (character.IsPlaying && character.ClipName is null)
                throw new InvalidDataException($"Object {data.Id} cannot play without an animation clip.");
            if (character.Attachments is null)
                throw new InvalidDataException($"Object {data.Id} has no attachment list.");
            foreach (var attachment in character.Attachments)
            {
                if (attachment is null || attachment.Id == Guid.Empty || string.IsNullOrWhiteSpace(attachment.BoneName))
                    throw new InvalidDataException($"Object {data.Id} contains an invalid attachment reference.");
                if (attachment.LocalOffset is null || attachment.LocalOffset.Length != 16
                    || attachment.LocalOffset.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException($"Object {data.Id} attachment {attachment.Id} has an invalid local offset matrix.");
            }
        }
    }

    private static void ValidateAttachmentIds(IEnumerable<SceneObjectData> objects)
    {
        var ids = new HashSet<Guid>();
        foreach (var data in objects)
        foreach (var attachment in data.Character?.Attachments ?? [])
        {
            if (!ids.Add(attachment.Id))
                throw new InvalidDataException($"Duplicate character attachment ID: {attachment.Id}.");
        }
    }

    private static float[] ToArray(Matrix value) =>
    [
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
    ];

    private static Matrix ToMatrix(float[] values) => new(
        values[0], values[1], values[2], values[3],
        values[4], values[5], values[6], values[7],
        values[8], values[9], values[10], values[11],
        values[12], values[13], values[14], values[15]);

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
        public Guid? GltfAssetId { get; set; }
        public string? GltfAssetPath { get; set; }
        public SceneCharacterData? Character { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
    }

    private sealed class SceneCharacterData
    {
        public string? ClipName { get; set; }
        public float Time { get; set; }
        public float Speed { get; set; } = 1f;
        public bool Loop { get; set; } = true;
        public bool IsPlaying { get; set; }
        public string? CrossfadeClipName { get; set; }
        public float BlendAmount { get; set; } = 0.5f;
        public List<SceneAttachmentData>? Attachments { get; set; } = new();
    }

    private sealed class SceneAttachmentData
    {
        public Guid Id { get; set; }
        public string? BoneName { get; set; }
        public float[]? LocalOffset { get; set; }
    }
}
