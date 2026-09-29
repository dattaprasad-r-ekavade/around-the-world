using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.IO;
using Ember.Scene;

namespace Ember.Authoring;

/// <summary>A validated template file, or a file that needs repair before it can be used.</summary>
public sealed record SceneTemplateLibraryEntry(
    string Path, SceneTemplateSnapshot? Template, string? Error)
{
    public bool IsValid => Template is not null;
}

/// <summary>Project-local storage and recovery for reusable scene templates.</summary>
public sealed class SceneTemplateLibrary
{
    public const string TemplateFilePattern = "*.embertemplate.json";
    private const string TemplateExtension = ".embertemplate.json";
    private readonly string _directory;

    public SceneTemplateLibrary(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        _directory = Path.Combine(ProjectDirectory, "Templates");
    }

    public string ProjectDirectory { get; }
    public string DirectoryPath => _directory;

    /// <summary>Reads every project template independently so one damaged file does not hide the rest.</summary>
    public IReadOnlyList<SceneTemplateLibraryEntry> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<SceneTemplateLibraryEntry>();

        var entries = new List<SceneTemplateLibraryEntry>();
        foreach (var path in Directory.EnumerateFiles(_directory, TemplateFilePattern, SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                entries.Add(new SceneTemplateLibraryEntry(Path.GetFullPath(path),
                    SceneTemplateFile.Load(path), null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                               or ArgumentException or InvalidDataException)
            {
                entries.Add(new SceneTemplateLibraryEntry(Path.GetFullPath(path), null, exception.Message));
            }
        }

        return entries;
    }

    /// <summary>Saves a new template without overwriting another template that has the same name.</summary>
    public SceneTemplateSnapshot SaveNew(SceneGraph scene, Guid rootObjectId, string name)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Directory.CreateDirectory(_directory);

        var stem = MakeFileStem(name);
        var path = Path.Combine(_directory, stem + TemplateExtension);
        for (var suffix = 2; File.Exists(path); suffix++)
            path = Path.Combine(_directory, $"{stem}-{suffix}{TemplateExtension}");

        return SceneTemplateFile.Save(scene, rootObjectId, name.Trim(), path);
    }

    /// <summary>Advances an existing template while preserving its ID and source root identity.</summary>
    public SceneTemplateSnapshot SaveRevision(SceneGraph scene, Guid rootObjectId,
        Guid templateId, string name)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (templateId == Guid.Empty) throw new ArgumentException("Template ID cannot be empty.", nameof(templateId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entry = ReadAll().FirstOrDefault(item => item.Template?.Id == templateId)
            ?? throw new FileNotFoundException($"Scene template {templateId} is not in this project library.");
        var existing = entry.Template!;
        if (existing.RootObjectId != rootObjectId)
            throw new InvalidOperationException("The selected hierarchy is not this template's original source root.");

        return SceneTemplateFile.Save(scene, rootObjectId, name.Trim(), entry.Path);
    }

    /// <summary>
    /// Relinks a missing project source by validating its stable ID, then atomically copying it into
    /// the project's template folder so the link survives project relocation.
    /// </summary>
    public SceneTemplateSnapshot Relink(Guid expectedTemplateId, string sourcePath)
    {
        if (expectedTemplateId == Guid.Empty)
            throw new ArgumentException("Template ID cannot be empty.", nameof(expectedTemplateId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = SceneTemplateFile.Load(fullSourcePath);
        if (source.Id != expectedTemplateId)
            throw new InvalidDataException(
                $"This file contains template {source.Id}, but the selected instance needs {expectedTemplateId}.");

        Directory.CreateDirectory(_directory);
        var destination = Path.GetFullPath(Path.Combine(_directory, expectedTemplateId.ToString("N") + TemplateExtension));
        if (!PathsEqual(fullSourcePath, destination))
        {
            AtomicFile.Write(destination, output =>
            {
                using var input = new FileStream(fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                input.CopyTo(output);
            });
        }

        return SceneTemplateFile.Load(destination);
    }

    private static string MakeFileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = name.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray();
        var stem = new string(chars).Trim('-', '_', '.');
        if (string.IsNullOrWhiteSpace(stem)) stem = "scene-template";
        if (invalid.Any(stem.Contains))
            stem = new string(stem.Where(character => !invalid.Contains(character)).ToArray());
        if (IsReservedWindowsName(stem)) stem = $"template-{stem}";
        return stem.Length > 64 ? stem[..64].Trim('-', '_', '.') : stem;
    }

    private static bool IsReservedWindowsName(string name)
    {
        var deviceName = name.Split('.')[0];
        return deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || deviceName.Length == 4
                && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && deviceName[3] is >= '1' and <= '9';
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
