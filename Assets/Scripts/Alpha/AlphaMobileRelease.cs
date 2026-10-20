#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MajdataPlay.Editor
{
    public static class AlphaMobileRelease
    {
        [MenuItem("Alpha/Check Android release support")]
        public static void CheckAndroid()
        {
            var module=BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android);
            var plugins=PluginImporter.GetAllImporters()
                .Where(importer=>importer.assetPath.Contains("/Android/arm64-v8a/") && importer.assetPath.EndsWith(".so") && importer.GetCompatibleWithPlatform(BuildTarget.Android))
                .Select(importer=>importer.assetPath).ToArray();
            var report=new Status { unity=Application.unityVersion,version=Alpha.AlphaVersion.Name,
                androidBuildSupport=module,enabledArm64Plugins=plugins,
                status=module ? "Build module available; SDK/NDK/JDK and device validation still required."
                    : "Android Build Support missing. Install Android SDK & NDK Tools and OpenJDK for this Unity editor." };
            var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"android-release-status.json"),JsonUtility.ToJson(report,true));
            Debug.Log("ALPHA ANDROID RELEASE CHECK: "+JsonUtility.ToJson(report));
        }

        // Match upstream's APK flow: Unity exports the Gradle project; Gradle builds the APK.
        public static void ExportAndroidProject()
        {
            CheckAndroid();
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, Android SDK & NDK Tools and OpenJDK in Unity Hub first.");
            var args=Environment.GetCommandLineArgs();
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","Android","Gradle"));
            for(var i=0;i<args.Length-1;i++) if(args[i]=="-alphaAndroidExport") output=Path.GetFullPath(args[i+1]);
            var target=NamedBuildTarget.Android;
            var identifier=PlayerSettings.GetApplicationIdentifier(target);
            var backend=PlayerSettings.GetScriptingBackend(target);
            var architectures=PlayerSettings.Android.targetArchitectures;
            var customKey=PlayerSettings.Android.useCustomKeystore;
            var api=PlayerSettings.Android.targetSdkVersion;
            var perCpu=PlayerSettings.Android.buildApkPerCpuArchitecture;
            var bundle=EditorUserBuildSettings.buildAppBundle;
            var version=PlayerSettings.bundleVersion;
            var versionCode=PlayerSettings.Android.bundleVersionCode;
            var export=EditorUserBuildSettings.exportAsGoogleAndroidProject;
            try
            {
                PlayerSettings.bundleVersion=Alpha.AlphaVersion.Name;
                PlayerSettings.SetApplicationIdentifier(target,"net.majdata.majdataplayalpha");
                PlayerSettings.Android.bundleVersionCode=61;
                PlayerSettings.SetScriptingBackend(target,ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
                PlayerSettings.Android.useCustomKeystore=false;
                PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.Android.buildApkPerCpuArchitecture=false;
                EditorUserBuildSettings.buildAppBundle=false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject=true;
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).Select(scene=>scene.path).ToArray(),
                    target=BuildTarget.Android,locationPathName=output,options=BuildOptions.CompressWithLz4HC }).summary;
                if(result.result!=BuildResult.Succeeded) throw new BuildFailedException("Android project export failed: "+result.result);
                Debug.Log("ALPHA ANDROID PROJECT EXPORTED: "+output);
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(target,identifier);
                PlayerSettings.SetScriptingBackend(target,backend);
                PlayerSettings.Android.targetArchitectures=architectures;
                PlayerSettings.Android.useCustomKeystore=customKey;
                PlayerSettings.Android.targetSdkVersion=api;
                PlayerSettings.Android.buildApkPerCpuArchitecture=perCpu;
                EditorUserBuildSettings.buildAppBundle=bundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject=export;
                PlayerSettings.bundleVersion=version;
                PlayerSettings.Android.bundleVersionCode=versionCode;
            }
        }

        public static void BuildIOS()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new BuildFailedException("Install iOS Build Support for this Unity editor first.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".codex-build", "iOS"));
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-alphaIOSOutput") output = Path.GetFullPath(args[i + 1]);
            var target = NamedBuildTarget.iOS;
            var identifier = PlayerSettings.GetApplicationIdentifier(target);
            var backend = PlayerSettings.GetScriptingBackend(target);
            var version = PlayerSettings.bundleVersion;
            var buildNumber = PlayerSettings.iOS.buildNumber;
            var sdk = PlayerSettings.iOS.sdkVersion;
            var signing = PlayerSettings.iOS.appleEnableAutomaticSigning;
            try
            {
                // Apple's bundle version must be numeric. The in-game version
                // remains alpha.0.6.1 through AlphaVersion.Name.
                PlayerSettings.bundleVersion = "0.6.1";
                PlayerSettings.iOS.buildNumber = "61";
                PlayerSettings.SetApplicationIdentifier(target, "net.majdata.majdataplayalpha");
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
                PlayerSettings.iOS.appleEnableAutomaticSigning = false;
                Directory.CreateDirectory(output);
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                    target = BuildTarget.iOS,
                    locationPathName = output,
                    options = BuildOptions.CompressWithLz4HC
                }).summary;
                if (result.result != BuildResult.Succeeded)
                    throw new BuildFailedException("iOS Xcode export failed: " + result.result);
                Debug.Log("ALPHA IOS XCODE EXPORTED: " + output + "; compile and sign on macOS with Xcode.");
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(target, identifier);
                PlayerSettings.SetScriptingBackend(target, backend);
                PlayerSettings.bundleVersion = version;
                PlayerSettings.iOS.buildNumber = buildNumber;
                PlayerSettings.iOS.sdkVersion = sdk;
                PlayerSettings.iOS.appleEnableAutomaticSigning = signing;
            }
        }
        [Serializable] sealed class Status
        {
            public string unity,version,status;
            public bool androidBuildSupport;
            public string[] enabledArm64Plugins;
        }
    }
}
#endif
