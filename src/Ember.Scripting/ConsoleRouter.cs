using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Ember.Scripting;

/// <summary>How a line of console output should be read.</summary>
public enum ConsoleTone
{
    /// <summary>The command the player typed, echoed back.</summary>
    Echo,

    /// <summary>What happened.</summary>
    Info,

    /// <summary>What did not happen, and why.</summary>
    Error
}

public readonly record struct ConsoleLine(string Text, ConsoleTone Tone);

/// <summary>
/// The arguments to one command, with the parsing already done.
///
/// Commands ask for what they want by position and say what to do if it is missing, rather
/// than each one re-deciding how to read a number. A console that throws on a typo is a
/// console people stop using.
/// </summary>
public sealed class ConsoleArgs
{
    private readonly IReadOnlyList<string> _values;

    public ConsoleArgs(string name, IReadOnlyList<string> values)
    {
        Name = name;
        _values = values;
    }

    public string Name { get; }
    public int Count => _values.Count;

    public string Text(int index, string fallback = "") =>
        index >= 0 && index < _values.Count ? _values[index] : fallback;

    /// <summary>The rest of the line from this argument on, space-joined.</summary>
    public string Rest(int index) =>
        index >= _values.Count ? string.Empty : string.Join(' ', _values.Skip(index));

    public bool TryNumber(int index, out float value)
    {
        value = 0f;
        return index >= 0 && index < _values.Count
            && float.TryParse(_values[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                out value);
    }

    public float Number(int index, float fallback = 0f) =>
        TryNumber(index, out var value) ? value : fallback;

    public bool TryInteger(int index, out int value)
    {
        value = 0;
        return index >= 0 && index < _values.Count
            && int.TryParse(_values[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out value);
    }

    public int Integer(int index, int fallback = 0) =>
        TryInteger(index, out var value) ? value : fallback;

    /// <summary>
    /// A flag argument: absent means toggle, "on"/"off"/"1"/"0" means set.
    ///
    /// Every switch in a Bethesda console works this way and it is the right shape: typing
    /// <c>god</c> twice should not leave you wondering which way round it is.
    /// </summary>
    public bool? Switch(int index)
    {
        var text = Text(index).ToLowerInvariant();
        return text switch
        {
            "" => null,
            "on" or "1" or "true" or "yes" => true,
            "off" or "0" or "false" or "no" => false,
            _ => null
        };
    }
}

public sealed record ConsoleCommand(
    string Name,
    string Usage,
    string Summary,
    Func<ConsoleArgs, string> Run)
{
    /// <summary>Other names this answers to, so muscle memory from other consoles works.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A developer console: names to actions, and a line of text to one of them.
///
/// It exists so the game can be driven without hands. Verifying a change to the shaft or a
/// room full of enemies meant reading the manifest and reasoning about it, because a capture
/// renders from the spawn point and there was no way to walk anywhere — so several fixes were
/// argued for from JSON rather than looked at. A console that a script can type into makes the
/// game inspectable from outside, which is worth more than the convenience of it in play.
///
/// Engine-free on purpose: the parsing, the registry, the history and the completion are all
/// testable without opening a window, and the client only supplies the verbs.
/// </summary>
public sealed class ConsoleRouter
{
    private readonly Dictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _history = new();

    /// <summary>Lines the player typed, newest last. Bounded so a long session cannot grow forever.</summary>
    public IReadOnlyList<string> History => _history;

    private const int MaxHistory = 64;

    /// <summary>Every registered command, in the order help should list them.</summary>
    public IReadOnlyList<ConsoleCommand> Commands => _commands.Values
        .Distinct()
        .OrderBy(command => command.Name, StringComparer.Ordinal)
        .ToList();

    public void Register(ConsoleCommand command)
    {
        _commands[command.Name] = command;
        foreach (var alias in command.Aliases) _commands[alias] = command;
    }

    public void Register(string name, string usage, string summary, Func<ConsoleArgs, string> run,
        params string[] aliases) =>
        Register(new ConsoleCommand(name, usage, summary, run) { Aliases = aliases });

    public bool Knows(string name) => _commands.ContainsKey(name);

    /// <summary>
    /// Run a line, or several separated by semicolons.
    ///
    /// Semicolons are what make the console scriptable from the command line: one --exec can
    /// walk to the shaft, turn to face it and take a picture.
    /// </summary>
    public IReadOnlyList<ConsoleLine> Execute(string input)
    {
        var output = new List<ConsoleLine>();
        if (string.IsNullOrWhiteSpace(input)) return output;

        Remember(input.Trim());

        foreach (var statement in SplitStatements(input))
        {
            var tokens = Tokenise(statement);
            if (tokens.Count == 0) continue;

            output.Add(new ConsoleLine("> " + statement.Trim(), ConsoleTone.Echo));

            var name = tokens[0];
            if (!_commands.TryGetValue(name, out var command))
            {
                output.Add(new ConsoleLine(Unknown(name), ConsoleTone.Error));
                continue;
            }

            string result;
            try
            {
                result = command.Run(new ConsoleArgs(name, tokens.Skip(1).ToList()));
            }
            catch (Exception exception)
            {
                // A command that throws must not take the game with it. The console is a
                // debugging tool; the most likely time to need it is when something is
                // already broken.
                output.Add(new ConsoleLine($"{name} failed: {exception.Message}", ConsoleTone.Error));
                continue;
            }

            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                output.Add(new ConsoleLine(line.TrimEnd(), ConsoleTone.Info));
        }

        return output;
    }

    /// <summary>
    /// Run one statement and return only what it said.
    ///
    /// No echo, no history, no error decoration -- for commands built out of other commands.
    /// 'assert' needs to read a command's answer rather than show it, and 'watch' re-runs one
    /// every frame, neither of which should fill the log.
    /// </summary>
    public string RunQuiet(string statement)
    {
        var tokens = Tokenise(statement);
        if (tokens.Count == 0) return string.Empty;

        if (!_commands.TryGetValue(tokens[0], out var command)) return Unknown(tokens[0]);

        try
        {
            return command.Run(new ConsoleArgs(tokens[0], tokens.Skip(1).ToList()));
        }
        catch (Exception exception)
        {
            return $"{tokens[0]} failed: {exception.Message}";
        }
    }

    /// <summary>Command names starting with this, for tab completion.</summary>
    public IReadOnlyList<string> Complete(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Commands.Select(c => c.Name).ToList();

        // Only real names, never aliases: completing to an alias teaches the wrong word.
        return Commands
            .Select(command => command.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Every command and what it is for, or the detail of one of them.</summary>
    public string Help(string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (!_commands.TryGetValue(name, out var one)) return Unknown(name);

            var alias = one.Aliases.Count == 0
                ? string.Empty
                : $"\naliases: {string.Join(", ", one.Aliases)}";
            return $"{one.Usage}\n{one.Summary}{alias}";
        }

        var text = new StringBuilder();
        foreach (var command in Commands)
            text.Append(command.Name.PadRight(12)).Append(command.Summary).Append('\n');

        return text.ToString();
    }

    private string Unknown(string name)
    {
        // A near miss is far more likely than a wrong idea, so say the closest thing rather
        // than only that this one does not exist.
        var closest = Commands
            .Select(command => command.Name)
            .OrderBy(candidate => Distance(candidate, name))
            .FirstOrDefault(candidate => Distance(candidate, name) <= 2);

        return closest is null
            ? $"No command '{name}'. Type help."
            : $"No command '{name}'. Did you mean '{closest}'?";
    }

    private void Remember(string line)
    {
        // Repeating the last line adds nothing to walk back through.
        if (_history.Count > 0 && _history[^1] == line) return;

        _history.Add(line);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
    }

    /// <summary>
    /// The statements in a script file: one per line, blank lines and <c>#</c> comments dropped.
    ///
    /// One reader for both callers. The command line reads a script before there is a console,
    /// and the <c>script</c> command reads one while there is; when they each had their own copy
    /// of "trim, skip blanks, skip hashes" a change to the format only ever landed in one.
    /// </summary>
    public static IReadOnlyList<string> ReadScript(IEnumerable<string> lines) => lines
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();

    /// <summary>
    /// The command names in these statements that nothing has registered.
    ///
    /// Checked before a script runs rather than as each statement comes up. A typo halfway
    /// down a script otherwise costs the whole run before it reports, and a scripted gate that
    /// half-ran is worse than one that refused: the asserts that never executed are silent
    /// rather than failed, so the run can still exit zero.
    /// </summary>
    public IReadOnlyList<string> UnknownCommands(IEnumerable<string> statements)
    {
        var unknown = new List<string>();

        foreach (var statement in statements)
        {
            var tokens = Tokenise(statement);
            if (tokens.Count == 0) continue;

            var name = tokens[0];
            if (_commands.ContainsKey(name) || unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            unknown.Add(name);
        }

        return unknown;
    }

    /// <summary>Split on semicolons, respecting quotes.</summary>
    public static IReadOnlyList<string> SplitStatements(string input)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var character in input)
        {
            if (character == '"') quoted = !quoted;

            if (character == ';' && !quoted)
            {
                statements.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        statements.Add(current.ToString());
        return statements.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    /// <summary>Split one statement into a command and its arguments, respecting quotes.</summary>
    public static IReadOnlyList<string> Tokenise(string statement)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var character in statement)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>Levenshtein, bounded — only ever used to suggest one near miss.</summary>
    private static int Distance(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return Math.Max(a.Length, b.Length);

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
