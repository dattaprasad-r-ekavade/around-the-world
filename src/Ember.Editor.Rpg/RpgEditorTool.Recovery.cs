using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ember.Authoring;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace Ember.Editor;

public sealed partial class RpgEditorTool
{
    private sealed record RecoveryReviewResult(AuthoredProjectRecoveryStaging Staging,
        AuthoredProjectValidationResult? Validation, Exception? ValidationError);

    private const float RecoveryAutosaveIntervalSeconds = 60f;
    private AuthoredProjectRecoveryStaging? _recoveryStaging;
    private string _recoveryStatus = "No recovery snapshot reviewed.";
    private string _recoveryReport = string.Empty;
    private bool _recoveryCanApply;
    private Task<RecoveryReviewResult>? _recoveryReviewTask;
    private CancellationTokenSource? _recoveryReviewCancellation;
    private AuthoredProjectRecoveryProgress? _recoveryReviewProgress;
    private string? _recoveryReviewActivity;
    private string? _recoveryReviewProjectRoot;
    private string? _recoveryReviewDirectory;
    private string? _recoveryStagingDirectory;
    private float _recoveryAutosaveElapsedSeconds;
    private string? _observedWorldManifestPath;
    private WorldManifest? _recoveryWorldManifest;
    private string? _worldManifestError;
    private string _projectValidationStatus = "Project not checked.";
    private IReadOnlyList<string> _projectValidationDiagnostics = Array.Empty<string>();

    public void Update(EditorToolContext context, float elapsedSeconds)
    {
        SelectContentPath(context.ProjectFilePath);
        EnsureRecoveryWorldManifest(context.WorldManifestPath, context.WorldManifest);
        PollRecoveryReview(context);
        MaybeAutosaveAuthoredProject(context, elapsedSeconds);
    }

    public void Dispose()
    {
        try { _recoveryReviewCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }

        if (_recoveryReviewTask is { } task)
        {
            try
            {
                var review = task.GetAwaiter().GetResult();
                DeleteRecoveryStaging(review.Staging, _recoveryReviewDirectory);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"RPG recovery review cleanup failed: {exception}");
            }
        }

