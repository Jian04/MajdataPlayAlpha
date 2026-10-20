using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MajSimai;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    internal static class AlphaPathParser
    {
        internal static bool TryRead(string text, out NoteExpressionData expression)
        {
            if (!NoteExpressionParser.TryParse(text, out expression, out _) || expression.kind != NoteExpressionKind.Slide) return false;
            // Native SlideCode keeps its own geometry and judgment table.
            if (expression.path.segments.Any(s => s.shape == "SC")) return false;
            return expression.isTouchPath || expression.path.head.isDZone ||
                expression.path.segments.Any(s => s.shape is "rp" or "rq" or "P" or "Q" || s.end.isDZone || s.start.isDZone || s.hasMiddle && s.middle.isDZone);
        }
        internal static SimaiNote Parse(double time, double bpm, string text)
        {
            if (!TryRead(text, out var expression)) return null;
            var segments = expression.path.segments;
            // Reuse MajSimai's duration parser, including BPM/#/## and chain waits.
            var dummy = "1" + string.Concat(segments.Select(s => "-5" + s.duration));
            var note = SimaiParser.ParseChart($"({bpm.ToString(CultureInfo.InvariantCulture)}){{4}}{dummy},").NoteTimings[0].Notes[0];
            note.RawContent = text;
            note.StartPosition = expression.position.area == 'C' ? 8 : expression.position.position;
            note.TouchArea = expression.position.IsTouch ? expression.position.area : ' ';
            note.SlideStartTime += time;
            var flags = expression.modifiers;
            note.IsBreak = flags.HasHead(NoteModifierFlags.Break);
            note.IsEx = flags.HasHead(NoteModifierFlags.Ex);
            note.IsHanabi = flags.HasHead(NoteModifierFlags.Firework);
            note.IsMine = flags.HasHead(NoteModifierFlags.Mine);
            note.IsMineSlide = flags.HasSlide(NoteModifierFlags.Mine);
            note.IsSlideBreak = flags.HasSlide(NoteModifierFlags.Break);
            note.IsSlideNoHead = flags.HasAny(NoteModifierFlags.NoHead);
            return note;
        }
        internal static SimaiNote[] ParseSlot(double time, double bpm, string text)
        {
            if (!NoteSlotParser.TrySplit(text, out var parts, out _) || !parts.Any(p => TryRead(p.text, out _) || AlphaNoteDecoration.Read(p.text) != null)) return null;
            var notes = new List<SimaiNote>();
            var headedGroups = new HashSet<int>();
            foreach (var part in parts)
            {
                var note = AlphaNoteDecoration.Parse(time, bpm, part.text);
                if (note == null)
                {
                    var native = SimaiParser.ParseChart($"({bpm.ToString(CultureInfo.InvariantCulture)}){{4}}{part.text},");
                    if (native.NoteTimings.Length == 0) continue;
                    foreach (var other in native.NoteTimings[0].Notes) { other.SlideStartTime += time; notes.Add(other); }
                    continue;
                }
                if (part.fromSameHead && !headedGroups.Add(part.groupIndex)) note.IsSlideNoHead = true;
                notes.Add(note);
            }
            return notes.ToArray();
        }
    }
}
