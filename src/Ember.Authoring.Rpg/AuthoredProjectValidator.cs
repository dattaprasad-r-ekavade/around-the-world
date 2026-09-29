using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Rpg;
using Ember.World;

namespace Ember.Authoring;

public sealed record AuthoredProjectDiagnostic(string SourceFile, string SourceRecord, string Message)
{
    public override string ToString() => $"{SourceFile} [{SourceRecord}]: {Message}";
}

public sealed class AuthoredProjectValidationResult
{
    internal AuthoredProjectValidationResult(IReadOnlyList<AuthoredProjectDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<AuthoredProjectDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Validates all world and RPG references in an authored project without changing its files.</summary>
public static class AuthoredProjectValidator
{
    public static AuthoredProjectValidationResult Validate(string worldManifestPath, string rpgContentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpgContentPath);

        var diagnostics = new List<AuthoredProjectDiagnostic>();
        var world = WorldProjectValidator.Validate(worldManifestPath);
        diagnostics.AddRange(world.Diagnostics.Select(issue =>
            new AuthoredProjectDiagnostic(issue.SourceFile, issue.SourceRecord, issue.Message)));

        var fullContentPath = Path.GetFullPath(rpgContentPath);
        RpgContentValidationResult content;
        try
        {
            content = RpgContentJson.LoadForValidation(fullContentPath);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new AuthoredProjectDiagnostic(fullContentPath, "RPG content pack", exception.Message));
            return BuildResult(diagnostics);
        }

        var placements = new List<RpgPlacementReference>();
        foreach (var (cellId, scene) in world.Scenes.OrderBy(pair => pair.Key))
        {
            if (!world.SceneFiles.TryGetValue(cellId, out var sceneFile)) continue;
            foreach (var sceneObject in scene.Objects.OrderBy(item => item.Id))
            {
                if (sceneObject.WorldEntity is not { } placement) continue;
                var kind = placement.Kind switch
                {
                    WorldEntityKind.Actor => RpgPlacementDefinitionKind.Actor,
                    WorldEntityKind.Item => RpgPlacementDefinitionKind.Item,
                    _ => (RpgPlacementDefinitionKind)(-1)
                };
                placements.Add(new RpgPlacementReference(sceneFile,
                    $"cell '{cellId}' object '{sceneObject.Name}' ({sceneObject.Id}) placement",
                    kind, placement.DefinitionId));
            }
        }

        diagnostics.AddRange(RpgProjectValidator.Validate(content, fullContentPath, placements)
            .Select(issue => new AuthoredProjectDiagnostic(issue.SourceFile, issue.SourceRecord, issue.Message)));
        return BuildResult(diagnostics);
    }

    private static AuthoredProjectValidationResult BuildResult(IEnumerable<AuthoredProjectDiagnostic> diagnostics) =>
        new(diagnostics
            .OrderBy(issue => issue.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.SourceRecord, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray());
}
