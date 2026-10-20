using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MajdataPlay.Editor
{
    // Keep documentation outside the distributable, including StreamingAssets.
    internal sealed class AlphaReleasePackaging : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                report.summary.result == BuildResult.Failed) return;
            Prepare(Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath)));
        }
        internal static void Prepare(string releaseRoot)
        {
            releaseRoot = Path.GetFullPath(releaseRoot);
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var docs = releaseRoot.TrimEnd(Path.DirectorySeparatorChar) + "-docs";
            Directory.CreateDirectory(docs);
            foreach (var name in new[] { "ALPHA_COMPATIBILITY.md", "FILTER_CANDIDATES.md", "MOBILE_RELEASE.md" })
            {
                var source = Path.Combine(project, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(docs, name), true);
            }
            var readme = Path.Combine(releaseRoot, "README.md");
            File.Copy(Path.Combine(project, "README.md"), readme, true);
            foreach (var path in Directory.GetFiles(releaseRoot, "*.md", SearchOption.AllDirectories))
            {
                if (path.Equals(readme, StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(docs, Path.GetRelativePath(releaseRoot, path));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(path, target, true);
                File.Delete(path);
            }
            foreach (var name in new[] { "ALPHA_BASELINE.json", "ALPHA_BUILD_INFO.json", "AndroidReleaseStatus.json" })
            {
                var path = Path.Combine(releaseRoot, name);
                if (!File.Exists(path)) continue;
                File.Copy(path, Path.Combine(docs, name), true);
                File.Delete(path);
            }
            foreach (var folder in new[] { "Skins", "MajdataPlayAlpha_Data/StreamingAssets/Skins" })
            {
                var root = Path.Combine(releaseRoot, folder);
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.GetFiles(root, "SOURCE.json", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(docs, Path.GetRelativePath(releaseRoot, path));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(path, target, true);
                    File.Delete(path);
                }
            }
            var demos = Path.GetFullPath(Path.Combine(releaseRoot, "MajdataPlayAlpha_Data/StreamingAssets/MaiCharts/Original"));
            if (!demos.StartsWith(releaseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Bundled chart path is outside the build directory.");
            if (Directory.Exists(demos)) Directory.Delete(demos, true);
            Debug.Log("PLAY LEAN PACKAGE PASS: README only; documentation exported to " + docs);
        }
    }
}
