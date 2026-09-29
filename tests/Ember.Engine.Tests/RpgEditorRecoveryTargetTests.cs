using System;
using System.IO;
using Ember.Editor;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class RpgEditorRecoveryTargetTests
{
    [Fact]
    public void RecoveryReviewOnlyMatchesTheSameWorldAndRpgContentFiles()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "EmberRecoveryTargetTests", Guid.NewGuid().ToString("N"));
        var worldManifest = Path.Combine(projectRoot, "World.json");
        var rpgContent = Path.Combine(projectRoot, "RpgContent.json");

        Assert.True(RpgEditorTool.RecoveryTargetsMatch(worldManifest, rpgContent,
            Path.Combine(projectRoot, ".", "World.json"), rpgContent));
        Assert.False(RpgEditorTool.RecoveryTargetsMatch(worldManifest, rpgContent,
            Path.Combine(projectRoot, "OtherWorld.json"), rpgContent));
        Assert.False(RpgEditorTool.RecoveryTargetsMatch(worldManifest, rpgContent,
            worldManifest, Path.Combine(projectRoot, "OtherRpgContent.json")));
        Assert.False(RpgEditorTool.RecoveryTargetsMatch(worldManifest, rpgContent,
            null, rpgContent));
    }
}
