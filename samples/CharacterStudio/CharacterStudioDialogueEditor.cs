using Ember.Rpg;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private string? _selectedDialogueId;
    private string? _selectedDialogueNodeId;
    private string? _dialogueNodeIdDraftTarget;
    private string _dialogueNodeIdDraft = string.Empty;
    private int? _selectedDialogueOptionIndex;
    private int? _selectedDialogueConditionIndex;
    private string? _selectedDialogueEffectName;
    private string _newDialogueId = "dialogue.new";
    private string _newFactionRequirementId = "factions.example";

    private void DrawDialogueAuthoringTab()
    {
        ImGui.BeginChild("Dialogue authoring", new NumericsVector2(0f, 0f), ImGuiChildFlags.Borders);
        if (_rpgContent is null)
        {
            ImGui.TextWrapped("Load an RPG content pack in the Placement tab before authoring dialogue.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextDisabled($"{_rpgContent.Dialogues.Count} conversation records · {_rpgContentPath}");
        ImGui.SetNextItemWidth(210f);
        ImGui.InputText("New dialogue ID", ref _newDialogueId, 128);
        ImGui.SameLine();
        if (ImGui.Button("Add dialogue")) AddDialogue();
        ImGui.SameLine();
        if (ImGui.Button("Reload pack")) LoadRpgPlacementContent();

        var selectedTree = GetSelectedDialogue();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("Conversation", selectedTree?.Id.Value ?? "Select conversation"))
        {
            foreach (var tree in _rpgContent.Dialogues.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                var selected = selectedTree?.Id == tree.Id;
                if (ImGui.Selectable(tree.Id.Value, selected))
                {
                    _selectedDialogueId = tree.Id.Value;
                    _selectedDialogueNodeId = tree.Nodes.FirstOrDefault()?.Id;
                    _dialogueNodeIdDraftTarget = _selectedDialogueNodeId;
                    _dialogueNodeIdDraft = _selectedDialogueNodeId ?? string.Empty;
                    _selectedDialogueOptionIndex = null;
                    _selectedDialogueConditionIndex = null;
                    _selectedDialogueEffectName = null;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        selectedTree = GetSelectedDialogue();
        if (selectedTree is not null) DrawDialogueTreeEditor(selectedTree);

        var diagnostics = GetDialogueDiagnostics(_rpgContent);
        ImGui.Separator();
        if (diagnostics.Count == 0)
            ImGui.TextColored(new NumericsVector4(0.35f, 0.9f, 0.5f, 1f), "Content references and dialogue records validate.");
        else
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.5f, 0.3f, 1f), $"Validation found {diagnostics.Count} issue(s):");
            ImGui.BeginChild("Dialogue validation results", new NumericsVector2(0f, 92f), ImGuiChildFlags.Borders);
            foreach (var diagnostic in diagnostics) ImGui.BulletText(diagnostic);
            ImGui.EndChild();
        }

        var canSave = diagnostics.Count == 0;
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button("Save validated content pack")) SaveDialogueContent();
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextWrapped(_rpgPlacementStatus);
        ImGui.EndChild();
    }

    private void DrawDialogueTreeEditor(DialogueTree tree)
    {
        ImGui.TextDisabled("The first node is the conversation entry. Nodes and choices are saved in this order.");
        ImGui.BeginChild("Dialogue nodes", new NumericsVector2(0f, 88f), ImGuiChildFlags.Borders);
        foreach (var listedNode in tree.Nodes)
        {
            if (ImGui.Selectable($"{listedNode.Id} · {ShortText(listedNode.Text, 48)}##dialogue-node-{listedNode.Id}",
                listedNode.Id == _selectedDialogueNodeId))
            {
                _selectedDialogueNodeId = listedNode.Id;
                _dialogueNodeIdDraftTarget = listedNode.Id;
                _dialogueNodeIdDraft = listedNode.Id;
                _selectedDialogueOptionIndex = null;
                _selectedDialogueConditionIndex = null;
                _selectedDialogueEffectName = null;
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Add node")) AddDialogueNode(tree);

        tree = GetSelectedDialogue() ?? tree;
        var nodeIndex = tree.Nodes.FindIndex(node => node.Id == _selectedDialogueNodeId);
        if (nodeIndex < 0)
        {
            ImGui.TextDisabled("Select a node to edit its speaker, text, and choices.");
            return;
        }

        var node = tree.Nodes[nodeIndex];
        ImGui.Separator();
        ImGui.Text($"Node: {node.Id}");
        if (_dialogueNodeIdDraftTarget != node.Id)
        {
            _dialogueNodeIdDraftTarget = node.Id;
            _dialogueNodeIdDraft = node.Id;
        }
        ImGui.SetNextItemWidth(210f);
        ImGui.InputText("Node ID", ref _dialogueNodeIdDraft, 128);
        ImGui.SameLine();
        if (ImGui.Button("Rename node"))
        {
            var newId = _dialogueNodeIdDraft.Trim();
            if (string.IsNullOrWhiteSpace(newId)) _rpgPlacementStatus = "A dialogue node needs an ID.";
            else if (newId != node.Id && tree.Nodes.Any(item => item.Id == newId))
                _rpgPlacementStatus = $"Node ID '{newId}' is already in this conversation.";
            else
            {
                UpdateDialogueNode(node with { Id = newId });
                _dialogueNodeIdDraft = newId;
                _dialogueNodeIdDraftTarget = newId;
                _rpgPlacementStatus = $"Renamed node to '{newId}' and updated incoming choice links.";
            }
        }
        var speaker = node.Speaker;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Speaker name", ref speaker, 256))
            UpdateDialogueNode(node with { Speaker = speaker });
        node = GetSelectedDialogueNode() ?? node;
        var actorId = node.SpeakerActorId?.Value ?? string.Empty;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("Speaker actor ID", "Optional registered actor ID", ref actorId, 256))
            UpdateDialogueNode(node with
            {
                SpeakerActorId = string.IsNullOrWhiteSpace(actorId)
                    ? null
                    : new ContentId<ActorContentKind>(actorId.Trim())
            });
        node = GetSelectedDialogueNode() ?? node;
        var text = node.Text;
        if (ImGui.InputTextMultiline("Dialogue text", ref text, 4096, new NumericsVector2(-1f, 56f)))
            UpdateDialogueNode(node with { Text = text });

        var canMoveUp = nodeIndex > 0;
        if (!canMoveUp) ImGui.BeginDisabled();
        if (ImGui.Button("Move to conversation start")) MoveDialogueNodeToStart(nodeIndex);
        if (!canMoveUp) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Delete node")) DeleteDialogueNode(node.Id);

        node = GetSelectedDialogueNode() ?? node;
        ImGui.Separator();
        ImGui.Text("Player choices");
        ImGui.BeginChild("Dialogue choices", new NumericsVector2(0f, 72f), ImGuiChildFlags.Borders);
        for (var index = 0; index < node.Options.Count; index++)
        {
            var option = node.Options[index];
            if (ImGui.Selectable($"{option.Label}##dialogue-option-{node.Id}-{index}",
                _selectedDialogueOptionIndex == index))
            {
                _selectedDialogueOptionIndex = index;
                _selectedDialogueConditionIndex = null;
                _selectedDialogueEffectName = null;
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Add choice")) AddDialogueOption(node);

        node = GetSelectedDialogueNode() ?? node;
        if (_selectedDialogueOptionIndex is not { } optionIndex
            || (uint)optionIndex >= (uint)node.Options.Count)
        {
            ImGui.TextDisabled("Select a choice to edit its destination and rules.");
            return;
        }

        DrawDialogueOptionEditor(tree, node, optionIndex);
    }

    private void DrawDialogueOptionEditor(DialogueTree tree, DialogueNode node, int optionIndex)
    {
        var option = node.Options[optionIndex];
        ImGui.Separator();
        ImGui.Text($"Choice {optionIndex + 1}");
        var optionId = option.Id;
        if (ImGui.InputText("Choice ID", ref optionId, 128))
            UpdateDialogueOption(optionIndex, current => current with { Id = optionId.Trim() });
        option = GetSelectedDialogueOption() ?? option;
        var label = option.Label;
        if (ImGui.InputText("Choice text", ref label, 512))
            UpdateDialogueOption(optionIndex, current => current with { Label = label });
        option = GetSelectedDialogueOption() ?? option;

        var nextLabel = option.Next is null ? "End conversation" : option.Next;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("Next node", nextLabel))
        {
            if (ImGui.Selectable("End conversation", option.Next is null))
                UpdateDialogueOption(optionIndex, current => current with { Next = null });
            foreach (var target in tree.Nodes)
            {
                var selected = option.Next == target.Id;
                if (ImGui.Selectable(target.Id, selected))
                    UpdateDialogueOption(optionIndex, current => current with { Next = target.Id });
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete choice")) DeleteDialogueOption(optionIndex);

        option = GetSelectedDialogueOption() ?? option;
        DrawFlagConditions(optionIndex, option);
        option = GetSelectedDialogueOption() ?? option;
        DrawStatRequirements(optionIndex, option);
        option = GetSelectedDialogueOption() ?? option;
        DrawFactionRequirements(optionIndex, option);
        option = GetSelectedDialogueOption() ?? option;
        DrawDialogueEffects(optionIndex, option);
    }

    private void DrawFlagConditions(int optionIndex, DialogueOption option)
    {
        ImGui.Separator();
        ImGui.Text("Flag conditions (all must match)");
        ImGui.BeginChild("Dialogue flag conditions", new NumericsVector2(0f, 56f), ImGuiChildFlags.Borders);
        for (var index = 0; index < option.Requires.Count; index++)
        {
            var listedCondition = option.Requires[index];
            var summary = ConditionSummary(listedCondition);
            if (ImGui.Selectable($"{summary}##condition-{index}", _selectedDialogueConditionIndex == index))
                _selectedDialogueConditionIndex = index;
        }
        ImGui.EndChild();
        if (ImGui.Button("Add flag condition"))
        {
            var requirements = option.Requires.Append(new FlagCondition { Flag = "flag_name" }).ToArray();
            UpdateDialogueOption(optionIndex, current => current with { Requires = requirements });
            _selectedDialogueConditionIndex = requirements.Length - 1;
        }
        var conditionIndex = _selectedDialogueConditionIndex;
        option = GetSelectedDialogueOption() ?? option;
        if (conditionIndex is not { } selected || (uint)selected >= (uint)option.Requires.Count) return;
        var condition = option.Requires[selected];
        if (ImGui.Button("Delete selected condition"))
        {
            UpdateDialogueOption(optionIndex, current => current with
            {
                Requires = current.Requires.Where((_, index) => index != selected).ToArray()
            });
            _selectedDialogueConditionIndex = null;
            return;
        }

        var flag = condition.Flag;
        if (ImGui.InputText("Required flag", ref flag, 256))
            UpdateDialogueCondition(optionIndex, selected, current => current with { Flag = flag.Trim() });
        condition = GetSelectedDialogueOption()!.Requires[selected];
        var hasBool = condition.Bool.HasValue;
        if (ImGui.Checkbox("Check boolean value", ref hasBool))
            UpdateDialogueCondition(optionIndex, selected, current => current with { Bool = hasBool ? current.Bool ?? true : null });
        condition = GetSelectedDialogueOption()!.Requires[selected];
        if (condition.Bool is { } boolValue)
        {
            if (ImGui.Checkbox("Must be true", ref boolValue))
                UpdateDialogueCondition(optionIndex, selected, current => current with { Bool = boolValue });
        }

        condition = GetSelectedDialogueOption()!.Requires[selected];
        var hasMinimum = condition.AtLeast.HasValue;
        if (ImGui.Checkbox("Minimum numeric value", ref hasMinimum))
            UpdateDialogueCondition(optionIndex, selected, current => current with { AtLeast = hasMinimum ? current.AtLeast ?? 0d : null });
        condition = GetSelectedDialogueOption()!.Requires[selected];
        if (condition.AtLeast is { } minimum)
        {
            var value = (float)minimum;
            if (ImGui.InputFloat("At least", ref value, 0f, 0f, "%.3f"))
                UpdateDialogueCondition(optionIndex, selected, current => current with { AtLeast = value });
        }

        condition = GetSelectedDialogueOption()!.Requires[selected];
        var hasMaximum = condition.AtMost.HasValue;
        if (ImGui.Checkbox("Maximum numeric value", ref hasMaximum))
            UpdateDialogueCondition(optionIndex, selected, current => current with { AtMost = hasMaximum ? current.AtMost ?? 0d : null });
        condition = GetSelectedDialogueOption()!.Requires[selected];
        if (condition.AtMost is { } maximum)
        {
            var value = (float)maximum;
            if (ImGui.InputFloat("At most", ref value, 0f, 0f, "%.3f"))
                UpdateDialogueCondition(optionIndex, selected, current => current with { AtMost = value });
        }

        condition = GetSelectedDialogueOption()!.Requires[selected];
        var hasText = condition.Text is not null;
        if (ImGui.Checkbox("Check text value", ref hasText))
            UpdateDialogueCondition(optionIndex, selected, current => current with { Text = hasText ? current.Text ?? string.Empty : null });
        condition = GetSelectedDialogueOption()!.Requires[selected];
        if (condition.Text is { } requiredText)
        {
            var value = requiredText;
            if (ImGui.InputText("Text must equal", ref value, 256))
                UpdateDialogueCondition(optionIndex, selected, current => current with { Text = value });
        }
    }

    private void DrawStatRequirements(int optionIndex, DialogueOption option)
    {
        ImGui.Separator();
        ImGui.Text("Stat requirements");
        for (var index = 0; index < option.RequiresStats.Count; index++)
        {
            var requirement = option.RequiresStats[index];
            ImGui.BulletText($"{requirement.Attribute} ≥ {requirement.MinimumValue:0.##}");
        }
        if (ImGui.Button("Add stat requirement"))
            UpdateDialogueOption(optionIndex, current => current with
            {
                RequiresStats = current.RequiresStats.Append(new DialogueStatRequirement(ActorAttribute.Personality, 0)).ToArray()
            });
        for (var index = 0; index < option.RequiresStats.Count; index++)
        {
            var requirement = option.RequiresStats[index];
            ImGui.PushID($"stat-{index}");
            var attribute = requirement.Attribute;
            if (DrawEnumCombo("Attribute", ref attribute))
                UpdateDialogueStatRequirement(optionIndex, index, current => current with { Attribute = attribute });
            requirement = GetSelectedDialogueOption()!.RequiresStats[index];
            var minimum = (float)requirement.MinimumValue;
            if (ImGui.InputFloat("Minimum", ref minimum, 0f, 0f, "%.2f"))
                UpdateDialogueStatRequirement(optionIndex, index, current => current with { MinimumValue = minimum });
            if (ImGui.Button("Remove stat requirement"))
                UpdateDialogueOption(optionIndex, current => current with
                {
                    RequiresStats = current.RequiresStats.Where((_, itemIndex) => itemIndex != index).ToArray()
                });
            ImGui.PopID();
        }
    }

    private void DrawFactionRequirements(int optionIndex, DialogueOption option)
    {
        ImGui.Separator();
        ImGui.Text("Faction requirements");
        if (ImGui.InputText("New faction ID", ref _newFactionRequirementId, 256)
            && string.IsNullOrWhiteSpace(_newFactionRequirementId))
            _newFactionRequirementId = "factions.example";
        if (ImGui.Button("Add faction requirement"))
        {
            var requirement = new DialogueFactionRequirement(
                new ContentId<FactionContentKind>(_newFactionRequirementId.Trim()));
            UpdateDialogueOption(optionIndex, current => current with
            {
                RequiresFactions = current.RequiresFactions.Append(requirement).ToArray()
            });
        }
        for (var index = 0; index < option.RequiresFactions.Count; index++)
        {
            var requirement = option.RequiresFactions[index];
            ImGui.PushID($"faction-{index}");
            var factionId = requirement.FactionId.Value;
            if (ImGui.InputText("Faction ID", ref factionId, 256))
                UpdateDialogueFactionRequirement(optionIndex, index, current => current with
                {
                    FactionId = new ContentId<FactionContentKind>(string.IsNullOrWhiteSpace(factionId) ? "invalid" : factionId.Trim())
                });
            requirement = GetSelectedDialogueOption()!.RequiresFactions[index];
            var minimumReputation = requirement.MinimumReputation;
            if (ImGui.InputInt("Minimum reputation", ref minimumReputation))
                UpdateDialogueFactionRequirement(optionIndex, index, current => current with { MinimumReputation = minimumReputation });
            requirement = GetSelectedDialogueOption()!.RequiresFactions[index];
            var member = requirement.RequiresMembership;
            if (ImGui.Checkbox("Requires membership", ref member))
                UpdateDialogueFactionRequirement(optionIndex, index, current => current with { RequiresMembership = member });
            if (ImGui.Button("Remove faction requirement"))
                UpdateDialogueOption(optionIndex, current => current with
                {
                    RequiresFactions = current.RequiresFactions.Where((_, itemIndex) => itemIndex != index).ToArray()
                });
            ImGui.PopID();
        }
    }

    private void DrawDialogueEffects(int optionIndex, DialogueOption option)
    {
        ImGui.Separator();
        ImGui.Text("Effects written when this choice is picked");
        var effectNames = option.Sets.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (effectNames.Length > 0)
        {
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("Effect flag", _selectedDialogueEffectName ?? effectNames[0]))
            {
                foreach (var name in effectNames)
                {
                    if (ImGui.Selectable(name, _selectedDialogueEffectName == name))
                        _selectedDialogueEffectName = name;
                }
                ImGui.EndCombo();
            }
            if (_selectedDialogueEffectName is null || !option.Sets.ContainsKey(_selectedDialogueEffectName))
                _selectedDialogueEffectName = effectNames[0];
        }
        if (ImGui.Button("Add effect"))
        {
            var name = UniqueId("effect", option.Sets.Keys);
            var effects = option.Sets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            effects[name] = FlagValue.From(false);
            UpdateDialogueOption(optionIndex, current => current with { Sets = effects });
            _selectedDialogueEffectName = name;
        }

        option = GetSelectedDialogueOption() ?? option;
        if (_selectedDialogueEffectName is not { } effectName || !option.Sets.TryGetValue(effectName, out var effect)) return;
        var key = effectName;
        if (ImGui.InputText("Effect flag name", ref key, 256)) RenameDialogueEffect(optionIndex, effectName, key.Trim(), effect);
        option = GetSelectedDialogueOption() ?? option;
        if (_selectedDialogueEffectName is not { } currentName || !option.Sets.TryGetValue(currentName, out effect)) return;

        var kind = effect.Kind;
        if (DrawEnumCombo("Value type", ref kind))
        {
            var converted = kind switch
            {
                FlagKind.Boolean => FlagValue.From(effect.AsBool()),
                FlagKind.Number => FlagValue.From(effect.Kind == FlagKind.Number ? effect.AsNumber() : 0d),
                _ => FlagValue.From(effect.ToString())
            };
            UpdateDialogueEffect(optionIndex, currentName, converted);
        }
        effect = GetSelectedDialogueOption()!.Sets[currentName];
        if (effect.Kind == FlagKind.Boolean)
        {
            var value = effect.AsBool();
            if (ImGui.Checkbox("Set value", ref value)) UpdateDialogueEffect(optionIndex, currentName, FlagValue.From(value));
        }
        else if (effect.Kind == FlagKind.Number)
        {
            var value = (float)effect.AsNumber();
            if (ImGui.InputFloat("Set number", ref value, 0f, 0f, "%.3f"))
                UpdateDialogueEffect(optionIndex, currentName, FlagValue.From(value));
        }
        else
        {
            var value = effect.AsText();
            if (ImGui.InputText("Set text", ref value, 512))
                UpdateDialogueEffect(optionIndex, currentName, FlagValue.From(value));
        }
        if (ImGui.Button("Delete effect"))
        {
            UpdateDialogueOption(optionIndex, current =>
            {
            var effects = current.Sets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                effects.Remove(currentName);
                return current with { Sets = effects };
            });
            _selectedDialogueEffectName = null;
        }
    }

    private DialogueTree? GetSelectedDialogue() => _rpgContent?.Dialogues.FirstOrDefault(
        tree => tree.Id.Value == _selectedDialogueId);

    private DialogueNode? GetSelectedDialogueNode() => GetSelectedDialogue()?.Nodes.FirstOrDefault(
        node => node.Id == _selectedDialogueNodeId);

    private DialogueOption? GetSelectedDialogueOption()
    {
        var node = GetSelectedDialogueNode();
        return _selectedDialogueOptionIndex is { } index && node is not null
            && (uint)index < (uint)node.Options.Count ? node.Options[index] : null;
    }

    private void AddDialogue()
    {
        if (_rpgContent is null || string.IsNullOrWhiteSpace(_newDialogueId)) return;
        try
        {
            var id = new ContentId<DialogueContentKind>(_newDialogueId.Trim());
            if (_rpgContent.Dialogues.Any(tree => tree.Id == id))
            {
                _rpgPlacementStatus = $"Dialogue ID '{id.Value}' is already in use.";
                return;
            }
            var tree = new DialogueTree
            {
                Id = id,
                Nodes = new List<DialogueNode> { new() { Id = "start", Speaker = "Speaker", Text = "New dialogue." } }
            };
            ReplaceDialogues(_rpgContent.Dialogues.Append(tree).ToArray());
            _selectedDialogueId = id.Value;
            _selectedDialogueNodeId = "start";
            _newDialogueId = UniqueId("dialogue.new", _rpgContent.Dialogues.Select(item => item.Id.Value));
            _rpgPlacementStatus = $"Added conversation '{id.Value}'.";
        }
        catch (Exception exception) { _rpgPlacementStatus = $"Could not add dialogue: {exception.Message}"; }
    }

    private void AddDialogueNode(DialogueTree tree)
    {
        var id = UniqueId("node", tree.Nodes.Select(node => node.Id));
        var updated = CloneDialogue(tree);
        updated.Nodes.Add(new DialogueNode { Id = id, Speaker = "Speaker", Text = "New dialogue." });
        ReplaceDialogue(updated);
                _selectedDialogueNodeId = id;
        _dialogueNodeIdDraftTarget = id;
        _dialogueNodeIdDraft = id;
        _selectedDialogueOptionIndex = null;
    }

    private void AddDialogueOption(DialogueNode node)
    {
        var id = UniqueId("choice", node.Options.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id));
        var options = node.Options.Append(new DialogueOption { Id = id, Label = "New choice" }).ToArray();
        UpdateDialogueNode(node with { Options = options });
        _selectedDialogueOptionIndex = options.Length - 1;
    }

    private void UpdateDialogueNode(DialogueNode updated)
    {
        var tree = GetSelectedDialogue();
        if (tree is null) return;
        var previousId = _selectedDialogueNodeId;
        var clone = CloneDialogue(tree);
        var index = clone.Nodes.FindIndex(node => node.Id == previousId);
        if (index < 0) return;
        if (!string.Equals(updated.Id, previousId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(updated.Id) || clone.Nodes.Any(node => node.Id == updated.Id)) return;
            clone.Nodes = clone.Nodes.Select(node => node with
            {
                Options = node.Options.Select(option => option.Next == previousId
                    ? option with { Next = updated.Id }
                    : option).ToArray()
            }).ToList();
            _selectedDialogueNodeId = updated.Id;
        }
        clone.Nodes[index] = updated;
        ReplaceDialogue(clone);
    }

    private void UpdateDialogueOption(int optionIndex, Func<DialogueOption, DialogueOption> update)
    {
        var node = GetSelectedDialogueNode();
        if (node is null || (uint)optionIndex >= (uint)node.Options.Count) return;
        var options = node.Options.ToArray();
        options[optionIndex] = update(options[optionIndex]);
        UpdateDialogueNode(node with { Options = options });
    }

    private void UpdateDialogueCondition(int optionIndex, int conditionIndex, Func<FlagCondition, FlagCondition> update) =>
        UpdateDialogueOption(optionIndex, option =>
        {
            var requirements = option.Requires.ToArray();
            requirements[conditionIndex] = update(requirements[conditionIndex]);
            return option with { Requires = requirements };
        });

    private void UpdateDialogueStatRequirement(int optionIndex, int requirementIndex,
        Func<DialogueStatRequirement, DialogueStatRequirement> update) =>
        UpdateDialogueOption(optionIndex, option =>
        {
            var requirements = option.RequiresStats.ToArray();
            requirements[requirementIndex] = update(requirements[requirementIndex]);
            return option with { RequiresStats = requirements };
        });

    private void UpdateDialogueFactionRequirement(int optionIndex, int requirementIndex,
        Func<DialogueFactionRequirement, DialogueFactionRequirement> update) =>
        UpdateDialogueOption(optionIndex, option =>
        {
            var requirements = option.RequiresFactions.ToArray();
            requirements[requirementIndex] = update(requirements[requirementIndex]);
            return option with { RequiresFactions = requirements };
        });

    private void UpdateDialogueEffect(int optionIndex, string name, FlagValue value) =>
        UpdateDialogueOption(optionIndex, option =>
        {
            var effects = option.Sets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            effects[name] = value;
            return option with { Sets = effects };
        });

    private void RenameDialogueEffect(int optionIndex, string oldName, string newName, FlagValue value)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        if (!string.Equals(oldName, newName, StringComparison.Ordinal)
            && GetSelectedDialogueOption()?.Sets.ContainsKey(newName) == true)
        {
            _rpgPlacementStatus = $"Effect flag '{newName}' already exists on this choice.";
            return;
        }
        UpdateDialogueOption(optionIndex, option =>
        {
            var effects = option.Sets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            effects.Remove(oldName);
            effects[newName] = value;
            return option with { Sets = effects };
        });
        _selectedDialogueEffectName = newName;
    }

    private void DeleteDialogueOption(int optionIndex)
    {
        var node = GetSelectedDialogueNode();
        if (node is null || (uint)optionIndex >= (uint)node.Options.Count) return;
        UpdateDialogueNode(node with { Options = node.Options.Where((_, index) => index != optionIndex).ToArray() });
        _selectedDialogueOptionIndex = null;
        _selectedDialogueConditionIndex = null;
        _selectedDialogueEffectName = null;
    }

    private void DeleteDialogueNode(string id)
    {
        var tree = GetSelectedDialogue();
        if (tree is null) return;
        var clone = CloneDialogue(tree);
        clone.Nodes = clone.Nodes.Where(node => node.Id != id).Select(node => node with
        {
            Options = node.Options.Select(option => option.Next == id ? option with { Next = null } : option).ToArray()
        }).ToList();
        ReplaceDialogue(clone);
        _selectedDialogueNodeId = clone.Nodes.FirstOrDefault()?.Id;
        _dialogueNodeIdDraftTarget = _selectedDialogueNodeId;
        _dialogueNodeIdDraft = _selectedDialogueNodeId ?? string.Empty;
        _selectedDialogueOptionIndex = null;
        _selectedDialogueConditionIndex = null;
        _selectedDialogueEffectName = null;
        _rpgPlacementStatus = "Deleted node; choices that pointed to it now end the conversation.";
    }

    private void MoveDialogueNodeToStart(int index)
    {
        var tree = GetSelectedDialogue();
        if (tree is null || (uint)index >= (uint)tree.Nodes.Count || index == 0) return;
        var nodes = tree.Nodes.ToList();
        var node = nodes[index];
        nodes.RemoveAt(index);
        nodes.Insert(0, node);
        ReplaceDialogue(new DialogueTree { Id = tree.Id, Nodes = nodes });
    }

    private void ReplaceDialogue(DialogueTree replacement)
    {
        var content = _rpgContent;
        if (content is null) return;
        ReplaceDialogues(content.Dialogues.Select(tree => tree.Id == replacement.Id ? replacement : tree).ToArray());
    }

    private void ReplaceDialogues(IReadOnlyList<DialogueTree> dialogues)
    {
        var content = _rpgContent;
        if (content is null) return;
        _rpgContent = new RpgContentSet
        {
            Actors = content.Actors,
            Items = content.Items,
            Factions = content.Factions,
            Spells = content.Spells,
            SkillProgression = content.SkillProgression,
            Dialogues = dialogues,
            Quests = content.Quests
        };
    }

    private static DialogueTree CloneDialogue(DialogueTree tree) =>
        new() { Id = tree.Id, Nodes = tree.Nodes.Select(node => node with
        {
            Options = node.Options.Select(option => option with
            {
                Requires = option.Requires.ToArray(),
                RequiresStats = option.RequiresStats.ToArray(),
                RequiresFactions = option.RequiresFactions.ToArray(),
                Sets = option.Sets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToArray()
        }).ToList() };

    private static IReadOnlyList<string> GetDialogueDiagnostics(RpgContentSet content)
    {
        try { return content.Validate().Select(item => item.ToString()).ToArray(); }
        catch (Exception exception) { return new[] { $"Content validation failed: {exception.Message}" }; }
    }

    private void SaveDialogueContent()
    {
        if (_rpgContent is null) return;
        try
        {
            RpgContentJson.SaveAtomic(_rpgContentPath, _rpgContent);
            _rpgPlacementStatus = $"Saved validated content to {_rpgContentPath}.";
        }
        catch (Exception exception) { _rpgPlacementStatus = $"Could not save content: {exception.Message}"; }
    }

    private static string UniqueId(string prefix, IEnumerable<string> used)
    {
        var ids = new HashSet<string>(used, StringComparer.Ordinal);
        if (!ids.Contains(prefix)) return prefix;
        for (var index = 2; ; index++)
        {
            var candidate = $"{prefix}.{index}";
            if (!ids.Contains(candidate)) return candidate;
        }
    }

    private static string ConditionSummary(FlagCondition condition)
    {
        var constraints = new List<string>();
        if (condition.Bool is { } boolean) constraints.Add(boolean ? "true" : "false");
        if (condition.AtLeast is { } minimum) constraints.Add($"≥{minimum:0.##}");
        if (condition.AtMost is { } maximum) constraints.Add($"≤{maximum:0.##}");
        if (condition.Text is { } text) constraints.Add($"=\"{text}\"");
        return constraints.Count == 0 ? $"{condition.Flag} exists" : $"{condition.Flag} {string.Join(" ", constraints)}";
    }

    private static string ShortText(string text, int maximumLength) => string.IsNullOrWhiteSpace(text)
        ? "(empty)"
        : text.Length <= maximumLength ? text : text[..maximumLength] + "…";

    private static bool DrawEnumCombo<TEnum>(string label, ref TEnum selected)
        where TEnum : struct, Enum
    {
        var changed = false;
        if (ImGui.BeginCombo(label, selected.ToString()))
        {
            foreach (var option in Enum.GetValues<TEnum>())
            {
                var isSelected = EqualityComparer<TEnum>.Default.Equals(option, selected);
                if (ImGui.Selectable(option.ToString(), isSelected))
                {
                    selected = option;
                    changed = true;
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
