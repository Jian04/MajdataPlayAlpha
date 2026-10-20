using System;
using System.IO;
using System.Security.Cryptography;

namespace MajdataCore
{
    public static class FontFiles
    {
        public static bool IsFontFile(string path)
        {
            var extension = Path.GetExtension(path) ?? "";
            return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
        }

        public static string DirectoryForApplication(string applicationDirectory)
        {
            var directory = new DirectoryInfo(applicationDirectory);
            if (directory.Parent?.Name.Equals("App", StringComparison.OrdinalIgnoreCase) == true)
                directory = directory.Parent.Parent;
            return Path.Combine(directory.FullName, "Fonts");
        }

        public static string Resolve(string requested, string fontDirectory, string chartDirectory = null)
        {
            var path = requested.Trim().Trim('"');
            if (!IsFontFile(path)) throw new ArgumentException("字体文件必须为 TTF、OTF 或 TTC：" + requested);
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            if (!string.IsNullOrEmpty(chartDirectory))
            {
                var local = Path.GetFullPath(Path.Combine(chartDirectory, path));
                if (File.Exists(local)) return local;
            }
            if (path.Replace('\\', '/').StartsWith("Fonts/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(6);
            return Path.GetFullPath(Path.Combine(fontDirectory, path));
        }

        public static string CreateLoadCopy(string path)
        {
            // Font renderers retain open file handles. Keep the user-managed file replaceable.
            var bytes = File.ReadAllBytes(path);
            using var hash = SHA256.Create();
            var name = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "") + Path.GetExtension(path);
            var directory = Path.Combine(Path.GetTempPath(), "MajdataAlpha", "Fonts");
            Directory.CreateDirectory(directory);
            var copy = Path.Combine(directory, name);
            if (!File.Exists(copy)) File.WriteAllBytes(copy, bytes);
            return copy;
        }

    }
}
