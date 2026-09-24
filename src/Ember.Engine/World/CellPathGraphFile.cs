using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.World;

/// <summary>Versioned JSON persistence for one cell's authored path graph.</summary>
public static class CellPathGraphFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void SaveAtomic(string path, CellPathGraph graph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(graph);
        graph.Validate();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Navigation graph path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new NavigationDocument
                {
                    Version = CurrentVersion,
                    Graph = graph
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

    public static CellPathGraph Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        var document = JsonSerializer.Deserialize<NavigationDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Navigation graph document is empty.");
        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported navigation graph version {document.Version}; expected {CurrentVersion}.");
        var graph = document.Graph
            ?? throw new InvalidDataException("Navigation graph document has no graph.");
        graph.Validate();
        return graph;
    }

    private sealed class NavigationDocument
    {
        public int Version { get; init; }
        public CellPathGraph? Graph { get; init; }
    }
}
