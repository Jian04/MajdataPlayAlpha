using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay.Utils
{
    // A song is identified by its chart and audio, not by the number of media subfolders.
    internal static class ChartArchiveLayout
    {
        static readonly HashSet<string> AudioNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "track.mp3", "track.ogg", "track.opus", "track.aac", "track.wav"
        };

        internal static IEnumerable<(DirectoryInfo Source, string RelativePath)> FindSongs(string rootPath, string packageName)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(rootPath));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                var hasChart = false;
                var hasAudio = false;
                foreach (var file in directory.EnumerateFiles())
                {
                    hasChart |= file.Name.Equals("maidata.txt", StringComparison.OrdinalIgnoreCase);
                    hasAudio |= AudioNames.Contains(file.Name);
                }
                if (hasChart && hasAudio)
                {
                    var relative = Path.GetRelativePath(rootPath, directory.FullName);
                    var separator = relative.IndexOf(Path.DirectorySeparatorChar);
                    var category = separator < 0 ? "Import" : relative.Substring(0, separator);
                    var name = relative == "." ? packageName : directory.Name;
                    yield return (directory, Path.Combine(category, name));
                    continue;
                }
                foreach (var child in directory.EnumerateDirectories())
                    if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                        !child.Name.StartsWith(".", StringComparison.Ordinal))
                        pending.Push(child);
            }
        }
    }
}
