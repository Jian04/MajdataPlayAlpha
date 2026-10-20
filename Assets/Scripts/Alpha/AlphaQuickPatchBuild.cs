#if UNITY_EDITOR
using System;
using System.IO;
using MajSimai;
using MajdataPlay.Alpha;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Parsing;
using UnityEngine;

namespace MajdataPlay.Editor
{
    public static class AlphaQuickPatchBuild
    {
        public static void Build()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();
            var chart = AlphaSimaiParser.ParseChart("14", "test", "(120){4}1f,2f-6v4[1:1],A1-A4/3,");
            var recovered = NoteLoader.FilterUnsupportedNotes(chart);
            if (!recovered.NoteTimings[0].Notes[0].IsHanabi || recovered.NoteTimings[1].Notes.Length != 1 ||
                NoteLoader.NormalizeSlideContent(recovered.NoteTimings[1].Notes[0].RawContent) != "2-6v4[1:1]")
                throw new Exception("Firework path normalization failed");
            var path = "D:/Maimai/release/MajdataPlayAlpha/MaiCharts/Alpha/7wonders/maidata.txt";
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var metadata = SimaiParser.ParseMetadata(stream);
                var difficulties = 0;
                foreach (var fumen in metadata.Fumens)
                {
                    if (string.IsNullOrWhiteSpace(fumen)) continue;
                    NoteLoader.FilterUnsupportedNotes(AlphaSimaiParser.ParseChart("", "", fumen));
                    difficulties++;
                }
                Debug.Log($"QUICK PATCH PASS: 7wonders {difficulties} difficulties parsed/filtered; f head path normalized, unknown touchslide skipped.");
            }
            Type.GetType("MajdataPlay.Editor.CustomBuild, Assembly-CSharp-Editor", true)
                .GetMethod("BuildRelease").Invoke(null, null);
        }
    }
}
#endif
