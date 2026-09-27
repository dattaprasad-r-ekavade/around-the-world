using System;
using System.IO;
using System.Linq;
using Ember.Project;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private string _projectPathInput = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EmberProjects", "New Game");
    private string _glbImportPath = string.Empty;
    private string _projectWorkspaceStatus = "Create a project or open an existing ember.project.json.";

    private void DrawProjectWorkspace()
    {
        ImGui.SetNextWindowPos(new NumericsVector2(16f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(370f, 200f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Project", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        var currentProject = _getCurrentProjectPath();
        ImGui.TextDisabled(currentProject is null
            ? "No project open"
            : $"Project: {Path.GetFileName(Path.GetDirectoryName(currentProject))}");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##projectPath", "Project folder to create, or project file/folder to open",
            ref _projectPathInput, 1024);
        if (ImGui.Button("Create project")) RunProjectAction(() => _createProject(_projectPathInput));
        ImGui.SameLine();
        if (ImGui.Button("Open project")) RunProjectAction(() => _openProject(_projectPathInput));

        try
        {
            var recentProjects = _getRecentProjectPaths();
            var selectedRecent = recentProjects.FirstOrDefault();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("Recent projects", selectedRecent ?? "No recent projects"))
            {
                foreach (var projectPath in recentProjects)
                {
                    var name = Path.GetFileName(Path.GetDirectoryName(projectPath));
                    if (ImGui.Selectable($"{name} — {projectPath}##{projectPath}",
                            string.Equals(projectPath, currentProject, StringComparison.OrdinalIgnoreCase)))
                    {
                        _projectPathInput = projectPath;
                        RunProjectAction(() => _openProject(projectPath));
                    }
                }
                ImGui.EndCombo();
            }
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not read recent projects: {exception.Message}";
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##glbImportPath", "Path to a static or animated .glb file",
            ref _glbImportPath, 1024);
        var canImport = currentProject is not null;
        if (!canImport) ImGui.BeginDisabled();
        if (ImGui.Button("Import and place GLB")) RunProjectAction(() => _importGlb(_glbImportPath));
        if (!canImport) ImGui.EndDisabled();

        ImGui.TextWrapped(_projectWorkspaceStatus);
        ImGui.End();
    }

    private void RunProjectAction(Func<string> action)
    {
        try
        {
            _projectWorkspaceStatus = action();
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Project operation failed: {exception.Message}";
        }
    }
}
