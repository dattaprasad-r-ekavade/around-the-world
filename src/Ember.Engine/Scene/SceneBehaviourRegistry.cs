using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.Scene;

/// <summary>Maps stable behaviour IDs to factories supplied by a game or engine module.</summary>
public sealed class SceneBehaviourRegistry
{
    private readonly Dictionary<string, Func<SceneObject, SceneBehaviour>> _factories = new(StringComparer.Ordinal);

    /// <summary>Registered IDs in a stable order for validation and authoring UI.</summary>
    public IReadOnlyList<string> RegisteredIds => _factories.Keys
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Registers a factory under a stable, case-sensitive ID.</summary>
    public void Register(string behaviourId, Func<SceneObject, SceneBehaviour> factory)
    {
        var id = SceneBehaviourIds.ValidateId(behaviourId, nameof(behaviourId));
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(id, factory))
            throw new InvalidOperationException($"Behaviour ID '{id}' is already registered.");
    }

    /// <summary>Creates one behaviour instance and reports failures with its owning scene object.</summary>
    public SceneBehaviour Create(string behaviourId, SceneObject owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var id = SceneBehaviourIds.ValidateId(behaviourId, nameof(behaviourId));
        if (!_factories.TryGetValue(id, out var factory))
            throw new InvalidOperationException(
                $"Scene object '{owner.Name}' ({owner.Id}) assigns behaviour '{id}', but it is not registered. Register it before starting Play.");

        try
        {
            return factory(owner)
                ?? throw new InvalidOperationException("The registered factory returned no behaviour.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not create behaviour '{id}' for scene object '{owner.Name}' ({owner.Id}): {exception.Message}",
                exception);
        }
    }

}

internal static class SceneBehaviourIds
{
    internal static string ValidateId(string behaviourId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(behaviourId))
            throw new ArgumentException("A stable behaviour ID is required.", parameterName);
        if (!string.Equals(behaviourId, behaviourId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Behaviour IDs cannot start or end with whitespace.", parameterName);
        return behaviourId;
    }

    internal static string[] ValidateAssignments(IEnumerable<string> behaviourIds, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(behaviourIds);
        var assignments = behaviourIds
            .Select(id => ValidateId(id, parameterName))
            .ToArray();
        if (assignments.Distinct(StringComparer.Ordinal).Count() != assignments.Length)
            throw new ArgumentException("Behaviour assignments cannot contain duplicate IDs.", parameterName);
        return assignments;
    }
}
