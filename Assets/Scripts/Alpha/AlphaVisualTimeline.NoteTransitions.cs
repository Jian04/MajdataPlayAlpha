using System;
using System.Collections.Generic;
using System.Linq;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    internal sealed partial class AlphaVisualTimeline
    {
        readonly Dictionary<(string Type, float Time, bool Each, bool Break, bool Mine), double> appearances = new();
        readonly Dictionary<(string Type, float Time, bool Each, bool Break, bool Mine), ScrollPoint[]> motions = new();

        internal static AlphaValueSpec ValueSpec(Kind kind)
        {
            var name = kind switch { Kind.Color => "COLOR", Kind.Alpha => "ALPHA", Kind.Size => "SIZE",
                Kind.Angle => "ANGLE", Kind.Spawn => "SPAWN", Kind.Destroy => "DESTROY", Kind.HS => "HS", _ => "SV" };
            AlphaCommandGrammar.TryFind(name, out var descriptor);
            return descriptor.scalar;
        }
        internal static string UnwrapScalar(string text)
        {
            if (text.Length > 1 && text[0] == '(' && text[^1] == ')' &&
                AlphaCommandGrammar.TrySplitValues(text.Substring(1, text.Length - 2), out var parts) && parts.Count == 1)
                return parts[0];
            return text;
        }
        static NoteValueTransition ScaleTransition(NoteValueTransition source, double scale) => source == null ? null : new()
        {
            from = source.from, to = source.to, fromY = source.fromY, toY = source.toY,
            fromZ = source.fromZ, toZ = source.toZ, duration = source.duration * (float)scale,
            easing = source.easing, parameter = source.parameter
        };
        static ScrollPoint[] BuildSvCurve(Change[] source, string type, bool typedOnly)
        {
            var relevant = source.Where(c => c.Type == type || !typedOnly && c.Type == "").ToArray();
            var boundaries = new SortedSet<double> { Math.Min(0, source[0].Time) };
            foreach (var change in relevant)
            {
                boundaries.Add(change.Time);
                if (change.Transition == null || change.Transition.duration <= 0) continue;
                var steps = change.Transition.easing == "linear" ? 1 : 128;
                for (var step = 1; step <= steps; step++) boundaries.Add(change.Time + change.Transition.duration * (double)step / steps);
            }
            var times = boundaries.ToArray();
            var entries = new List<ScrollChange>();
            Change global = null, typed = null;
            var cursor = 0;
            for (var i = 0; i < times.Length; i++)
            {
                var at = times[i];
                while (cursor < relevant.Length && relevant[cursor].Time <= at + .0000001d)
                {
                    var change = relevant[cursor++];
                    if (change.Type == "") global = change.Reset ? null : change;
                    else typed = change.Reset ? null : change;
                }
                var changeAt = typed ?? global;
                float Value(double t) => changeAt?.Transition?.Evaluate(t - changeAt.Time) ?? changeAt?.Value ?? 1;
                var value = Value(at);
                var end = i + 1 < times.Length ? times[i + 1] : at;
                var slope = end > at ? (float)((Value(end) - value) / (end - at)) : 0;
                entries.Add(new ScrollChange(at, value, i, slope));
                if (slope == 0) continue;
                var zero = at - value / slope;
                if (zero > at + .000001d && zero < end - .000001d) entries.Add(new ScrollChange(zero, 0, i, slope));
            }
            return AlphaVisualTiming.BuildScrollCurve(entries);
        }
        double AnimationOrigin(Change state, string type, double noteTime, bool each, bool brk, bool mine) =>
            state.Live ? state.Time : appearances.TryGetValue((type, (float)noteTime, each, brk, mine), out var value) ? value : noteTime;
        float Scalar(Kind kind, string type, double noteTime, double now, bool each, bool brk, bool mine, float fallback)
        {
            var state = State(kind, type, noteTime, now, each, brk, mine);
            return state?.Transition?.Evaluate(now - AnimationOrigin(state, type, noteTime, each, brk, mine)) ?? state?.Value ?? fallback;
        }
        internal float AnimatedRadius(bool spawn, double noteTime, double now, string type, bool each, bool brk, bool mine) =>
            Scalar(spawn ? Kind.Spawn : Kind.Destroy, type, noteTime, now, each, brk, mine, spawn ? 1.225f : 4.8f);
        internal void RememberAppearance(string type, double time, bool each, bool brk, bool mine, double appearance) =>
            appearances[(type, (float)time, each, brk, mine)] = appearance;
        internal bool HasMotion(string type, double time, bool each, bool brk, bool mine) =>
            motions.ContainsKey((type, (float)time, each, brk, mine));
        ScrollPoint[] MotionCurve(string type, double time, bool each, bool brk, bool mine) =>
            motions.TryGetValue((type, (float)time, each, brk, mine), out var curve) ? curve : Curve(type, each, brk, mine);
        double ScrollForNote(double now, double time, string type, bool each, bool brk, bool mine) =>
            AlphaVisualTiming.GetCumulativeScroll(MotionCurve(type, time, each, brk, mine), now);
        internal float MotionSpeed(string type, double time, bool each, bool brk, bool mine, float speed, float baseSpeed) =>
            HasMotion(type, time, each, brk, mine) ? baseSpeed : speed;
        float PrepareMotion(double time, float original, string type, bool each, bool brk, bool mine, float speed, float baseSpeed)
        {
            var hs = State(Kind.HS, type, time, time, each, brk, mine)?.Transition;
            if (hs == null || baseSpeed == 0) return original;
            var key = (type, (float)time, each, brk, mine);
            if (motions.ContainsKey(key)) return (float)appearances[key];
            var distance = type == "blockarea" ? AlphaNoiseParser.LeadTime(1) : type is "touch" or "touchhold" ? AlphaVisualTiming.GetTouchMotionDuration(baseSpeed) :
                (Math.Abs(Radius(false, time, type, each, brk, mine) - Radius(true, time, type, each, brk, mine)) + AlphaVisualTiming.SpawnScaleDistance) / baseSpeed;
            var appearance = float.IsInfinity(original) || float.IsNaN(original) ? time : (double)original;
            motions[key] = BirthCurve(Curve(type, each, brk, mine), time, distance, hs, ref appearance);
            appearances[key] = appearance;
            return (float)appearance;
        }
        readonly Dictionary<(float Time, bool Each, bool Break, bool Mine), ScrollPoint[]> slideFades = new();
        internal float PrepareSlideAppearance(float time, float original, float baseSpeed, bool each, bool brk, bool mine)
        {
            var hs = State(Kind.HS, "slide", time, time, each, brk, mine)?.Transition;
            if (hs == null || baseSpeed == 0) return original;
            var key = (time, each, brk, mine);
            if (slideFades.ContainsKey(key)) return (float)appearances[("slide", time, each, brk, mine)];
            var appearance = float.IsInfinity(original) || float.IsNaN(original) ? time : (double)original;
            slideFades[key] = BirthCurve(Array.Empty<ScrollPoint>(), time, 3.926913f / Math.Abs(baseSpeed), hs, ref appearance);
            RememberAppearance("slide", time, each, brk, mine, appearance);
            RememberAppearance("slidestar", time, each, brk, mine, appearance);
            return (float)appearance;
        }
        internal float? SlideFade(float now, float time, bool each, bool brk, bool mine)
        {
            if (!slideFades.TryGetValue((time, each, brk, mine), out var curve)) return null;
            var appearance = appearances[("slide", time, each, brk, mine)];
            var start = AlphaVisualTiming.GetCumulativeScroll(curve, appearance);
            var total = AlphaVisualTiming.GetCumulativeScroll(curve, time) - start;
            return now >= time ? 1 : now <= appearance || Math.Abs(total) < .000001d ? 0 :
                Clamp((float)((AlphaVisualTiming.GetCumulativeScroll(curve, now) - start) / total));
        }
        // The same appearance solver and 128-segment baking as View's HS curve.
        // This runs while loading, never from note Update/OnLateUpdate.
        static ScrollPoint[] BirthCurve(ScrollPoint[] source, double judgeTime, double distance, NoteValueTransition hs, ref double appearance)
        {
            double Integral(double start)
            {
                const int steps = 64;
                var dt = (judgeTime - start) / steps;
                double sum = 0;
                for (var i = 0; i < steps; i++)
                {
                    var at = start + (i + .5d) * dt;
                    sum += AlphaVisualTiming.GetMultiplier(source, at) * hs.Evaluate(at - start) * dt;
                }
                return sum;
            }
            var sv = AlphaVisualTiming.GetMultiplier(source, judgeTime);
            if (Math.Abs(sv) <= AlphaVisualTiming.Epsilon)
                for (var i = source.Length - 1; i >= 0; i--)
                    if (source[i].Time <= judgeTime && Math.Abs(source[i].Multiplier) > AlphaVisualTiming.Epsilon) { sv = source[i].Multiplier; break; }
            var direction = Math.Sign(hs.from != 0 ? hs.from : hs.to) * Math.Sign(sv);
            var target = distance * (direction == 0 ? 1 : direction);
            var low = judgeTime - Math.Max(.01d, judgeTime - appearance);
            for (var i = 0; direction != 0 && i < 24 && Integral(low) * target < target * target; i++) low = judgeTime - (judgeTime - low) * 2;
            if (direction != 0 && Integral(low) * target >= target * target)
            {
                var high = judgeTime;
                for (var i = 0; i < 28; i++)
                {
                    var mid = (low + high) * .5d;
                    if (Integral(mid) * target >= target * target) low = mid; else high = mid;
                }
                appearance = (low + high) * .5d;
            }
            var appeared = appearance;
            var endTime = Math.Max(judgeTime, appeared + hs.duration);
            if (source.Length > 0) endTime = Math.Max(endTime, source[^1].Time);
            var boundaries = new SortedSet<double> { appeared, judgeTime, endTime, appeared + hs.duration };
            foreach (var point in source) if (point.Time > appeared && point.Time < endTime) boundaries.Add(point.Time);
            for (var i = 1; i < 128; i++) boundaries.Add(appeared + hs.duration * i / 128d);
            var times = boundaries.ToArray();
            var entries = new List<ScrollChange>();
            for (var i = 0; i < times.Length; i++)
            {
                var at = times[i];
                float Value(double t) => AlphaVisualTiming.GetMultiplier(source, t) * hs.Evaluate(t - appeared);
                var value = Value(at);
                var end = i + 1 < times.Length ? times[i + 1] : at;
                var slope = end > at ? (float)((Value(end - .00000001d) - value) / (end - at)) : 0;
                entries.Add(new ScrollChange(at, value, i, slope));
                if (slope == 0) continue;
                var zero = at - value / slope;
                if (zero > at + .000001d && zero < end - .000001d) entries.Add(new ScrollChange(zero, 0, i, slope));
            }
            return AlphaVisualTiming.BuildScrollCurve(entries);
        }
    }
}
