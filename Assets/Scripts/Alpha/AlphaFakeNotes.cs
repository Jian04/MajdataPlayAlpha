using System.Collections.Generic;
using MajSimai;

namespace MajdataPlay.Alpha
{
    // Keep fake state outside the pinned upstream parser and its judgment data.
    internal static class AlphaFakeNotes
    {
        internal static bool IsFake(SimaiNote note, SimaiTimingPoint timing, bool slide = false,
            AlphaVisualTimeline timeline = null)
        {
            if (AlphaNoteDecoration.IsCarrier(note)) return true;
            var chartTimeline = timeline ?? AlphaSimaiParser.Current;
            var ownEach = chartTimeline.NoteContexts.TryGetValue(note, out var context) ? (bool?)context.Each : null;
            timeline = chartTimeline.ForNote(note);
            if (!timeline.HasFake) return false;
            var heads = 0;
            foreach (var other in timing.Notes)
                if (!other.IsMine && !other.IsSlideNoHead) heads++;
            var type = slide ? "slide" : note.Type switch
            {
                SimaiNoteType.Slide => AlphaPathParser.TryRead(note.RawContent, out var path) && path.position.IsTouch ? "touch" : "star",
                SimaiNoteType.Tap => note.IsForceStar ? "star" : "tap",
                SimaiNoteType.Hold => "hold",
                SimaiNoteType.Touch => "touch",
                SimaiNoteType.TouchHold => "touchhold",
                _ => ""
            };
            return timeline.IsFake(type, timing.Timing, ownEach ?? heads > 1,
                slide ? note.IsSlideBreak : note.IsBreak, slide ? note.IsMineSlide : note.IsMine);
        }

        // A statistics-only projection. Visual notes and the original chart are preserved.
        internal static SimaiChart JudgableChart(SimaiChart chart)
        {
            var timeline = AlphaSimaiParser.GetTimeline(chart);
            if (!timeline.HasFake && !System.Linq.Enumerable.Any(chart.NoteTimings.ToArray(), t => System.Linq.Enumerable.Any(t.Notes, AlphaNoteDecoration.IsCarrier))) return chart;
            var timings = new List<SimaiTimingPoint>();
            foreach (var timing in chart.NoteTimings)
            {
                var notes = new List<SimaiNote>();
                foreach (var note in timing.Notes)
                {
                    var headFake = IsFake(note, timing, timeline: timeline);
                    if (note.Type != SimaiNoteType.Slide)
                    {
                        if (!headFake) notes.Add(note);
                        continue;
                    }
                    var slideFake = IsFake(note, timing, true, timeline);
                    if (!headFake && !slideFake) { notes.Add(note); continue; }
                    if (slideFake && (headFake || note.IsSlideNoHead)) continue;
                    notes.Add(new SimaiNote
                    {
                        Type = slideFake ? (AlphaPathParser.TryRead(note.RawContent, out var path) && path.position.IsTouch ? SimaiNoteType.Touch : SimaiNoteType.Tap) : SimaiNoteType.Slide,
                        StartPosition = note.StartPosition, HoldTime = note.HoldTime,
                        IsBreak = note.IsBreak, IsSlideBreak = note.IsSlideBreak,
                        IsSlideNoHead = headFake || note.IsSlideNoHead,
                        IsForceStar = slideFake || note.IsForceStar, IsFakeRotate = note.IsFakeRotate,
                        IsEx = note.IsEx, IsHanabi = note.IsHanabi, IsTapHeadSlide = note.IsTapHeadSlide,
                        IsMine = note.IsMine, IsMineSlide = note.IsMineSlide,
                        UsingSV = note.UsingSV, RawContent = note.RawContent,
                        SlideStartTime = note.SlideStartTime, SlideTime = note.SlideTime, TouchArea = note.TouchArea
                    });
                }
                timings.Add(new SimaiTimingPoint(timing.Timing, notes.ToArray(), timing.RawContent,
                    timing.RawTextPositionX, timing.RawTextPositionY, timing.Bpm, timing.HSpeed,
                    timing.SVeloc, timing.RawTextPosition, timing.SignatureNumerator, timing.SignatureDenominator));
            }
            return new SimaiChart(chart.Level, chart.Designer, chart.Fumen, timings.ToArray(), chart.CommaTimings);
        }
    }
}
