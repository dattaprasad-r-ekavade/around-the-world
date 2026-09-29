using System;
using System.IO;
using System.Linq;
using Ember.Editor;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EditorToolExtensionLoadingTests
{
    [Fact]
    public void LoadsOptionalRpgAuthoringToolFromModulesDirectory()
    {
        var moduleBuildDirectory = Path.GetDirectoryName(typeof(RpgEditorTool).Assembly.Location)!;
        var modulesDirectory = Path.Combine(AppContext.BaseDirectory, "Modules");
        Directory.CreateDirectory(modulesDirectory);
        foreach (var file in Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Editor.Rpg.*")
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Editor.Rpg.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Authoring.Rpg.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Rpg.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Engine.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Authoring.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "Ember.Editor.dll"))
                     .Concat(Directory.EnumerateFiles(moduleBuildDirectory, "ImGui.NET.dll"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            File.Copy(file, Path.Combine(modulesDirectory, Path.GetFileName(file)), overwrite: true);

        var result = EditorToolExtensionLoader.Load(AppContext.BaseDirectory);

        Assert.Empty(result.Errors);
        var extension = Assert.Single(result.Extensions);
        Assert.Equal("ember.rpg.authoring", extension.Id);
        Assert.Equal("RPG authoring", extension.DisplayName);
    }
}
