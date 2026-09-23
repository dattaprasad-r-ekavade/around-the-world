using System;
using System.IO;
using System.Text.Json;

namespace Ember.Project;

/// <summary>Versioned project configuration with one safe project-relative startup scene.</summary>
public sealed class EngineProjectFile
{
    public const int CurrentVersion = 1;
    public const string DefaultFileName = "ember.project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private EngineProjectFile(string filePath, string startupScenePath)
    {
        FilePath = filePath;
        RootDirectory = Path.GetDirectoryName(filePath)!;
        StartupScenePath = NormalizeStartupScenePath(startupScenePath);
    }

    public string FilePath { get; }
    public string RootDirectory { get; }
    public string StartupScenePath { get; }

    public string ResolveStartupScenePath()
    {
        var fullPath = ResolveContentPath(StartupScenePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Startup scene '{StartupScenePath}' was not found for project '{FilePath}' at '{fullPath}'.",
                fullPath);
        return fullPath;
    }

    /// <summary>Resolve a project-relative content file without allowing it to escape the project root.</summary>
    public string ResolveContentPath(string projectRelativePath)
    {
        var normalized = NormalizeRelativeContentPath(projectRelativePath);
        var fullPath = Path.GetFullPath(normalized, RootDirectory);
        var relativePath = Path.GetRelativePath(RootDirectory, fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Project content path '{projectRelativePath}' escapes project root '{RootDirectory}'.");
        return fullPath;
    }

    public static EngineProjectFile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        ProjectDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(fullPath), JsonOptions)
                ?? throw new InvalidDataException("Project JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Project JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported project version {document.Version}; expected {CurrentVersion}.");
        if (string.IsNullOrWhiteSpace(document.StartupScene))
            throw new InvalidDataException("Project startupScene path is missing.");

        EngineProjectFile project;
        try
        {
            project = new EngineProjectFile(fullPath, document.StartupScene);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Project startupScene path is invalid: {exception.Message}", exception);
        }
        _ = project.ResolveStartupScenePath();
        return project;
    }

    public static void SaveAtomic(string path, string startupScenePath)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project file path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(startupScenePath))
            throw new ArgumentException("A startup scene path is required.", nameof(startupScenePath));

        var fullPath = Path.GetFullPath(path);
        var normalizedStartupScene = NormalizeStartupScenePath(startupScenePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Project file path has no parent directory.");
        Directory.CreateDirectory(directory);

        var document = new ProjectDocument
        {
            Version = CurrentVersion,
            StartupScene = normalizedStartupScene
        };
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, document, JsonOptions);
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string NormalizeStartupScenePath(string path)
    {
        try
        {
            var normalized = NormalizeRelativeContentPath(path);
            if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Startup scene path must name a .json scene file.", nameof(path));
            return normalized;
        }
        catch (ArgumentException exception) when (!exception.Message.StartsWith("Startup scene path", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Startup scene path is invalid: {exception.Message}", nameof(path), exception);
        }
    }

    private static string NormalizeRelativeContentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Project content path is required.", nameof(path));

        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            || Path.IsPathRooted(normalized))
            throw new ArgumentException("Project content path must be relative to the project file.", nameof(path));

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Project content path is required.", nameof(path));
        foreach (var segment in segments)
        {
            if (segment == "..")
                throw new ArgumentException("Project content path cannot contain '..' segments.", nameof(path));
        }

        var result = string.Join('/', Array.FindAll(segments, segment => segment != "."));
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("Project content path is required.", nameof(path));
        return result;
    }

    private sealed class ProjectDocument
    {
        public int Version { get; set; }
        public string? StartupScene { get; set; }
    }
}
