#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MajSimai;
using UnityEngine;

namespace MajdataPlay.Alpha
{
    public static class AlphaOct13Verification
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            var folder = args[Array.IndexOf(args, "-alphaImageFixture") + 1];
            using var input = File.OpenRead(Path.Combine(folder, "maidata.txt"));
            var metadata = SimaiParser.ParseMetadata(input);
            var chart = AlphaSimaiParser.ParseChart("", "", metadata.Fumens[4]);
            var timeline = AlphaSimaiParser.GetTimeline(chart);
            var notes = chart.NoteTimings.ToArray().SelectMany(t => t.Notes.Select(n => (Time: t.Timing, Note: n))).Take(64).ToArray();
            double Sample(int frames)
            {
                double checksum = 0;
                for (var frame = 0; frame < frames; frame++) foreach (var item in notes)
                {
                    var local = timeline.ForNote(item.Note);
                    var type = item.Note.Type == SimaiNoteType.Hold ? "hold" : "tap";
                    var now = item.Time + frame % 60 / 60d;
                    checksum += local.Color(type, item.Time, now, true, false, false) ?? 0;
                    checksum += local.Alpha(type, item.Time, now, true, false, false);
                    checksum += local.Size(type, item.Time, now, true, false, false).X;
                    checksum += local.Radius(true, item.Time, type, true, false, false);
                    checksum += local.Radius(false, item.Time, type, true, false, false);
                }
                return checksum;
            }
            Sample(10);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = Sample(600); watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (allocated > 1024) throw new Exception("Alpha visual lookup allocates during playback: " + allocated);
            Debug.Log($"ALPHA VISUAL ALLOCATION PASS: Unity Mono, 600 frames x 64 notes, {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated} bytes; checksum={result:R}");
            var totalFrames = 0; var filterFrames = 0;
            for (var time = 0f; time < 161.55f; time += 1f / 60)
            {
                totalFrames++;
                if (timeline.NeedsScreenRender(time)) filterFrames++;
            }
            if (filterFrames >= totalFrames / 2) throw new Exception("Inactive screen effects still execute the filter pass");
            Debug.Log($"ALPHA IDLE FILTER PASS: MEGALOVANIA Master needs {filterFrames}/{totalFrames} postprocess frames at 60Hz, instead of rendering its entire chart.");
            MajdataPlay.Editor.AlphaFakeVerification.Run();
            AlphaOct12Verification.Build();
        }
    }
}
#endif
