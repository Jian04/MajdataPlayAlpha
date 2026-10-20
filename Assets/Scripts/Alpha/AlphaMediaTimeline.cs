using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using Newtonsoft.Json;

namespace MajdataPlay.Alpha
{
    // Matches the saved Edit project. Temporary, unsaved edits are not imported.
    internal sealed class AlphaMediaClip
    {
        public int Track { get; set; }
        public int TrackIndex { get; set; }
        public string SourcePath { get; set; } = "";
        public double TimelineStart { get; set; }
        public double SourceOffset { get; set; }
        public double Duration { get; set; }
        public bool IsStillImage { get; set; }
        public double Volume { get; set; } = 1;
        public double FadeIn { get; set; }
        public double FadeOut { get; set; }
        public double Brightness { get; set; }
        public double Opacity { get; set; } = 1;
        [JsonIgnore] public double End => TimelineStart + Duration;
        internal bool Active(double time) => time >= TimelineStart && time < End;
        internal double SourceTime(double time) => SourceOffset + time - TimelineStart;
        internal double Fade(double time)
        {
            var elapsed = time - TimelineStart;
            return Math.Min(FadeIn > 0 ? Math.Min(1, elapsed / FadeIn) : 1,
                FadeOut > 0 ? Math.Min(1, (Duration - elapsed) / FadeOut) : 1);
        }
    }
    internal sealed class AlphaMediaTimeline
    {
        public List<AlphaMediaClip> Clips { get; set; } = new();
        [JsonIgnore] internal bool HasAudio => Clips.Any(c => c.Track == 1);
        [JsonIgnore] internal bool HasVideo => Clips.Any(c => c.Track == 0);
        [JsonIgnore] internal double End => Clips.Select(c => c.End).DefaultIfEmpty(0).Max();
        internal static AlphaMediaTimeline Load(string folder)
        {
            var file = Path.Combine(folder, "media_timeline.json");
            if (string.IsNullOrEmpty(folder) || !File.Exists(file)) return null;
            var project = JsonConvert.DeserializeObject<AlphaMediaTimeline>(File.ReadAllText(file));
            if (project == null) return null;
            project.Clips ??= new List<AlphaMediaClip>();
            project.Clips.RemoveAll(c => c == null || c.Track is < 0 or > 1 ||
                string.IsNullOrWhiteSpace(c.SourcePath) || !Finite(c.Duration) || c.Duration <= 0);
            foreach (var clip in project.Clips)
            {
                clip.SourcePath = Path.GetFullPath(Path.IsPathRooted(clip.SourcePath) ? clip.SourcePath : Path.Combine(folder, clip.SourcePath));
                clip.TimelineStart = Nonnegative(clip.TimelineStart);
                clip.SourceOffset = Nonnegative(clip.SourceOffset);
                clip.Volume = Clamp(clip.Volume, 0, 4, 1);
                clip.Opacity = Clamp(clip.Opacity, 0, 1, 1);
                clip.Brightness = Clamp(clip.Brightness, -1, 1, 0);
                clip.FadeIn = Clamp(clip.FadeIn, 0, clip.Duration, 0);
                clip.FadeOut = Clamp(clip.FadeOut, 0, clip.Duration, 0);
            }
            return project.Clips.Count == 0 ? null : project;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Nonnegative(double value) => Finite(value) ? Math.Max(0, value) : 0;
        static double Clamp(double value, double min, double max, double fallback) => Math.Min(max, Math.Max(min, Finite(value) ? value : fallback));

        // Render using the already bundled decoder, without requiring an FFmpeg install.
        // Both audio tracks sum onto one clock; holes stay silent and source trims stay exact.
        internal string RenderAudio(string originalTrack, double originalLength, string output, CancellationToken token)
        {
            var clips = Clips.Where(c => c.Track == 1).ToList();
            if (clips.Count == 0) clips.Add(new AlphaMediaClip { Track = 1, SourcePath = originalTrack, Duration = originalLength });
            var end = Math.Max(End, clips.Max(c => c.End));
            const int rate = 44100;
            var frames = checked((int)Math.Ceiling(end * rate));
            var mix = new float[checked(frames * 2)];
            var buffer = new float[8192];
            foreach (var clip in clips)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(clip.SourcePath)) throw new FileNotFoundException("剪辑音频素材不存在", clip.SourcePath);
                var decoder = Bass.CreateStream(clip.SourcePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (decoder == 0 && Bass.LastError == Errors.Init)
                {
                    // Unity audio backend has no BASS output device. Decode offline only.
                    Bass.Init(Bass.NoSoundDevice, rate);
                    decoder = Bass.CreateStream(clip.SourcePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                }
                if (decoder == 0) throw new IOException("无法解码剪辑音频：" + clip.SourcePath + " (" + Bass.LastError + ")");
                var mixer = 0;
                try
                {
                    if (!Bass.ChannelSetPosition(decoder, Bass.ChannelSeconds2Bytes(decoder, clip.SourceOffset)))
                        throw new IOException("无法定位剪辑音频：" + clip.SourcePath);
                    mixer = BassMix.CreateMixerStream(rate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerEnd);
                    if (mixer == 0 || !BassMix.MixerAddChannel(mixer, decoder, BassFlags.MixerChanNoRampin))
                        throw new IOException("无法合成剪辑音轨：" + Bass.LastError);
                    var start = (int)Math.Round(clip.TimelineStart * rate);
                    var length = Math.Min((int)Math.Round(clip.Duration * rate), frames - start);
                    var readFrames = 0;
                    while (readFrames < length)
                    {
                        token.ThrowIfCancellationRequested();
                        var requested = Math.Min(buffer.Length / 2, length - readFrames);
                        var bytes = Bass.ChannelGetData(mixer, buffer, requested * 2 * sizeof(float));
                        if (bytes < 0 && Bass.LastError == Errors.Ended) break;
                        if (bytes < 0) throw new IOException("读取剪辑音轨失败：" + Bass.LastError);
                        if (bytes == 0) break;
                        var count = bytes / (2 * sizeof(float));
                        for (var i = 0; i < count; i++)
                        {
                            var time = clip.TimelineStart + (readFrames + i) / (double)rate;
                            var gain = (float)(clip.Volume * clip.Fade(time));
                            var index = (start + readFrames + i) * 2;
                            mix[index] += buffer[i * 2] * gain;
                            mix[index + 1] += buffer[i * 2 + 1] * gain;
                        }
                        readFrames += count;
                    }
                }
                finally { if (mixer != 0) Bass.StreamFree(mixer); Bass.StreamFree(decoder); }
            }
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using var writer = new BinaryWriter(File.Create(output));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + mix.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(mix.Length * 2);
            foreach (var sample in mix) writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample)) * 32767));
            return output;
        }
    }
}
