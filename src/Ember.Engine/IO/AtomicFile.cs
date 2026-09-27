using System;
using System.IO;

namespace Ember.IO;

/// <summary>Writes a file completely before atomically replacing its previous version.</summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes to a unique sibling temporary file, flushes it to disk, then moves it into place.
    /// If writing or replacement fails, the previous destination remains and the temporary file
    /// is removed. When <paramref name="overwrite"/> is false, an existing destination is kept.
    /// </summary>
    public static void Write(string path, Action<Stream> write, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Atomic file path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (!overwrite && File.Exists(fullPath))
            throw new IOException($"Destination already exists: '{fullPath}'.");
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                if (!overwrite)
                    throw new IOException($"Destination already exists: '{fullPath}'.");
                File.Replace(temporaryPath, fullPath, null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
