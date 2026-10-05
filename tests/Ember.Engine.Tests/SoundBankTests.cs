using System;
using System.IO;
using System.Text;
using Ember.Audio;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SoundBankTests
{
    [Fact]
    public void DumpWritesValidWavFiles()
    {
        var directory = TemporaryDirectory();
        try
        {
            var written = SoundBank.Dump(directory);
            var wavPaths = Directory.GetFiles(directory, "*.wav", SearchOption.TopDirectoryOnly);

            Assert.Equal(48, written);
            Assert.Equal(48, wavPaths.Length);
            foreach (var path in wavPaths)
            {
                var wav = File.ReadAllBytes(path);
                Assert.True(wav.Length > 44);
                Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
                Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
                Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
            }
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedDumpReplacementPreservesPreviousWavAndCleansTemporaryFile()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "swing0.wav");
            var original = Encoding.ASCII.GetBytes("keep the previous audio file");
            File.WriteAllBytes(path, original);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => SoundBank.Dump(directory));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-sound-bank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