        if (_recoveryStaging is { } staging)
        {
            try { DeleteRecoveryStaging(staging, _recoveryStagingDirectory ?? _recoveryReviewDirectory); }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"RPG recovery staging cleanup failed: {exception}");
            }
        }
        _recoveryReviewCancellation?.Dispose();
        _recoveryReviewCancellation = null;
        _recoveryReviewTask = null;
    }

    private void DrawProjectRecovery(EditorToolContext context)
    {
        var canValidate = !string.IsNullOrWhiteSpace(context.WorldManifestPath);
        ImGui.BeginDisabled(!canValidate);
        if (ImGui.Button("Validate project")) ValidateProject(context);
        ImGui.EndDisabled();
        ImGui.TextWrapped(_projectValidationStatus);
        if (_projectValidationDiagnostics.Count > 0)
        {
            ImGui.BeginChild("RPG project validation diagnostics", new NumericsVector2(0f, 92f), ImGuiChildFlags.Borders);
            foreach (var diagnostic in _projectValidationDiagnostics) ImGui.TextWrapped(diagnostic);
            ImGui.EndChild();
        }
        if (!string.IsNullOrWhiteSpace(_worldManifestError)) ImGui.TextWrapped(_worldManifestError);

        ImGui.Separator();
        ImGui.Text("Authored recovery");
        var reviewRunning = _recoveryReviewTask is not null;
        if (reviewRunning) ImGui.BeginDisabled();
        var canCapture = _recoveryWorldManifest is not null && !context.IsPlaying && !context.IsOperationBusy;
        if (!canCapture) ImGui.BeginDisabled();
        if (ImGui.Button("Autosave now"))
        {
            try { _recoveryStatus = CaptureAuthoredProject(context); }
            catch (Exception exception) { _recoveryStatus = $"Autosave failed: {exception.Message}"; }
        }
        if (!canCapture) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!reviewRunning)
        {
            if (ImGui.Button("Review recovery")) StartRecoveryReview(context);
        }
        else
        {
            var cancelRequested = _recoveryReviewCancellation?.IsCancellationRequested == true;
            ImGui.BeginDisabled(cancelRequested);
            if (ImGui.Button(cancelRequested ? "Canceling recovery…" : "Cancel recovery"))
                CancelRecoveryReview();
            ImGui.EndDisabled();
            ImGui.TextDisabled(Volatile.Read(ref _recoveryReviewActivity) ?? "Restoring authored files…");
            if (Volatile.Read(ref _recoveryReviewProgress) is { TotalFiles: > 0 } progress)
            {
                var fraction = Math.Clamp(progress.CompletedFiles / (float)progress.TotalFiles, 0f, 1f);
                ImGui.ProgressBar(fraction, new NumericsVector2(-1f, 0f));
                ImGui.TextDisabled($"Restoring {progress.CompletedFiles} of {progress.TotalFiles}: {Path.GetFileName(progress.CurrentRelativePath)}");
            }
        }
        if (reviewRunning) ImGui.EndDisabled();

        ImGui.TextWrapped(_recoveryStatus);
        if (_recoveryStaging is { } reviewed)
        {
            ImGui.TextWrapped($"Reviewed snapshot {reviewed.SnapshotId:N} in staging.");
            ImGui.BeginDisabled(!_recoveryCanApply || reviewRunning || context.IsPlaying || context.IsOperationBusy);
            if (ImGui.Button("Apply reviewed recovery")) ApplyReviewedRecovery(context, reviewed);
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(_recoveryReport))
            {
                ImGui.BeginChild("RPG recovery validation report", new NumericsVector2(0f, 96f), ImGuiChildFlags.Borders);
                foreach (var line in _recoveryReport.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    ImGui.TextWrapped(line);
                ImGui.EndChild();
            }
        }
    }

    private void ValidateProject(EditorToolContext context)
    {
        try
        {
            var result = AuthoredProjectValidator.Validate(context.WorldManifestPath!, _rpgContentPath);
            _projectValidationDiagnostics = result.Diagnostics.Select(issue => issue.ToString()).ToArray();
            _projectValidationStatus = result.IsValid
                ? "No project reference errors found."
                : $"Found {result.Diagnostics.Count} project validation issue(s).";
        }
        catch (Exception exception)
        {
            _projectValidationDiagnostics = Array.Empty<string>();
            _projectValidationStatus = $"Project validation failed: {exception.Message}";
        }
    }

    private void EnsureRecoveryWorldManifest(string? worldManifestPath, WorldManifest? worldManifest)
    {
        if (SamePath(_observedWorldManifestPath, worldManifestPath)
            && ReferenceEquals(_recoveryWorldManifest, worldManifest)) return;
        _observedWorldManifestPath = worldManifestPath;
        _recoveryAutosaveElapsedSeconds = 0f;
        var pathMatches = worldManifest is not null && !string.IsNullOrWhiteSpace(worldManifestPath)
            && SamePath(worldManifest.FilePath, worldManifestPath);
        _recoveryWorldManifest = pathMatches ? worldManifest : null;
        _worldManifestError = _recoveryWorldManifest is null && !string.IsNullOrWhiteSpace(worldManifestPath)
            ? "Open the selected manifest in World Cells before using RPG validation or recovery."
            : null;
    }

    private void MaybeAutosaveAuthoredProject(EditorToolContext context, float elapsedSeconds)
    {
        if (_recoveryWorldManifest is null || _rpgContent is null || context.IsPlaying
            || context.IsOperationBusy || _recoveryReviewTask is not null)
        {
            _recoveryAutosaveElapsedSeconds = 0f;
            return;
        }

        if (!SceneBelongsToManifest(_recoveryWorldManifest, context.ScenePath)) return;
        _recoveryAutosaveElapsedSeconds += Math.Max(0f, elapsedSeconds);
        if (_recoveryAutosaveElapsedSeconds < RecoveryAutosaveIntervalSeconds) return;
        _recoveryAutosaveElapsedSeconds = 0f;
        try { _recoveryStatus = CaptureAuthoredProject(context); }
        catch (Exception exception) { _recoveryStatus = $"Autosave failed: {exception.Message}"; }
    }

    private string CaptureAuthoredProject(EditorToolContext context)
    {
        if (context.IsPlaying)
            throw new InvalidOperationException("Stop play mode before capturing an authoring recovery snapshot.");
        if (context.IsOperationBusy)
            throw new InvalidOperationException("Wait for the current export to finish before capturing recovery.");
        if (context.WorldManifestPath is null || context.ScenePath is null)
            throw new InvalidOperationException("Save the active scene in an authored world before capturing recovery.");

        context.CompletePendingEdits();
        var recoveryDirectory = AuthoredProjectRecoveryService.GetDefaultRecoveryDirectory(
            context.WorldManifestPath, _rpgContentPath);
        var sceneJson = Encoding.UTF8.GetBytes(SceneFile.ToJson(context.Scene));
        var contentJson = _rpgContent is null
            ? null
            : Encoding.UTF8.GetBytes(RpgContentJson.ToJsonForRecovery(_rpgContent));
        var snapshot = AuthoredProjectRecoveryService.Capture(context.WorldManifestPath, _rpgContentPath,
            recoveryDirectory, currentScenePath: context.ScenePath, currentSceneJson: sceneJson,
            currentRpgContentJson: contentJson);
        return $"Autosaved {snapshot.Files.Count} authored files outside the project.";
    }

    private void StartRecoveryReview(EditorToolContext context)
    {
        if (_recoveryReviewTask is not null) return;
        if (context.WorldManifestPath is null)
        {
            _recoveryStatus = "Open a world manifest before reviewing recovery.";
            return;
        }

        _recoveryStaging = null;
        _recoveryCanApply = false;
        _recoveryReport = string.Empty;
        Interlocked.Exchange(ref _recoveryReviewProgress, null);
        try
        {
            var manifestPath = Path.GetFullPath(context.WorldManifestPath);
            var rpgContentPath = Path.GetFullPath(_rpgContentPath);
            _recoveryReviewProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(manifestPath, rpgContentPath);
            _recoveryReviewDirectory = AuthoredProjectRecoveryService.GetDefaultRecoveryDirectory(manifestPath, rpgContentPath);
            var cancellation = new CancellationTokenSource();
            _recoveryReviewCancellation = cancellation;
            Interlocked.Exchange(ref _recoveryReviewActivity, "Restoring authored files…");
            var progress = new InlineRecoveryProgress(value => Interlocked.Exchange(ref _recoveryReviewProgress, value));
            _recoveryStatus = "Preparing recovery review…";
            var recoveryDirectory = _recoveryReviewDirectory
                ?? throw new InvalidOperationException("The recovery directory could not be resolved.");
            _recoveryReviewTask = Task.Run(async () =>
            {
                var staging = await AuthoredProjectRecoveryService.RestoreLatestToStagingAsync(
                    manifestPath, rpgContentPath, recoveryDirectory, cancellation.Token, progress)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _recoveryReviewActivity, "Validating restored project…");
                try
                {
                    var validation = AuthoredProjectValidator.Validate(staging.WorldManifestPath, staging.RpgContentPath);
                    return new RecoveryReviewResult(staging, validation, null);
                }
                catch (Exception exception)
                {
                    return new RecoveryReviewResult(staging, null, exception);
                }
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _recoveryReviewCancellation?.Dispose();
            _recoveryReviewCancellation = null;
            _recoveryReviewProjectRoot = null;
            _recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _recoveryReviewActivity, null);
            _recoveryStatus = $"Could not review recovery: {exception.Message}";
        }
    }

    private void CancelRecoveryReview()
    {
        try { _recoveryReviewCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void PollRecoveryReview(EditorToolContext context)
    {
        var task = _recoveryReviewTask;
        if (task is null || !task.IsCompleted) return;

        AuthoredProjectRecoveryStaging? staging = null;
        try
        {
            var review = task.GetAwaiter().GetResult();
            staging = review.Staging;
            if (_recoveryReviewCancellation?.IsCancellationRequested == true)
            {
                DeleteRecoveryStaging(staging, _recoveryReviewDirectory);
                staging = null;
                _recoveryStaging = null;
                _recoveryStagingDirectory = null;
                _recoveryCanApply = false;
                _recoveryReport = string.Empty;
                _recoveryStatus = "Recovery review canceled. Staged files were removed.";
                return;
            }

            if (review.ValidationError is not null)
                throw new InvalidDataException("The staged recovery could not be validated.", review.ValidationError);
            var validation = review.Validation
                ?? throw new InvalidDataException("The recovery review completed without a validation result.");
            var activeProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(
                context.WorldManifestPath ?? string.Empty, _rpgContentPath);
            if (_recoveryReviewProjectRoot is not { } requestedProjectRoot
                || !SameDirectory(staging.ProjectRoot, requestedProjectRoot)
                || !SameDirectory(staging.ProjectRoot, activeProjectRoot))
            {
                DeleteRecoveryStaging(staging, _recoveryReviewDirectory);
                staging = null;
                _recoveryStaging = null;
                _recoveryStagingDirectory = null;
                _recoveryCanApply = false;
                _recoveryReport = string.Empty;
                _recoveryStatus = "The active project changed during recovery review; staged files were discarded.";
                return;
            }

            _recoveryStaging = staging;
            _recoveryStagingDirectory = _recoveryReviewDirectory;
            _recoveryCanApply = validation.IsValid;
            _recoveryReport = validation.IsValid
                ? "Validation passed. This snapshot is ready to apply."
                : string.Join(Environment.NewLine, validation.Diagnostics.Select(value => value.ToString()));
            _recoveryStatus = validation.IsValid
                ? "Recovery staged and validated."
                : $"Recovery staged with {validation.Diagnostics.Count} validation issue(s); apply is disabled.";
        }
        catch (OperationCanceledException)
        {
            _recoveryStaging = null;
            _recoveryCanApply = false;
            _recoveryReport = string.Empty;
            _recoveryStatus = "Recovery review canceled. Partial staging files were removed.";
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            if (staging is not null)
            {
                try { DeleteRecoveryStaging(staging, _recoveryReviewDirectory); }
                catch (Exception cleanupError) { message += $" Staging cleanup failed: {cleanupError.Message}"; }
            }
            _recoveryStaging = null;
            _recoveryCanApply = false;
            _recoveryReport = string.Empty;
            _recoveryStatus = $"Could not review recovery: {message}";
        }
        finally
        {
            _recoveryReviewTask = null;
            _recoveryReviewCancellation?.Dispose();
            _recoveryReviewCancellation = null;
            _recoveryReviewProjectRoot = null;
            _recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _recoveryReviewActivity, null);
            Interlocked.Exchange(ref _recoveryReviewProgress, null);
        }
    }

    private void ApplyReviewedRecovery(EditorToolContext context, AuthoredProjectRecoveryStaging staging)
    {
        try
        {
            if (context.ScenePath is null)
                throw new InvalidOperationException("Save the active scene before applying recovery.");
            var relativeScenePath = Path.GetRelativePath(Path.GetFullPath(staging.ProjectRoot),
                Path.GetFullPath(context.ScenePath));
            EnsureWithin(staging.ProjectRoot, Path.Combine(staging.ProjectRoot, relativeScenePath));
            var stagedScenePath = Path.GetFullPath(Path.Combine(staging.StagingRoot,
                relativeScenePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithin(staging.StagingRoot, stagedScenePath);
            if (!File.Exists(stagedScenePath))
                throw new FileNotFoundException("The active scene is not present in the reviewed recovery snapshot.", stagedScenePath);

            context.CompletePendingEdits();
            _recoveryStatus = context.ApplyRecoveredProject(stagedScenePath, staging.ProjectRoot,
                () => AuthoredProjectRecoveryService.ApplyValidatedStaging(staging));
            context.RefreshWorld();
            DeleteRecoveryStaging(staging, _recoveryStagingDirectory ?? _recoveryReviewDirectory);
            _recoveryStaging = null;
            _recoveryStagingDirectory = null;
            _recoveryReport = string.Empty;
            _recoveryCanApply = false;
            _rpgContent = null;
            LoadRpgPlacementContent();
        }
        catch (Exception exception)
        {
            _recoveryStatus = $"Recovery apply failed: {exception.Message}";
        }
    }

    private void MaybeResetProjectState(string? projectFilePath)
    {
        if (string.Equals(_loadedProjectFilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)) return;
        CancelRecoveryReview();
        if (_recoveryStaging is { } staging)
        {
            try { DeleteRecoveryStaging(staging, _recoveryStagingDirectory ?? _recoveryReviewDirectory); }
            catch (Exception exception) { _recoveryStatus = $"Could not remove old recovery staging: {exception.Message}"; }
        }
        _recoveryStaging = null;
        _recoveryStagingDirectory = null;
        _recoveryCanApply = false;
        _recoveryReport = string.Empty;
        _recoveryAutosaveElapsedSeconds = 0f;
    }

    private static bool SceneBelongsToManifest(WorldManifest manifest, string? scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath)) return false;
        try
        {
            var fullScenePath = Path.GetFullPath(scenePath);
            return manifest.Cells.Any(cell => string.Equals(Path.GetFullPath(manifest.ResolveScenePath(cell.Id)),
                fullScenePath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool SamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second);
        try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        { return string.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }

    private static bool SameDirectory(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static void DeleteRecoveryStaging(AuthoredProjectRecoveryStaging staging, string? recoveryDirectory)
    {
        if (string.IsNullOrWhiteSpace(recoveryDirectory))
            throw new InvalidOperationException("The recovery staging directory is unavailable for cleanup.");
        var stagingParent = Path.GetFullPath(Path.Combine(recoveryDirectory, "staging"));
        var stagingRoot = Path.GetFullPath(staging.StagingRoot);
        EnsureWithin(stagingParent, stagingRoot);
        if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
    }

    private static void EnsureWithin(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (Path.IsPathRooted(relative) || relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery staging path escapes its expected directory.");
    }

    private sealed class InlineRecoveryProgress(Action<AuthoredProjectRecoveryProgress> report)
        : IProgress<AuthoredProjectRecoveryProgress>
    {
        public void Report(AuthoredProjectRecoveryProgress value) => report(value);
    }
}
