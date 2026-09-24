using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("World path network file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new NetworkDocument
                {
                    Version = CurrentVersion,
                    Connections = network.Connections
                }, JsonOptions);
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
