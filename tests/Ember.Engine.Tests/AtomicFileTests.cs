using System;
using System.IO;
using System.Text;
using Ember.IO;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void InterruptedWritePreservesPreviousFileAndRemovesTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-atomic-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "save.json");
        const string previous = "previous-valid-save";
        File.WriteAllText(path, previous);

        try
        {
            var exception = Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("partial replacement"));
                throw new IOException("simulated interrupted write");
            }));

            Assert.Contains("simulated interrupted write", exception.Message, StringComparison.Ordinal);
            Assert.Equal(previous, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
