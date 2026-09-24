using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>
/// A conversation as data: nodes by id, each with a speaker, text and options. Content, not
/// state — load it from JSON and share it. The player's place in it lives on
/// <see cref="SaveState.Dialogue"/>; what saying something did to the world lives in the
/// FlagStore.
/// </summary>
public sealed class DialogueTree
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { FlagValueJson.Instance, new JsonStringEnumConverter() }
    };

    public ContentId<DialogueContentKind> Id { get; set; }

    public List<DialogueNode> Nodes { get; set; } = new();

    public static DialogueTree FromJson(string json) =>
        JsonSerializer.Deserialize<DialogueTree>(json, Json)
        ?? throw new JsonException("The dialogue tree was empty.");

    public static DialogueTree Load(string path) => FromJson(File.ReadAllText(path));

    public DialogueNode? Node(string id) => Nodes.Find(node => node.Id == id);

    /// <summary>
    /// The options on this node the given flags allow, in order. An unknown node has no
    /// options rather than an exception: a conversation pointing at a node that was renamed
    /// should end quietly, not take the game with it.
    /// </summary>
    public IReadOnlyList<DialogueOption> Available(string nodeId, FlagStore flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return Available(nodeId, new DialogueContext(flags, new ActorStats()));
    }

    public IReadOnlyList<DialogueOption> Available(string nodeId, DialogueContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var node = Node(nodeId);
        if (node is null) return Array.Empty<DialogueOption>();

        var open = new List<DialogueOption>();
        foreach (var option in node.Options)
            if (context.Meets(option)) open.Add(option);
        return open;
    }

    /// <summary>
    /// Take an option: write its flags, then move the progress to the option's next node —
    /// or close the conversation when there is none. Returns the node now showing, or null.
    /// </summary>
    public string? Pick(DialogueProgress progress, FlagStore flags, DialogueOption option)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return Pick(progress, new DialogueContext(flags, new ActorStats()), option);
    }

    public string? Pick(DialogueProgress progress, DialogueContext context, DialogueOption option)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(option);
        if (progress.Tree != Id || progress.Node is null || Node(progress.Node) is not { } node)
            return progress.Node;

        var optionIndex = -1;
        for (var i = 0; i < node.Options.Count; i++)
            if (ReferenceEquals(node.Options[i], option) || (!string.IsNullOrEmpty(option.Id)
                && node.Options[i].Id == option.Id))
            {
                optionIndex = i;
                break;
            }
        if (optionIndex < 0 || !context.Meets(node.Options[optionIndex])) return progress.Node;

        var selected = node.Options[optionIndex];
        var choiceId = string.IsNullOrEmpty(selected.Id)
            ? $"{Id.Value}:{node.Id}:{optionIndex}"
            : $"{Id.Value}:{node.Id}:{selected.Id}";
        if (progress.TakenChoiceIds.Contains(choiceId)) return progress.Node;

        progress.TakenChoiceIds.Add(choiceId);
        foreach (var (name, value) in selected.Sets) context.Flags.Set(name, value);

        progress.Node = selected.Next;
        if (selected.Next is null) progress.Tree = null;
        return progress.Node;
    }
}
