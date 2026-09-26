using Ember.Authoring;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private string _projectValidationStatus = "Project not checked.";
    private IReadOnlyList<string> _projectValidationDiagnostics = Array.Empty<string>();

    private void ValidateAuthoredProject()
    {
        var result = AuthoredProjectValidator.Validate(_worldManifestPath, _rpgContentPath);
        _projectValidationDiagnostics = result.Diagnostics.Select(issue => issue.ToString()).ToArray();
        _projectValidationStatus = result.IsValid
            ? "No project reference errors found."
            : $"Found {result.Diagnostics.Count} project validation issue(s).";
    }

    private void DrawProjectValidationReport()
    {
        ImGui.TextWrapped(_projectValidationStatus);
        if (_projectValidationDiagnostics.Count == 0) return;
        ImGui.BeginChild("Project validation diagnostics", new NumericsVector2(0f, 96f), ImGuiChildFlags.Borders);
        foreach (var diagnostic in _projectValidationDiagnostics)
            ImGui.TextWrapped(diagnostic);
        ImGui.EndChild();
    }
}
