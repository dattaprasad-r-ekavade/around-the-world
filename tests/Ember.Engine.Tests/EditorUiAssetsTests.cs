using System.IO;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EditorUiAssetsTests
{
    [Fact]
    public void ReadableEditorFontAndLicenseAreCopiedBesideTheHost()
    {
        var fontsDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fonts");

        Assert.True(File.Exists(Path.Combine(fontsDirectory, "SourceSans3-Regular.ttf")));
        Assert.True(File.Exists(Path.Combine(fontsDirectory, "SourceSans3-LICENSE.md")));
    }
}
