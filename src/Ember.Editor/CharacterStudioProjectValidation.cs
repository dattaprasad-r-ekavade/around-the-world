using Ember.Authoring;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private readonly ProjectValidationPanel _projectValidationPanel;

    private void ValidateAuthoredProject() => _projectValidationPanel.ValidateAuthoredProject();
    private void DrawProjectValidationReport() => _projectValidationPanel.DrawProjectValidationReport();

    private sealed class ProjectValidationPanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public ProjectValidationPanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    private string _projectValidationStatus = "Project not checked.";
    private IReadOnlyList<string> _projectValidationDiagnostics = Array.Empty<string>();

    public void ValidateAuthoredProject()
    {
        var result = AuthoredProjectValidator.Validate(_owner._worldManifestPath, _owner._rpgContentPath);
        _projectValidationDiagnostics = result.Diagnostics.Select(issue => issue.ToString()).ToArray();
        _projectValidationStatus = result.IsValid
            ? "No project reference errors found."
            : $"Found {result.Diagnostics.Count} project validation issue(s).";
    }

    public void DrawProjectValidationReport()
    {
        ImGui.TextWrapped(_projectValidationStatus);
        if (_projectValidationDiagnostics.Count == 0) return;
        ImGui.BeginChild("Project validation diagnostics", new NumericsVector2(0f, 96f), ImGuiChildFlags.Borders);
        foreach (var diagnostic in _projectValidationDiagnostics)
            ImGui.TextWrapped(diagnostic);
        ImGui.EndChild();
    }

    }
}
