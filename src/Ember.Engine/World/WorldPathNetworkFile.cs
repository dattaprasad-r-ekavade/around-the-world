using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;

namespace Ember.World;

/// <summary>Versioned persistence for cross-cell path connections; per-cell graphs stay in their own files.</summary>
public static class WorldPathNetworkFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void SaveAtomic(string path, WorldPathNetwork network)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(network);
        network.Validate();
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, new NetworkDocument
        {
            Version = CurrentVersion,
            Connections = network.Connections
        }, JsonOptions));
    }

    public static WorldPathNetwork Load(string path, IReadOnlyList<CellPathGraph> cellGraphs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cellGraphs);
        using var stream = File.OpenRead(Path.GetFullPath(path));
        var document = JsonSerializer.Deserialize<NetworkDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("World path network document is empty.");
        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported world path network version {document.Version}; expected {CurrentVersion}.");
        var network = new WorldPathNetwork
        {
            Cells = cellGraphs,
            Connections = document.Connections
                ?? throw new InvalidDataException("World path network has no connection list.")
        };
        network.Validate();
        return network;
    }

    private sealed class NetworkDocument
    {
        public int Version { get; init; }
        public IReadOnlyList<WorldPathConnection>? Connections { get; init; }
    }
}
