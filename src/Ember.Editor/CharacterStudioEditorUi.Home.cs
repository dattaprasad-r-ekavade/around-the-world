using Ember.Scene;
using Ember.Render;
using Ember.Authoring;
using Ember.Project;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private readonly HomePanel _homePanel;

    private void DrawHomeWorkspace() => _homePanel.Draw();
    private string GetWorkspaceReadyMessage() => _homePanel.GetWorkspaceReadyMessage();
    private void BeginFirstCreationLesson(SceneGraph scene, string projectFilePath) =>
        _homePanel.BeginFirstCreationLesson(scene, projectFilePath);
    private void BeginLesson(SceneGraph scene, string projectFilePath, string lessonId) =>
        _homePanel.BeginLesson(scene, projectFilePath, lessonId);
    private void RefreshLessonHelpForCurrentStep() => _homePanel.RefreshLessonHelpForCurrentStep();
    private static bool IsSamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class HomePanel
    {
        private readonly CharacterStudioEditorUi _owner;
        private string _projectParentDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EmberProjects");
        private string _projectName = "New Game";

        public HomePanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        public void Draw()
        {
            ImGui.SetNextWindowPos(NumericsVector2.Zero);
            ImGui.SetNextWindowSize(_owner._io.DisplaySize);
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
            if (!ImGui.Begin("Ember Home", flags))
            {
                ImGui.End();
                return;
            }

            var background = ImGui.GetWindowDrawList();
            var windowPosition = ImGui.GetWindowPos();
            background.AddRectFilled(windowPosition, windowPosition + ImGui.GetWindowSize(),
                ImGui.GetColorU32(new NumericsVector4(0.025f, 0.035f, 0.05f, 1f)));
            if (_owner._history.IsDirty)
            {
                ImGui.TextColored(new NumericsVector4(1f, 0.76f, 0.30f, 1f),
                    "Unsaved scene changes · save before opening another project.");
                ImGui.Separator();
            }

            var panelWidth = Math.Min(800f, Math.Max(360f, _owner._logicalWidth - 48f));
            var panelHeight = Math.Min(510f, Math.Max(360f, _owner._logicalHeight - 48f));
            ImGui.SetCursorPos(new NumericsVector2(
                Math.Max(24f, (_owner._logicalWidth - panelWidth) * 0.5f),
                Math.Max(24f, (_owner._logicalHeight - panelHeight) * 0.5f)));
            var showHomeChoices = ImGui.BeginChild("Home choices",
                new NumericsVector2(panelWidth, panelHeight), ImGuiChildFlags.Borders);
            if (showHomeChoices)
            {
                ImGui.Dummy(new NumericsVector2(0f, 12f));
                ImGui.Text("EMBER · MAKE SOMETHING REAL");
                ImGui.TextWrapped("Choose a starting point. You can change direction whenever you like.");
                ImGui.Separator();

                ImGui.Text("Name your project");
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##homeProjectName", "Project name", ref _projectName, 128);
                ImGui.TextWrapped($"Save in: {_projectParentDirectory}");
                if (ImGui.Button("Choose a folder…"))
                {
                    var selectedFolder = CharacterStudioFilePickers.PickProjectParent(
                        _projectParentDirectory, _owner._windowHandle);
                    if (selectedFolder is not null) _projectParentDirectory = selectedFolder;
                }
                ImGui.Dummy(new NumericsVector2(0f, 6f));
                ImGui.Checkbox("Guide me through my first creation", ref _owner._guideNewProject);
                ImGui.TextDisabled("Optional: free creation always stays available.");
                ImGui.Dummy(new NumericsVector2(0f, 4f));

                var twoStarterColumns = panelWidth >= 620f;
                var cardWidth = twoStarterColumns
                    ? Math.Max(140f, (panelWidth - 48f) * 0.5f)
                    : Math.Max(140f, panelWidth - 24f);
                DrawStarterThumbnail(false, cardWidth, 72f);
                if (twoStarterColumns) ImGui.SameLine();
                DrawStarterThumbnail(true, cardWidth, 72f);
                if (ImGui.Button("Make a game\nCourtyard + two characters", new NumericsVector2(cardWidth, 72f)))
                    CreateProjectFromHome(isFilm: false);
                if (twoStarterColumns) ImGui.SameLine();
                if (ImGui.Button("Make a film\nCourtyard + one animated character", new NumericsVector2(cardWidth, 72f)))
                    CreateProjectFromHome(isFilm: true);

                ImGui.Dummy(new NumericsVector2(0f, 4f));
                if (ImGui.Button("Open a project…", new NumericsVector2(-1f, 38f)))
                {
                    var projectFile = CharacterStudioFilePickers.PickProjectFile(
                        _projectParentDirectory, _owner._windowHandle);
                    if (projectFile is not null) OpenProjectFromHome(projectFile);
                }

                var recentProjects = _owner._getRecentProjectPaths();
                if (recentProjects.Count > 0)
                {
                    var recentLabel = recentProjects.Select(path => Path.GetFileName(Path.GetDirectoryName(path)))
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Recent projects";
                    if (ImGui.BeginCombo("Recent projects", recentLabel))
                    {
                        foreach (var projectPath in recentProjects)
                        {
                            var name = Path.GetFileName(Path.GetDirectoryName(projectPath));
                            if (ImGui.Selectable($"{name}##{projectPath}"))
                                OpenProjectFromHome(projectPath);
                        }
                        ImGui.EndCombo();
                    }
                }

                if (_owner._getCurrentProjectPath() is not null || _owner._getCurrentScenePath() is not null)
                {
                    if (ImGui.Button("Continue editing", new NumericsVector2(-1f, 38f))) _owner._showHome = false;
                }
                if (!string.IsNullOrWhiteSpace(_owner._projectWorkspaceStatus))
                    ImGui.TextWrapped(_owner._projectWorkspaceStatus);
            }
            ImGui.EndChild();
            ImGui.End();
        }

        private static void DrawStarterThumbnail(bool isFilm, float width, float height)
        {
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            static uint Color(float red, float green, float blue) =>
                ImGui.GetColorU32(new NumericsVector4(red, green, blue, 1f));

            var right = origin.X + width;
            var bottom = origin.Y + height;
            drawList.AddRectFilled(origin, new NumericsVector2(right, bottom), Color(0.08f, 0.12f, 0.16f));
            drawList.AddRectFilled(new NumericsVector2(origin.X + 8f, origin.Y + height * 0.62f),
                new NumericsVector2(right - 8f, bottom - 7f), Color(0.11f, 0.24f, 0.22f));
            drawList.AddRectFilled(new NumericsVector2(origin.X + 18f, origin.Y + 14f),
                new NumericsVector2(origin.X + width * 0.38f, origin.Y + height * 0.66f), Color(0.37f, 0.25f, 0.18f));
            drawList.AddRectFilled(new NumericsVector2(right - width * 0.38f, origin.Y + 20f),
                new NumericsVector2(right - 18f, origin.Y + height * 0.66f), Color(0.42f, 0.29f, 0.20f));

            var characterCount = isFilm ? 1 : 2;
            for (var index = 0; index < characterCount; index++)
            {
                var centerX = origin.X + width * (isFilm ? 0.52f : index == 0 ? 0.50f : 0.72f);
                var centerY = origin.Y + height * 0.55f;
                var foxColor = Color(0.88f, 0.48f, 0.13f);
                drawList.AddRectFilled(new NumericsVector2(centerX - 13f, centerY - 4f),
                    new NumericsVector2(centerX + 12f, centerY + 8f), foxColor);
                drawList.AddCircleFilled(new NumericsVector2(centerX + 11f, centerY - 8f), 7f, foxColor, 12);
                drawList.AddRectFilled(new NumericsVector2(centerX + 12f, centerY - 17f),
                    new NumericsVector2(centerX + 16f, centerY - 10f), foxColor);
                drawList.AddLine(new NumericsVector2(centerX - 7f, centerY + 7f),
                    new NumericsVector2(centerX - 9f, centerY + 16f), foxColor, 3f);
                drawList.AddLine(new NumericsVector2(centerX + 5f, centerY + 7f),
                    new NumericsVector2(centerX + 7f, centerY + 16f), foxColor, 3f);
            }

            if (isFilm)
            {
                var frameColor = Color(0.82f, 0.83f, 0.72f);
                drawList.AddLine(new NumericsVector2(origin.X + 28f, origin.Y + 8f),
                    new NumericsVector2(right - 28f, origin.Y + 8f), frameColor, 2f);
                drawList.AddLine(new NumericsVector2(origin.X + 28f, origin.Y + 8f),
                    new NumericsVector2(origin.X + 28f, bottom - 15f), frameColor, 2f);
                drawList.AddLine(new NumericsVector2(right - 28f, origin.Y + 8f),
                    new NumericsVector2(right - 28f, bottom - 15f), frameColor, 2f);
                drawList.AddRectFilled(new NumericsVector2(origin.X + 8f, bottom - 5f),
                    new NumericsVector2(origin.X + width * 0.62f, bottom - 2f), Color(0.75f, 0.31f, 0.20f));
            }

            ImGui.Dummy(new NumericsVector2(width, height));
        }

        private void CreateProjectFromHome(bool isFilm)
        {
            _owner.RequestSaveBeforeContinue("creating a project", () => CreateProjectFromHomeNow(isFilm));
        }

        private void CreateProjectFromHomeNow(bool isFilm)
        {
            try
            {
                var projectName = _projectName.Trim();
                if (string.IsNullOrWhiteSpace(projectName) || projectName is "." or ".."
                    || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || projectName.EndsWith(".", StringComparison.Ordinal)
                    || projectName.EndsWith(" ", StringComparison.Ordinal))
                    throw new ArgumentException("Choose a project name that can be used as a folder name.");

                var projectDirectory = Path.Combine(_projectParentDirectory, projectName);
                _owner._projectWorkspaceStatus = _owner._createProject(projectDirectory, isFilm);
                _projectName = projectName;
                _owner._showSequenceTools = false;
                _owner._showWorldTools = false;
                _owner._showWorldAuthoringTools = false;
                _owner._showSceneTemplateTools = false;
                _owner._startLessonAfterProjectCreate = _owner._guideNewProject;
                _owner._showHome = false;
            }
            catch (Exception exception)
            {
                _owner._projectWorkspaceStatus = $"Could not create the project: {exception.Message}";
            }
        }

        private void OpenProjectFromHome(string path)
        {
            _owner.RequestSaveBeforeContinue("opening another project", () => OpenProjectFromHomeNow(path));
        }

        private void OpenProjectFromHomeNow(string path)
        {
            try
            {
                _owner._projectWorkspaceStatus = _owner._openProject(path);
                _owner._lessonProjectOpenCheck = path;
                _owner._showSequenceTools = false;
                _owner._showWorldTools = false;
                _owner._showWorldAuthoringTools = false;
                _owner._showSceneTemplateTools = false;
                _owner._showHome = false;
            }
            catch (Exception exception)
            {
                _owner._projectWorkspaceStatus = $"Could not open the project: {exception.Message}";
            }
        }

        public string GetWorkspaceReadyMessage()
        {
            var projectPath = _owner._getCurrentProjectPath();
            if (projectPath is not null)
            {
                var readyMessage = $"Ready to edit {Path.GetFileName(Path.GetDirectoryName(projectPath))}. Select an object to change it, or use Add to place something new.";
                try
                {
                    var completion = ProjectLearningProgressStore.Load(EngineProjectFile.Load(projectPath))
                        .FirstOrDefault(item => string.Equals(item.LessonId, "first-creation", StringComparison.Ordinal));
                    if (completion is not null)
                        return $"{readyMessage} First creation completed {completion.CompletionCount} time(s); choose First creation to replay it.";
                }
                catch (Exception exception)
                {
                    return $"{readyMessage} Saved lesson progress could not be read: {exception.Message}";
                }
                return readyMessage;
            }
            if (_owner._getCurrentScenePath() is not null)
                return "This scene is open on its own. Create or open a project to add models and keep your work together.";
            return "Choose Make a game or Make a film to create a project.";
        }

        public void BeginFirstCreationLesson(SceneGraph scene, string projectFilePath)
            => BeginLesson(scene, projectFilePath, "first-creation");

        public void BeginLesson(SceneGraph scene, string projectFilePath, string lessonId)
        {
            try
            {
                var definition = LessonDefinitionCatalog.LoadBuiltIn().FirstOrDefault(item =>
                    string.Equals(item.Id, lessonId, StringComparison.OrdinalIgnoreCase));
                if (definition is null)
                {
                    _owner._projectWorkspaceStatus = $"Lesson '{lessonId}' is not available.";
                    return;
                }
                if (!definition.Available)
                {
                    _owner._projectWorkspaceStatus =
                        $"{definition.Title} will be available after the {definition.RequiredFeature} feature gate passes.";
                    return;
                }
                _owner._firstCreationLesson = new FirstCreationLesson(scene, projectFilePath, definition);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException
                or ArgumentException or InvalidOperationException)
            {
                _owner._projectWorkspaceStatus = $"Could not load lesson data: {exception.Message}";
                _owner._showFirstCreationLesson = false;
                return;
            }
            _owner._showFirstCreationLesson = true;
            _owner._showLessonWhy = false;
            _owner._lessonHintLevel = 0;
            _owner._lastObservedLessonStep = null;
            _owner._lessonCompletionRecordedForSession = false;
            _owner._showAddLibrary = true;
            _owner._showHome = false;
        }

        public void RefreshLessonHelpForCurrentStep()
        {
            if (_owner._firstCreationLesson is not { } lesson) return;
            var currentStepId = lesson.CurrentStepId ?? "complete";
            if (_owner._lastObservedLessonStep == currentStepId) return;
            _owner._lastObservedLessonStep = currentStepId;
            _owner._showLessonWhy = false;
            _owner._lessonHintLevel = 0;
        }
    }
}
