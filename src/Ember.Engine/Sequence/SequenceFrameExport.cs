using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ember.Sequence;

/// <summary>Fixed scene-sequence output settings. Frame times are start + index / frame rate.</summary>
public sealed class SequenceFrameExportSettings
{
    public const int MaximumFrameRate = 240;
    public const int MaximumDimension = 16384;
    public const long MaximumPixelCount = 67_108_864;
    public const int MaximumFrameCount = 1_000_000;

    public SequenceFrameExportSettings(string outputDirectory, float startTime, float endTime,
        int frameRate, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        if (!float.IsFinite(startTime) || startTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Start time must be finite and nonnegative.");
        if (!float.IsFinite(endTime) || endTime <= startTime)
            throw new ArgumentOutOfRangeException(nameof(endTime), "End time must be finite and later than start time.");
        if (frameRate < 1 || frameRate > MaximumFrameRate)
            throw new ArgumentOutOfRangeException(nameof(frameRate), $"Frame rate must be between 1 and {MaximumFrameRate} fps.");
        if (width < 1 || width > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), $"Width must be between 1 and {MaximumDimension} pixels.");
        if (height < 1 || height > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(height), $"Height must be between 1 and {MaximumDimension} pixels.");
        if ((long)width * height > MaximumPixelCount)
            throw new ArgumentException($"The render target cannot exceed {MaximumPixelCount:N0} pixels.");

        var frameSpan = ((double)endTime - startTime) * frameRate;
        var floatPrecisionTolerance = Math.Max(1e-7, Math.Abs(frameSpan) * 1e-7);
        var count = Math.Ceiling(frameSpan - floatPrecisionTolerance);
        if (!double.IsFinite(count) || count < 1 || count > MaximumFrameCount)
            throw new ArgumentOutOfRangeException(nameof(endTime), $"Export must contain between 1 and {MaximumFrameCount:N0} frames.");

        OutputDirectory = Path.GetFullPath(outputDirectory);
        StartTime = startTime;
        EndTime = endTime;
        FrameRate = frameRate;
        Width = width;
        Height = height;
        FrameCount = (int)count;
    }

    public string OutputDirectory { get; }
    public float StartTime { get; }
    public float EndTime { get; }
    public int FrameRate { get; }
    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; }

    public float FrameTime(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        return (float)((double)StartTime + (double)frameIndex / FrameRate);
    }

    public string FramePath(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        return Path.Combine(OutputDirectory, $"frame_{frameIndex:D6}.png");
    }
}

/// <summary>A content-file version recorded in an export manifest.</summary>
public sealed record SequenceExportAssetVersion
{
    public SequenceExportAssetVersion(Guid assetId, string sourcePath, string sha256, long byteLength)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset ID cannot be empty.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Asset source path is required.", nameof(sourcePath));
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Asset SHA-256 must contain 64 hexadecimal characters.", nameof(sha256));
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        AssetId = assetId;
        SourcePath = sourcePath;
        Sha256 = sha256.ToLowerInvariant();
        ByteLength = byteLength;
    }

    public Guid AssetId { get; }
    public string SourcePath { get; }
    public string Sha256 { get; }
    public long ByteLength { get; }
}

public enum SequenceFrameExportState
{
    Running,
    Completed,
    Canceled,
    Failed
}

/// <summary>One requested frame passed to the render-thread capture callback.</summary>
public sealed record SequenceFrameExportRequest(
    int FrameIndex, float Time, int Width, int Height, string OutputPath);

/// <summary>
/// Incremental frame-export job. It writes a manifest before rendering and after every frame,
/// so interrupted, canceled, and failed outputs remain distinguishable from complete exports.
/// </summary>
public sealed class SequenceFrameExportJob
{
    public const string ManifestFileName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IReadOnlyList<SequenceExportAssetVersion> _assets;
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _finishedAtUtc;

    public SequenceFrameExportJob(SequenceFrameExportSettings settings, string sequenceName,
        IEnumerable<SequenceExportAssetVersion>? assetVersions = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(sequenceName))
            throw new ArgumentException("Sequence name is required.", nameof(sequenceName));
        var assets = (assetVersions ?? Array.Empty<SequenceExportAssetVersion>()).ToArray();
        if (assets.Any(asset => asset is null))
            throw new ArgumentException("Asset versions cannot contain null entries.", nameof(assetVersions));
        if (assets.Select(asset => asset.AssetId).Distinct().Count() != assets.Length)
            throw new ArgumentException("Asset version IDs must be unique.", nameof(assetVersions));

