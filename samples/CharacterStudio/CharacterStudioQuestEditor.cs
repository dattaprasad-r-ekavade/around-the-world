using Ember.Rpg;
using ImGuiNET;
using System;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private static readonly QuestEventKind[] QuestEventKinds =
    [
        QuestEventKind.Interaction,
        QuestEventKind.ActorKilled,
        QuestEventKind.ItemCollected
    ];

    private string _newQuestId = "quest.new";
    private string _newQuestTitle = "New Quest";
    private string? _selectedQuestId;
    private string? _selectedQuestStageId;
    private string? _questDraftIdentity;
    private string _questTitleDraft = string.Empty;
    private string _questStartDialogueDraft = string.Empty;
    private string? _stageDraftIdentity;
    private string _questStageIdDraft = string.Empty;
    private string _questJournalDraft = string.Empty;
    private string _questActorDraft = string.Empty;
    private string _questItemDraft = string.Empty;
    private string _questInstanceDraft = string.Empty;
    private int _questEventKindIndex;
    private string? _questEditorMessage;

    private void DrawQuestAuthoringTab()
    {
        ImGui.BeginChild("Quest authoring", new NumericsVector2(0f, 0f), ImGuiChildFlags.Borders);
        if (_rpgContent is null)
        {
            ImGui.TextWrapped("Load an RPG content pack in the Placement tab before authoring quests.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextDisabled($"{_rpgContent.Quests.Count} quests · {_rpgContentPath}");
        ImGui.SetNextItemWidth(150f);
        ImGui.InputText("New quest ID", ref _newQuestId, 128);
        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("Title", ref _newQuestTitle, 256);
        if (ImGui.Button("Add quest")) AddQuestFromEditor();
        ImGui.SameLine();
        if (ImGui.Button("Reload pack")) LoadRpgPlacementContent();

        var quests = _rpgContent.Quests.All()
            .OrderBy(quest => quest.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var selectedQuest = GetSelectedQuest();
        if (ImGui.BeginCombo("Quest", selectedQuest?.Id.Value ?? "Select quest"))
        {
            foreach (var quest in quests)
            {
                var selected = quest.Id.Value == _selectedQuestId;
                if (ImGui.Selectable($"{quest.Id.Value} · {quest.Title}##quest-{quest.Id.Value}", selected))
                    SelectQuest(quest);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        selectedQuest = GetSelectedQuest();
        if (selectedQuest is not null)
        {
            SyncQuestDrafts(selectedQuest);
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Quest title", ref _questTitleDraft, 256);
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Start dialogue ID (optional)", ref _questStartDialogueDraft, 128);
            if (ImGui.Button("Apply quest details")) ApplyQuestDetails(selectedQuest);

            ImGui.Separator();
            ImGui.Text("Objectives (ordered)");
            ImGui.BeginChild("Quest objective list", new NumericsVector2(0f, 82f), ImGuiChildFlags.Borders);
            foreach (var stage in selectedQuest.Stages)
            {
                if (ImGui.Selectable($"{stage.Id} · {stage.CompleteOn?.ToString() ?? "invalid event"}##quest-stage-{stage.Id}",
                    stage.Id == _selectedQuestStageId))
                    SelectQuestStage(selectedQuest, stage.Id);
            }
            ImGui.EndChild();
            if (ImGui.Button("Add objective")) AddQuestStage(selectedQuest);

            selectedQuest = GetSelectedQuest() ?? selectedQuest;
            var selectedStage = selectedQuest.Stages.FirstOrDefault(stage => stage.Id == _selectedQuestStageId);
            if (selectedStage is not null)
            {
                SyncQuestStageDrafts(selectedStage);
                ImGui.Separator();
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Objective ID", ref _questStageIdDraft, 128);
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Journal text", ref _questJournalDraft, 512);
                var eventLabel = QuestEventKinds[_questEventKindIndex].ToString();
                if (ImGui.BeginCombo("Complete on event", eventLabel))
                {
                    for (var i = 0; i < QuestEventKinds.Length; i++)
                    {
                        var selected = i == _questEventKindIndex;
                        if (ImGui.Selectable(QuestEventKinds[i].ToString(), selected))
                            _questEventKindIndex = i;
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Target actor ID (optional)", ref _questActorDraft, 128);
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Required item ID (optional)", ref _questItemDraft, 128);
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("Target world-instance GUID (optional)", ref _questInstanceDraft, 64);
                if (ImGui.Button("Apply objective")) ApplyQuestStage(selectedQuest, selectedStage);
            }
        }

        var diagnostics = GetQuestDiagnostics(_rpgContent);
        ImGui.Separator();
        if (diagnostics.Length == 0)
            ImGui.TextColored(new NumericsVector4(0.35f, 0.9f, 0.5f, 1f),
                "Quest and content references validate.");
        else
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.5f, 0.3f, 1f),
                $"Validation found {diagnostics.Length} issue(s):");
            ImGui.BeginChild("Quest validation results", new NumericsVector2(0f, 75f), ImGuiChildFlags.Borders);
            foreach (var diagnostic in diagnostics) ImGui.BulletText(diagnostic);
            ImGui.EndChild();
        }

        var canSave = diagnostics.Length == 0;
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button("Save validated content pack")) SaveDialogueContent();
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextWrapped(_questEditorMessage ?? _rpgPlacementStatus);
        ImGui.EndChild();
    }

    private QuestDef? GetSelectedQuest() => _rpgContent is not null && _selectedQuestId is not null
        ? _rpgContent.Quests.Get(_selectedQuestId)
        : null;

    private void AddQuestFromEditor()
    {
        if (_rpgContent is null) return;
        var id = _newQuestId.Trim();
        if (id.Length == 0 || _rpgContent.Quests.TryGet(id, out _))
        {
            _questEditorMessage = $"Quest ID '{id}' is empty or already exists.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_newQuestTitle))
        {
            _questEditorMessage = "A quest title is required.";
            return;
        }
        try
        {
            var quest = new QuestDef { Id = new ContentId<QuestContentKind>(id), Title = _newQuestTitle.Trim() };
            _rpgContent.Quests.Add(quest);
            _selectedQuestId = id;
            _selectedQuestStageId = null;
            _questDraftIdentity = null;
            _questEditorMessage = $"Added quest '{id}'.";
        }
        catch (Exception exception) { _questEditorMessage = $"Could not add quest: {exception.Message}"; }
    }

    private void SelectQuest(QuestDef quest)
    {
        _selectedQuestId = quest.Id.Value;
        _selectedQuestStageId = quest.Stages.FirstOrDefault()?.Id;
        _questDraftIdentity = null;
        _stageDraftIdentity = null;
        _questEditorMessage = $"Selected quest '{quest.Id.Value}'.";
    }

    private void SyncQuestDrafts(QuestDef quest)
    {
        if (_questDraftIdentity == quest.Id.Value) return;
        _questDraftIdentity = quest.Id.Value;
        _questTitleDraft = quest.Title;
        _questStartDialogueDraft = quest.StartDialogueId?.Value ?? string.Empty;
    }

    private void ApplyQuestDetails(QuestDef quest)
    {
        if (string.IsNullOrWhiteSpace(_questTitleDraft))
        {
            _questEditorMessage = "A quest title is required.";
            return;
        }
        var dialogueId = _questStartDialogueDraft.Trim();
        _rpgContent!.Quests.Add(quest with
        {
            Title = _questTitleDraft.Trim(),
            StartDialogueId = dialogueId.Length == 0
                ? null
                : new ContentId<DialogueContentKind>(dialogueId)
        });
        _questEditorMessage = $"Updated quest '{quest.Id.Value}'.";
    }

    private void AddQuestStage(QuestDef quest)
    {
        var id = UniqueId("objective", quest.Stages.Select(stage => stage.Id));
        var stage = new QuestStage
        {
            Id = id,
            Journal = "Describe the objective for the player.",
            CompleteOn = QuestEventKind.Interaction
        };
        _rpgContent!.Quests.Add(quest with { Stages = quest.Stages.Append(stage).ToArray() });
        _selectedQuestStageId = id;
        _stageDraftIdentity = null;
        _questEditorMessage = $"Added objective '{id}'. Assign a target before saving.";
    }

    private void SelectQuestStage(QuestDef quest, string stageId)
    {
        _selectedQuestStageId = stageId;
        _stageDraftIdentity = null;
        var stage = quest.Stages.FirstOrDefault(candidate => candidate.Id == stageId);
        if (stage is not null) SyncQuestStageDrafts(stage);
    }

    private void SyncQuestStageDrafts(QuestStage stage)
    {
        if (_stageDraftIdentity == $"{_selectedQuestId}:{stage.Id}") return;
        _stageDraftIdentity = $"{_selectedQuestId}:{stage.Id}";
        _questStageIdDraft = stage.Id;
        _questJournalDraft = stage.Journal;
        _questActorDraft = stage.TargetActorId?.Value ?? string.Empty;
        _questItemDraft = stage.RequiredItemId?.Value ?? string.Empty;
        _questInstanceDraft = stage.TargetWorldInstanceId?.ToString("D") ?? string.Empty;
        _questEventKindIndex = Math.Max(0, Array.IndexOf(QuestEventKinds, stage.CompleteOn ?? QuestEventKind.Interaction));
    }

    private void ApplyQuestStage(QuestDef quest, QuestStage previous)
    {
        var id = _questStageIdDraft.Trim();
        if (id.Length == 0 || (id != previous.Id && quest.Stages.Any(stage => stage.Id == id)))
        {
            _questEditorMessage = $"Objective ID '{id}' is empty or already used in this quest.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_questJournalDraft))
        {
            _questEditorMessage = "Journal text is required for each objective.";
            return;
        }
        Guid? worldInstanceId = null;
        var worldIdText = _questInstanceDraft.Trim();
        if (worldIdText.Length > 0)
        {
            if (!Guid.TryParse(worldIdText, out var parsed) || parsed == Guid.Empty)
            {
                _questEditorMessage = "Target world-instance ID must be a nonempty GUID.";
                return;
            }
            worldInstanceId = parsed;
        }

        try
        {
            var actorText = _questActorDraft.Trim();
            var itemText = _questItemDraft.Trim();
            var replacement = previous with
            {
                Id = id,
                Journal = _questJournalDraft.Trim(),
                CompleteOn = QuestEventKinds[_questEventKindIndex],
                TargetActorId = actorText.Length == 0 ? null : new ContentId<ActorContentKind>(actorText),
                RequiredItemId = itemText.Length == 0 ? null : new ContentId<ItemContentKind>(itemText),
                TargetWorldInstanceId = worldInstanceId
            };
            _rpgContent!.Quests.Add(quest with
            {
                Stages = quest.Stages.Select(stage => stage.Id == previous.Id ? replacement : stage).ToArray()
            });
            _selectedQuestStageId = id;
            _stageDraftIdentity = $"{_selectedQuestId}:{id}";
            _questEditorMessage = $"Updated objective '{id}'.";
        }
        catch (Exception exception) { _questEditorMessage = $"Could not update objective: {exception.Message}"; }
    }

    private static string[] GetQuestDiagnostics(RpgContentSet content)
    {
        try { return content.Validate().Select(item => item.ToString()).ToArray(); }
        catch (Exception exception) { return new[] { $"Content validation failed: {exception.Message}" }; }
    }
}
