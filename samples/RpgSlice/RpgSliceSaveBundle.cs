using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Rpg;
using Ember.World;

namespace RpgSlice;

internal sealed record RpgSliceSaveBundle(WorldSaveSnapshot World, SaveState Rpg)
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void SaveAtomic(string path, WorldSaveSnapshot world, SaveState rpg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rpg);

        var document = new BundleDocument
        {
            Version = CurrentVersion,
            WorldSaveJson = WorldSaveFile.SerializeToJson(world),
            RpgSaveJson = rpg.ToJson()
        };
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, document, JsonOptions));
    }

    public static RpgSliceSaveBundle Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        BundleDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BundleDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Game save bundle is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Game save bundle JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported game save bundle version {document.Version}; expected {CurrentVersion}.");
        var worldJson = document.WorldSaveJson
            ?? throw new InvalidDataException("Game save bundle has no world save.");
        var rpgJson = document.RpgSaveJson
            ?? throw new InvalidDataException("Game save bundle has no RPG save.");
        return new RpgSliceSaveBundle(WorldSaveFile.LoadFromJson(worldJson), SaveState.FromJson(rpgJson));
    }

    private sealed class BundleDocument
    {
        public int Version { get; init; } = CurrentVersion;
        public string? WorldSaveJson { get; init; }
        public string? RpgSaveJson { get; init; }
    }
}