        SequenceName = sequenceName.Trim();
        _assets = Array.AsReadOnly(assets);
        ManifestPath = Path.Combine(Settings.OutputDirectory, ManifestFileName);
        Directory.CreateDirectory(Settings.OutputDirectory);
        if (File.Exists(ManifestPath))
            throw new IOException($"An export manifest already exists at '{ManifestPath}'. Choose an empty output folder.");
        WriteManifest(allowReplace: false);
    }

    public SequenceFrameExportSettings Settings { get; }
    public string SequenceName { get; }
    public string ManifestPath { get; }
    public SequenceFrameExportState State { get; private set; } = SequenceFrameExportState.Running;
    public int CompletedFrames { get; private set; }
    public int TotalFrames => Settings.FrameCount;
    public double Progress => TotalFrames == 0 ? 0d : (double)CompletedFrames / TotalFrames;
    public string? Error { get; private set; }
    public bool IsRunning => State == SequenceFrameExportState.Running;
    public bool IsComplete => State == SequenceFrameExportState.Completed;

    /// <summary>Render and write at most one frame. Call once per game draw to keep UI responsive.</summary>
    public bool ProcessNextFrame(Action<SequenceFrameExportRequest> renderAndWrite)
    {
        ArgumentNullException.ThrowIfNull(renderAndWrite);
        if (!IsRunning) return false;

        var frame = new SequenceFrameExportRequest(CompletedFrames,
            Settings.FrameTime(CompletedFrames), Settings.Width, Settings.Height,
            Settings.FramePath(CompletedFrames));
        try
        {
            renderAndWrite(frame);
            CompletedFrames++;
            if (CompletedFrames == TotalFrames)
            {
                State = SequenceFrameExportState.Completed;
                _finishedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception exception)
        {
            State = SequenceFrameExportState.Failed;
            Error = $"{exception.GetType().Name}: {exception.Message}";
            _finishedAtUtc = DateTimeOffset.UtcNow;
        }

        PersistAfterStateChange();
        return true;
    }

    /// <summary>Marks the export incomplete between frames; completed PNGs are left in place.</summary>
    public void Cancel()
    {
        if (!IsRunning) return;
        State = SequenceFrameExportState.Canceled;
        _finishedAtUtc = DateTimeOffset.UtcNow;
        PersistAfterStateChange();
    }

    private void PersistAfterStateChange()
    {
        try
        {
            WriteManifest(allowReplace: true);
        }
        catch (Exception exception)
        {
            State = SequenceFrameExportState.Failed;
            Error = AppendError(Error, $"Manifest write failed: {exception.GetType().Name}: {exception.Message}");
            _finishedAtUtc ??= DateTimeOffset.UtcNow;
            try { WriteManifest(allowReplace: true); }
            catch (Exception secondException)
            {
                Error = AppendError(Error,
                    $"Manifest retry failed: {secondException.GetType().Name}: {secondException.Message}");
            }
        }
    }

    private void WriteManifest(bool allowReplace)
    {
        var document = new SequenceFrameExportManifest(
            Version: 1,
            SequenceName,
            State.ToString().ToLowerInvariant(),
            _startedAtUtc,
            _finishedAtUtc,
            new SequenceFrameExportManifestSettings(Settings.OutputDirectory, Settings.StartTime,
                Settings.EndTime, Settings.FrameRate, Settings.Width, Settings.Height, Settings.FrameCount),
            TotalFrames,
            CompletedFrames,
            _assets,
            Error);
        var temporaryPath = ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, document, JsonOptions);
            if (File.Exists(ManifestPath))
            {
                if (!allowReplace)
                    throw new IOException($"An export manifest already exists at '{ManifestPath}'.");
                File.Move(temporaryPath, ManifestPath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, ManifestPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string AppendError(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : existing + Environment.NewLine + addition;
}

public sealed record SequenceFrameExportManifest(
    int Version,
    string SequenceName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    SequenceFrameExportManifestSettings Settings,
    int TotalFrames,
    int CompletedFrames,
    IReadOnlyList<SequenceExportAssetVersion> Assets,
    string? Error)
{
    public bool IsComplete => string.Equals(Status, "completed", StringComparison.Ordinal);
}

public sealed record SequenceFrameExportManifestSettings(
    string OutputDirectory,
    float StartTime,
    float EndTime,
    int FrameRate,
    int Width,
    int Height,
    int FrameCount);
