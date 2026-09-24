using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
        Converters = { FlagValueJson.Instance }
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
        var node = Node(nodeId);
        if (node is null) return Array.Empty<DialogueOption>();

        var open = new List<DialogueOption>();
        foreach (var option in node.Options)
        {
            var allowed = true;
            foreach (var condition in option.Requires)
                if (!condition.Matches(flags))
                {
                    allowed = false;
                    break;
                }
            if (allowed) open.Add(option);
        }
        return open;
    }

    /// <summary>
    /// Take an option: write its flags, then move the progress to the option's next node —
    /// or close the conversation when there is none. Returns the node now showing, or null.
    /// </summary>
    public string? Pick(DialogueProgress progress, FlagStore flags, DialogueOption option)
    {
        foreach (var (name, value) in option.Sets) flags.Set(name, value);

        progress.Node = option.Next;
        if (option.Next is null) progress.Tree = null;
        return progress.Node;
    }
}
