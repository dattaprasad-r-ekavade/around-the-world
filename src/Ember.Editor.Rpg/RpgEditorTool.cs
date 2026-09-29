using System;
using System.IO;
using System.Linq;
using Ember.Editor;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace Ember.Editor;

public sealed partial class RpgEditorTool : IEditorToolExtension, IDisposable
{
    private sealed record RpgPlacementOption(WorldEntityKind Kind, string Id, string Name);

    private RpgContentSet? _rpgContent;
    private string _rpgContentPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RpgPlacementDefinitions.json");
    private string _rpgPlacementStatus = "Load RPG placement definitions.";
    private string? _selectedRpgDefinitionKey;
    private NumericsVector3 _rpgPlacementPosition;
    private string? _loadedProjectFilePath;
    private bool _contentPathInitialized;

    public RpgEditorTool()
    {
        _dialoguePanel = new DialoguePanel(this);
        _questPanel = new QuestPanel(this);
    }

    public string Id => "ember.rpg.authoring";
    public string DisplayName => "RPG authoring";

    public void Draw(EditorToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SelectContentPath(context.ProjectFilePath);
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(450f, 520f));
        if (!ImGui.Begin("RPG Authoring", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("RPG authoring tabs"))
        {
            if (ImGui.BeginTabItem("Placement"))
            {
                DrawPlacement(context);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Dialogue"))
            {
                _dialoguePanel.DrawDialogueAuthoringTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Quests"))
            {
                _questPanel.DrawQuestAuthoringTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Project"))
            {
                DrawProjectRecovery(context);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void SelectContentPath(string? projectFilePath)
    {
        if (_contentPathInitialized
            && string.Equals(_loadedProjectFilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)) return;
        MaybeResetProjectState(projectFilePath);
        _loadedProjectFilePath = projectFilePath;
        _contentPathInitialized = true;
        _rpgContentPath = projectFilePath is null
            ? Path.Combine(AppContext.BaseDirectory, "Assets", "RpgPlacementDefinitions.json")
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!, "RpgContent.json");
        LoadRpgPlacementContent();
    }

    private void DrawPlacement(EditorToolContext context)
    {
        if (ImGui.Button("Reload pack")) LoadRpgPlacementContent();
        ImGui.SameLine();
        ImGui.TextDisabled(_rpgContent is null ? "No definitions loaded" :
            $"{_rpgContent.Actors.Count} actors · {_rpgContent.Items.Count} items");

        if (_rpgContent is not null)
        {
            var options = GetRpgPlacementOptions();
            ImGui.BeginChild("RPG definition list", new NumericsVector2(0f, 112f), ImGuiChildFlags.Borders);
            foreach (var option in options)
            {
                var key = $"{option.Kind}:{option.Id}";
                if (ImGui.Selectable($"{option.Name} · {option.Kind}##{key}",
                    string.Equals(_selectedRpgDefinitionKey, key, StringComparison.Ordinal)))
                    _selectedRpgDefinitionKey = key;
            }
            ImGui.EndChild();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputFloat3("Position", ref _rpgPlacementPosition);
            var selected = options.FirstOrDefault(option =>
                string.Equals($"{option.Kind}:{option.Id}", _selectedRpgDefinitionKey, StringComparison.Ordinal));
            var canPlace = selected is not null && !context.IsPlaying;
            if (!canPlace) ImGui.BeginDisabled();
            if (ImGui.Button("Place selected definition") && selected is not null)
                PlaceRpgDefinition(context, selected);
            if (!canPlace) ImGui.EndDisabled();
        }

        ImGui.TextWrapped(_rpgPlacementStatus);
    }

    private RpgPlacementOption[] GetRpgPlacementOptions()
    {
        if (_rpgContent is null) return [];
        return _rpgContent.Actors.All.Values
            .Select(actor => new RpgPlacementOption(WorldEntityKind.Actor, actor.Id.Value, actor.Name))
            .Concat(_rpgContent.Items.All.Values
                .Select(item => new RpgPlacementOption(WorldEntityKind.Item, item.Id.Value, item.Name)))
            .OrderBy(option => option.Kind)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private void PlaceRpgDefinition(EditorToolContext context, RpgPlacementOption option)
    {
        try
        {
            var placement = SceneObjectFactory.CreateWorldEntityPlacement(context.Scene, option.Kind, option.Id,
                option.Name, new Vector3(_rpgPlacementPosition.X, _rpgPlacementPosition.Y, _rpgPlacementPosition.Z));
            context.AddSceneObject(placement);
            _rpgPlacementStatus = $"Placed {option.Kind.ToString().ToLowerInvariant()} '{option.Name}' " +
                $"with instance ID {placement.WorldEntity!.InstanceId}.";
        }
        catch (Exception exception)
        {
            _rpgPlacementStatus = $"Could not place definition: {exception.Message}";
        }
    }

    private void LoadRpgPlacementContent()
    {
        _rpgContent = null;
        _selectedRpgDefinitionKey = null;
        try
        {
            var parsed = RpgContentJson.ParseForValidation(File.ReadAllText(_rpgContentPath));
            _rpgContent = parsed.Content;
            _rpgPlacementStatus = parsed.Diagnostics.Count == 0
                ? $"Loaded definitions from {Path.GetFileName(_rpgContentPath)}."
                : $"Loaded draft from {Path.GetFileName(_rpgContentPath)} with {parsed.Diagnostics.Count} validation issue(s).";
        }
        catch (Exception exception)
        {
            _rpgPlacementStatus = $"Could not load definitions: {exception.Message}";
        }
    }

}
