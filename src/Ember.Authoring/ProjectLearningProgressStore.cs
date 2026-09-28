using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Project;

namespace Ember.Authoring;

/// <summary>Persists completed offline lessons with their project, without gating editing tools.</summary>
public static class ProjectLearningProgressStore
{
    public const string RelativePath = ".ember/learning-progress.json";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyList<LessonCompletion> Load(EngineProjectFile project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = project.ResolveContentPath(RelativePath);
        if (!File.Exists(path)) return Array.Empty<LessonCompletion>();

        LearningProgressDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LearningProgressDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Project learning progress is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Project learning progress is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported project learning progress version {document.Version}; expected {CurrentVersion}.");
        if (document.Lessons is null)
            throw new InvalidDataException("Project learning progress is missing its lesson list.");

        var result = new List<LessonCompletion>(document.Lessons.Count);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lesson in document.Lessons)
        {
            if (lesson is null || !IsLessonId(lesson.LessonId) || lesson.CompletionCount < 1
                || !DateTimeOffset.TryParseExact(lesson.CompletedAtUtc, "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var completedAt))
                throw new InvalidDataException("Project learning progress contains an invalid lesson completion.");

            string normalizedScenePath;
            try
            {
                normalizedScenePath = NormalizeScenePath(project,
                    project.ResolveContentPath(lesson.ScenePath), requireExists: false);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Project learning progress contains an invalid scene path: {exception.Message}", exception);
            }
            if (!keys.Add(Key(lesson.LessonId, normalizedScenePath)))
                throw new InvalidDataException("Project learning progress repeats a lesson and scene entry.");

            result.Add(new LessonCompletion(lesson.LessonId, normalizedScenePath,
                completedAt.ToUniversalTime(), lesson.CompletionCount));
        }

        return result.OrderBy(item => item.LessonId, StringComparer.Ordinal)
            .ThenBy(item => item.ScenePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static LessonCompletion RecordCompletion(EngineProjectFile project, string lessonId,
        string scenePath, DateTimeOffset? completedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsLessonId(lessonId))
            throw new ArgumentException("Lesson ID must use lowercase letters, digits, and hyphens.", nameof(lessonId));
        var normalizedScenePath = NormalizeScenePath(project, scenePath, requireExists: true);
        var existing = Load(project).ToList();
        var index = existing.FindIndex(item =>
            string.Equals(item.LessonId, lessonId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.ScenePath, normalizedScenePath, StringComparison.OrdinalIgnoreCase));
        var completed = new LessonCompletion(lessonId, normalizedScenePath,
            (completedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            index < 0 ? 1 : existing[index].CompletionCount + 1);
        if (index < 0) existing.Add(completed);
        else existing[index] = completed;

        var document = new LearningProgressDocument
        {
            Version = CurrentVersion,
            Lessons = existing.Select(item => new LessonCompletionRecord
            {
                LessonId = item.LessonId,
                ScenePath = item.ScenePath,
                CompletedAtUtc = item.CompletedAtUtc.ToString("O"),
                CompletionCount = item.CompletionCount
            }).ToList()
        };
        AtomicFile.Write(project.ResolveContentPath(RelativePath),
            stream => JsonSerializer.Serialize(stream, document, JsonOptions));
        return completed;
    }

    private static string NormalizeScenePath(EngineProjectFile project, string scenePath, bool requireExists)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            throw new ArgumentException("A completed lesson scene path is required.", nameof(scenePath));
        var fullPath = Path.GetFullPath(scenePath);
        var relative = Path.GetRelativePath(project.RootDirectory, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Lesson scene path must be a JSON scene inside the project.", nameof(scenePath));
        _ = project.ResolveContentPath(relative);
        if (requireExists && !File.Exists(fullPath))
            throw new FileNotFoundException("Completed lesson scene was not found inside the project.", fullPath);
        return relative.Replace('\\', '/');
    }

    private static bool IsLessonId(string? lessonId) => !string.IsNullOrWhiteSpace(lessonId)
        && lessonId.Length <= 64
        && lessonId.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static string Key(string lessonId, string scenePath) =>
        $"{lessonId.ToLowerInvariant()}|{scenePath.ToLowerInvariant()}";

    private sealed class LearningProgressDocument
    {
        public int Version { get; set; }
        public List<LessonCompletionRecord>? Lessons { get; set; }
    }

    private sealed class LessonCompletionRecord
    {
        public string LessonId { get; set; } = string.Empty;
        public string ScenePath { get; set; } = string.Empty;
        public string CompletedAtUtc { get; set; } = string.Empty;
        public int CompletionCount { get; set; }
    }
}

public sealed record LessonCompletion(string LessonId, string ScenePath,
    DateTimeOffset CompletedAtUtc, int CompletionCount);
