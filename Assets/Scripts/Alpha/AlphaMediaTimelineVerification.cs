#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using ManagedBass;
using Newtonsoft.Json;
using UnityEngine;

namespace MajdataPlay.Editor
{
    internal static class AlphaMediaTimelineVerification
    {
        internal static void Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "AlphaTimelineCheck"));
            Directory.CreateDirectory(folder);
            var source = Path.Combine(folder, "source.wav");
            const int rate = 44100;
            using (var writer = new BinaryWriter(File.Create(source)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + rate * 4 * 4);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
                writer.Write((short)4); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(rate * 4 * 4);
                for (var i = 0; i < rate * 4; i++)
                {
                    var value = (short)((i < rate ? .1 : i < rate * 2 ? .2 : .3) * 32767);
                    writer.Write(value); writer.Write(value);
                }
            }
            var path = Path.Combine(folder, "media_timeline.json");
            File.WriteAllText(path, @"{""Version"":1,""Clips"":[
                {""Track"":1,""TrackIndex"":0,""SourcePath"":""source.wav"",""TimelineStart"":1,""SourceOffset"":1,""Duration"":2,""Volume"":0.5,""FadeIn"":0.5,""FadeOut"":0.5},
                {""Track"":1,""TrackIndex"":1,""SourcePath"":""source.wav"",""TimelineStart"":2,""SourceOffset"":0,""Duration"":1},
                {""Track"":0,""SourcePath"":""source.wav"",""TimelineStart"":1,""SourceOffset"":2,""Duration"":3,""Opacity"":0.6,""Brightness"":0.2}
            ]}");
            var project = MajdataPlay.Alpha.AlphaMediaTimeline.Load(folder);
            if (!project.HasAudio || !project.HasVideo || project.End != 4) throw new Exception("Edit timeline project was not loaded");
            var clip = project.Clips.Last();
            if (clip.Active(.5) || !clip.Active(1) || clip.Active(4) || clip.SourceTime(2) != 3 || clip.Opacity != .6 || clip.Brightness != .2)
                throw new Exception("PV timeline/source offsets or boundaries lost");
            Bass.Init(Bass.NoSoundDevice, rate);
            var output = project.RenderAudio(source, 4, Path.Combine(folder, "mixed.wav"), default);
            using (var reader = new BinaryReader(File.OpenRead(output)))
            {
                void Near(double time, double expected)
                {
                    reader.BaseStream.Position = 44 + (long)Math.Round(time * rate) * 4;
                    for (var channel = 0; channel < 2; channel++)
                        if (Math.Abs(reader.ReadInt16() / 32767d - expected) > .002) throw new Exception("Timeline mix mismatch at " + time);
                }
                Near(.5, 0); Near(1.125, .025); Near(1.75, .1); Near(2.25, .25); Near(2.875, .1375); Near(3.5, 0);
                if (reader.BaseStream.Length != 44 + rate * 4 * 4) throw new Exception("Timeline length lost silent tail");
            }
            var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
            try { project.RenderAudio(source, 4, output, cancellation.Token); throw new Exception("Timeline cancellation ignored"); }
            catch (OperationCanceledException) { }
            var tail = new MajdataPlay.Alpha.AlphaMediaTimeline();
            tail.Clips.Add(new MajdataPlay.Alpha.AlphaMediaClip { Track = 1, SourcePath = source, SourceOffset = 3.5, Duration = 1.5 });
            tail.RenderAudio(source, 4, output, default);
            using (var reader = new BinaryReader(File.OpenRead(output)))
            {
                reader.BaseStream.Position = 44 + rate * 4;
                if (reader.ReadInt16() != 0) throw new Exception("EOF did not pad the clip with silence");
            }
            foreach (var skin in new[] { "default", "default-C", "Deluxe", "Deluxe-C" })
            {
                var root = Path.Combine(Application.streamingAssetsPath, "Skins", skin);
                var manifest = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(Path.Combine(root, "SOURCE.json")));
                foreach (var relative in new[] { "HoldSkins/hold.png", "HoldSkins/hold_on.png", "JudgeTextSkins/judge_text_cPerfect.png", "JudgeTextSkins/judge_text_cPerfect_break.png" })
                {
                    var original = (string)manifest["resolvedSprites"][relative.Replace('/', Path.DirectorySeparatorChar)];
                    if (original == null || original.Contains("skin-fallback")) throw new Exception("Skin still uses bundled Hold/judge art: " + skin + "/" + relative);
                    if (File.Exists(original) && !File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Path.Combine(root, relative))))
                        throw new Exception("Imported skin differs from the supplied pack: " + relative);
                }
            }
            if (MajInstances.VersionName != MajdataPlay.Alpha.AlphaVersion.Name) throw new Exception("Alpha display version differs from the build version");
            Debug.Log("ALPHA EDIT TIMELINE CHECK PASS: saved project, track/PV offsets, source trims, silent gaps/tail, overlapping tracks, gain/fades, speed-independent video time, cancellation; supplied Hold/judge sprites; " + MajdataPlay.Alpha.AlphaVersion.Name);
        }
    }
}
#endif
