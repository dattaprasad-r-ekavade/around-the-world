using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ember.Rpg;

public sealed record RpgContentValidationResult(
    RpgContentSet Content,
    IReadOnlyList<ContentDiagnostic> Diagnostics);

public enum RpgPlacementDefinitionKind
{
    Actor,
    Item
}

/// <summary>A scene placement reference without an Ember.Engine dependency.</summary>
public sealed record RpgPlacementReference(
    string SourceFile,
    string SourceRecord,
    RpgPlacementDefinitionKind Kind,
    string DefinitionId);

public sealed record RpgProjectDiagnostic(string SourceFile, string SourceRecord, string Message)
{
    public override string ToString() => $"{SourceFile} [{SourceRecord}]: {Message}";
}

/// <summary>Combines content-pack references with actor/item placements supplied by a project loader.</summary>
public static class RpgProjectValidator
{
    public static IReadOnlyList<RpgProjectDiagnostic> Validate(RpgContentValidationResult content,
        string contentFilePath, IEnumerable<RpgPlacementReference> placements)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFilePath);
        ArgumentNullException.ThrowIfNull(placements);

        var fullContentPath = Path.GetFullPath(contentFilePath);
        var diagnostics = new List<RpgProjectDiagnostic>();
        foreach (var issue in content.Diagnostics)
            diagnostics.Add(new RpgProjectDiagnostic(fullContentPath, issue.SourceRecord, issue.MissingTarget));

        foreach (var placement in placements)
        {
            if (placement is null)
            {
                diagnostics.Add(new RpgProjectDiagnostic(fullContentPath, "scene placement",
                    "placement reference cannot be null"));
                continue;
            }

            var sourceFile = string.IsNullOrWhiteSpace(placement.SourceFile)
                ? fullContentPath
                : Path.GetFullPath(placement.SourceFile);
            var sourceRecord = string.IsNullOrWhiteSpace(placement.SourceRecord)
                ? "scene placement"
                : placement.SourceRecord;
            if (!Enum.IsDefined(placement.Kind))
            {
                diagnostics.Add(new RpgProjectDiagnostic(sourceFile, sourceRecord,
                    $"unknown RPG placement kind '{placement.Kind}'"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(placement.DefinitionId))
            {
                diagnostics.Add(new RpgProjectDiagnostic(sourceFile, sourceRecord,
                    "definition ID is empty"));
                continue;
            }

            var isRegistered = placement.Kind switch
            {
                RpgPlacementDefinitionKind.Actor => content.Content.Actors.Get(
                    new ContentId<ActorContentKind>(placement.DefinitionId)) is not null,
                RpgPlacementDefinitionKind.Item => content.Content.Items.Get(
                    new ContentId<ItemContentKind>(placement.DefinitionId)) is not null,
                _ => false
            };
            if (!isRegistered)
                diagnostics.Add(new RpgProjectDiagnostic(sourceFile, sourceRecord,
                    $"{placement.Kind} definition '{placement.DefinitionId}' is not registered in the content pack"));
        }

        return diagnostics
            .OrderBy(issue => issue.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.SourceRecord, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
    }
}
