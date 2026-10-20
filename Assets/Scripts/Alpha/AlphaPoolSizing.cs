using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Scenes.Game.Buffers;

namespace MajdataPlay.Alpha
{
    internal static class AlphaPoolSizing
    {
        internal static int RequiredCapacity(IEnumerable<NotePoolingInfo> source, int configured)
        {
            var notes = source.ToArray();
            bool HasScroll(NotePoolingInfo n) => n.AlphaTimeline?.HasSV == true ||
                n is EachLinePoolingInfo line && ((line.MemberA != null && HasScroll(line.MemberA)) ||
                                                  (line.MemberB != null && HasScroll(line.MemberB)));
            if (!notes.Any(HasScroll)) return configured;
            float End(NotePoolingInfo n) => n.Timing + .6f + (n switch
            {
                HoldPoolingInfo h => Math.Max(0, h.LastFor),
                TouchHoldPoolingInfo h => Math.Max(0, h.LastFor),
                EachLinePoolingInfo l => Math.Max(l.MemberA == null ? n.Timing : End(l.MemberA),
                                                 l.MemberB == null ? n.Timing : End(l.MemberB)) - n.Timing,
                _ => 0
            });
            // Reversals can reveal several future passages together. Reserve their
            // actual overlapping lifetimes; a fixed native pool must not hide them.
            var events = new List<(float Time, int Delta)>();
            foreach (var n in notes)
            {
                events.Add((n.AppearTiming - .15f, 1));
                events.Add((Math.Max(n.AppearTiming, End(n)), -1));
            }
            var active = 0;
            var required = Math.Max(0, configured);
            foreach (var e in events.OrderBy(e => e.Time).ThenByDescending(e => e.Delta))
            {
                active += e.Delta;
                required = Math.Max(required, active);
            }
            return required;
        }
    }
}
