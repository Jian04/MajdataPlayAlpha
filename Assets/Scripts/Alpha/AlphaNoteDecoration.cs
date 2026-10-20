using System;
using System.Text;
using System.Linq;
using MajSimai;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    internal static class AlphaNoteDecoration
    {
        internal static NoteExpressionData Read(string raw) =>
            raw != null && NoteExpressionParser.TryParse(raw, out var expression, out _) && (raw.Contains('~') || expression.position.isDZone || expression.kind == NoteExpressionKind.Hold && expression.modifiers.HasHead(NoteModifierFlags.Firework)) ? expression : null;
        internal static bool IsDHead(string raw) => Read(raw)?.position.isDZone == true;
        internal static bool IsCarrier(SimaiNote note) => Read(note.RawContent)?.trajectory != null;
        internal static SimaiTimingPoint WithoutCarriers(SimaiTimingPoint timing) => new(
            timing.Timing, timing.Notes.Where(n => !IsCarrier(n)).ToArray(), timing.RawContent,
            timing.RawTextPositionX, timing.RawTextPositionY, timing.Bpm, timing.HSpeed,
            timing.SVeloc, timing.RawTextPosition, timing.SignatureNumerator, timing.SignatureDenominator);
        internal static double TrajectoryDuration(NoteExpressionData expression, double bpm)
        {
            var dummy = "1" + string.Concat(expression.trajectory.segments.Select(s => "-5" + s.duration));
            return SimaiParser.ParseChart($"({bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)}){{4}}{dummy},").NoteTimings[0].Notes[0].SlideTime;
        }
        internal static string Strip(string raw)
        {
            var result = new StringBuilder();
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '~' && i + 1 < raw.Length && raw[i + 1] == '[')
                {
                    var depth = 1; i += 2;
                    while (i < raw.Length && depth > 0) { if (raw[i] == '[') depth++; if (raw[i] == ']') depth--; i++; }
                    i--; continue;
                }
                result.Append(raw[i]);
            }
            return result.ToString();
        }
        internal static SimaiNote Parse(double time, double bpm, string raw)
        {
            var decoration = Read(raw);
            if (decoration == null) return AlphaPathParser.Parse(time, bpm, raw);
            var bare = Strip(raw);
            var note = AlphaPathParser.Parse(time, bpm, bare);
            if (note == null)
            {
                if (decoration.position.isDZone) bare = bare.Remove(1, 1);
                if (decoration.kind == NoteExpressionKind.Hold) bare = bare.Replace("f", "");
                var parsed = SimaiParser.ParseChart($"({bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)}){{4}}{bare},");
                if (parsed.NoteTimings.Length == 0 || parsed.NoteTimings[0].Notes.Length != 1) return null;
                note = parsed.NoteTimings[0].Notes[0];
                note.SlideStartTime += time;
            }
            if (decoration.trajectory != null)
            {
                // Resolve on the stream's own BPM, then let playback transforms
                // scale the stored seconds just like an ordinary Slide duration.
                note.SlideStartTime = time;
                note.SlideTime = TrajectoryDuration(decoration, bpm);
            }
            note.IsHanabi |= decoration.modifiers.HasHead(NoteModifierFlags.Firework);
            note.RawContent = raw;
            return note;
        }
    }
}
