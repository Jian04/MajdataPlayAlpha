#if UNITY_EDITOR
using System;
using MajdataPlay.Alpha;
using UnityEngine;

namespace MajdataPlay.Editor
{
    public static class AlphaNoisePatchBuild
    {
        public static void Build()
        {
            AlphaMobileRelease.CheckAndroid();
            AlphaNoiseVerification.Run();
            AlphaDisplayVerification.Run();
            AlphaOct08Verification.Run();
            AlphaOct10Verification.Run();
            Type.GetType("MajdataPlay.Editor.CustomBuild, Assembly-CSharp-Editor", true)
                .GetMethod("BuildRelease").Invoke(null, null);
        }
    }
}
#endif
