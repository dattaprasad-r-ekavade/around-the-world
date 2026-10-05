using System;
using System.IO;
using Ember.Rpg;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SaveStateFileTests
{
    [Fact]
    public void WriteCreatesAndReplacesSaveState()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "rpg-save.json");
            var original = new SaveState { WorldTimeSeconds = 1.5 };
            var replacement = new SaveState { WorldTimeSeconds = 9.25 };

            original.Write(path);
            replacement.Write(path);

            Assert.Equal(replacement.ToJson(), SaveState.Read(path).ToJson());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedReplacementPreservesPreviousSaveAndCleansTemporaryFile()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "rpg-save.json");
            var original = new SaveState { WorldTimeSeconds = 1.5 };
            var replacement = new SaveState { WorldTimeSeconds = 9.25 };
            original.Write(path);
            var originalJson = File.ReadAllText(path);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => replacement.Write(path));

            Assert.Equal(originalJson, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-rpg-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
