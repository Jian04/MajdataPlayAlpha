using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    // Chart-time display state. No input, judgment, scoring or audio dependencies.
    internal sealed partial class AlphaVisualTimeline
    {
        internal enum Kind { SV, HS, Bounce, SpawnMode, Color, Alpha, Size, Angle, Spawn, Destroy, Fake, Effect, Display, Noise }
        internal sealed class Change
        {
            public Kind Command;
            public double Time;
            public int Position;
            public string Type = "";
            public bool Live, Reset;
            public float Value;
            public float ValueY;
            public uint RGB;
            public string Token;
            public NoteValueTransition Transition;
            public ScreenEffectEvent Effect;
            public AlphaDisplayEvent Display;
            public int Sensor;
            public float NoiseHS = 1, NoiseTimeScale = 1;
            public AlphaVisualTimeline NoiseSource;
        }

        static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
            { "tap", "hold", "star", "slide", "slidestar", "touch", "touchhold", "each", "break", "mine", "blockarea" };
        internal readonly Dictionary<MajSimai.SimaiNote, AlphaVisualTimeline> NoteOverrides = new();
        internal readonly Dictionary<MajSimai.SimaiNote, (double Bpm, float HS, bool Each)> NoteContexts = new();
        internal IEnumerable<Change> Changes => _changes;
        internal AlphaVisualTimeline ForNote(MajSimai.SimaiNote note) => NoteOverrides.TryGetValue(note, out var timeline) ? timeline : this;
        readonly Change[] _changes;
        readonly Dictionary<string, ScrollPoint[]> _scroll = new();
        readonly HashSet<ScrollPoint[]> _modifiedCurves = new();
        readonly Dictionary<(Kind Kind, string Type, bool Live), StateTrack> _states = new();
        readonly Dictionary<string, AlphaEffectTrack> _effects = new();
        readonly AlphaEffectTrack kaleidoscopeVisibility;
        internal static readonly AlphaVisualTimeline Empty = new(Array.Empty<Change>());
        public bool HasEffects => _changes.Length != 0 || NoteOverrides.Count != 0;
        public bool HasSV => _scroll.Count != 0 || _hasBirthMotion;
        readonly bool _hasBirthMotion;
        readonly Dictionary<(int Sensor, double Time), Change> noiseChanges = new();
        public bool HasSkinEffects { get; }
        public readonly NoiseZoneTimeline Noise = new();
        public NoiseZoneEvent[] NoiseEvents { get; }
        public bool HasNoise => NoiseEvents.Length != 0;
        public double NoiseEnd { get; }
        public bool EnvironmentFinished(double time) => time >= NoiseEnd;
        public bool HasScreenEffects => _effects.Count != 0;
        public bool NeedsScreenRender(float time)
        {
            // Future Trail commands need preceding frames; finished tracks do not.
            if (NeedsTrailHistory(time)) return true;
            foreach (var effect in _effects)
            {
                var value = effect.Key == "Kaleidoscope" ? KaleidoscopeVisibility(time) : effect.Value.Evaluate(time, out _);
                if (Math.Abs(value) > .0001f) return true;
            }
            return false;
        }
        public bool HasTrail => _effects.ContainsKey("Trail");
        public bool NeedsTrailHistory(float time) =>
            _effects.TryGetValue("Trail", out var trail) &&
            (Math.Abs(trail.Evaluate(time)) > .0001f || trail.IsUpcoming(time, float.MaxValue));
        bool _hasFake;
        public bool HasFake => _hasFake || NoteOverrides.Values.Any(v => v.HasFake);
        public bool HasDisplay => displayEvents.Length != 0;
        readonly AlphaDisplayEvent[] displayEvents;
        public AlphaDisplayEvent[] DisplayEvents => displayEvents;
        public float? DisplayValue(string name, double time, float baseline = 1)
        {
            AlphaDisplayEvent previous = null;
            float from = baseline, target = baseline;
            foreach (var ev in displayEvents)
            {
                if (ev.Name != name || (float)ev.Time > (float)time) continue;
                from = previous == null ? baseline : previous.Duration <= 0 ? target
                    : from + (target - from) * Clamp((float)(ev.Time - previous.Time) / previous.Duration);
                target = ev.ResetToDefault ? baseline : ev.Value;
                previous = ev;
            }
            return previous == null ? null : previous.Duration <= 0 ? target
                : from + (target - from) * Clamp((float)(time - previous.Time) / previous.Duration);
        }
        public bool IsFake(string type, double noteTime, bool each, bool brk, bool mine = false) =>
            ((mine ? Lookup(Kind.Fake, "mine", false, noteTime) : null)
            ?? (brk ? Lookup(Kind.Fake, "break", false, noteTime) : each ? Lookup(Kind.Fake, "each", false, noteTime) : null)
            ?? Lookup(Kind.Fake, type, false, noteTime)
            ?? Lookup(Kind.Fake, "", false, noteTime))?.Value == 1;

        internal AlphaVisualTimeline(IEnumerable<Change> changes)
        {
            _changes = changes.OrderBy(x => x.Time).ThenBy(x => x.Position).ToArray();
            _hasBirthMotion = _changes.Any(c => c.Command == Kind.HS && c.Transition != null);
            foreach (var change in _changes) if (change.Command == Kind.Noise) noiseChanges[(change.Sensor, change.Time)] = change;
            displayEvents = _changes.Where(x => x.Command == Kind.Display).Select(x => x.Display).ToArray();
            NoiseEvents = _changes.Where(c=>c.Command==Kind.Noise).Select(c=>new NoiseZoneEvent(c.Sensor,c.Time,c.Value,c.ValueY)).ToArray();
            NoiseEnd = HasNoise ? NoiseEvents.Max(zone => zone.End) : double.NegativeInfinity;
            Noise.Configure(NoiseEvents);
            HasSkinEffects = _changes.Any(x => x.Command == Kind.Color || x.Command == Kind.Alpha || x.Command == Kind.Size || x.Command == Kind.Angle);
            _hasFake = _changes.Any(x => x.Command == Kind.Fake);
            pvRotation = new AlphaEffectTrack(_changes.Where(x => x.Command == Kind.Effect && x.Effect.effect == "Rotate").Select(x => x.Effect.ForPV()).ToList(), true);
            foreach (var group in _changes.Where(x => x.Command == Kind.Effect).GroupBy(x => x.Effect.effect))
                _effects[group.Key] = new AlphaEffectTrack(group.Select(x => x.Effect).ToList(),
                    group.Key == "Flash" || group.Key == "Vignette" || group.Key == "Zoom" || group.Key == "Hue" || group.Key == "Tint" || group.Key == "Move" || group.Key == "Rotate" || group.Key == "Shake" || AlphaExtraFilters.Find(group.Key) != null);
            foreach (var group in _changes.Where(x => x.Command != Kind.SV && x.Command != Kind.Effect && x.Command != Kind.Display && x.Command != Kind.Noise)
                         .GroupBy(x => Key(x.Command, x.Type, x.Live)))
                _states[group.Key] = new StateTrack(group.ToArray());
            kaleidoscopeVisibility = new AlphaEffectTrack(_changes.Where(x=>x.Command==Kind.Effect && x.Effect.effect=="Kaleidoscope").Select(x=> {
                var ev=x.Effect; return new ScreenEffectEvent {time=ev.time,intensity=1,duration=ev.duration,stateful=ev.stateful,enabled=ev.enabled,transition=ev.transition,attack=ev.attack,holdTime=ev.holdTime,release=ev.release};
            }).ToList(),true);
            var sv = _changes.Where(x => x.Command == Kind.SV).ToArray();
            if (sv.Length != 0)
            {
                foreach (var type in sv.Select(x => x.Type).Append("").Distinct())
                    _scroll[type] = BuildSvCurve(sv, type, false);
                if (sv.Any(x => x.Type == "slide")) _scroll["$slide"] = BuildSvCurve(sv, "slide", true);
                foreach (var curve in _scroll.Values)
                    if (curve.Any(x => x.Multiplier != 1 || x.Slope != 0)) _modifiedCurves.Add(curve);
            }
            NoiseEvents = _changes.Where(c => c.Command == Kind.Noise).Select(c => {
                var context=c.NoiseSource ?? this;
                var hs = context.HSpeed("blockarea", c.Time, c.NoiseHS, false, false, false);
                context.PrepareMotion(c.Time, (float)(c.Time - AlphaNoiseParser.LeadTime(hs)), "blockarea", false, false, false, hs, 1);
                var noiseCurve=context.MotionCurve("blockarea",c.Time,false,false,false);
                var motion = context.NoiseMotionDuration(c);
                var scroll = AlphaVisualTiming.GetCumulativeScroll(noiseCurve,c.Time);
                var lower = AlphaVisualTiming.FindLastTimeAtCumulativeScroll(noiseCurve,c.Time,scroll-motion);
                var upper = AlphaVisualTiming.FindLastTimeAtCumulativeScroll(noiseCurve,c.Time,scroll+motion);
                if(lower>c.Time) lower=double.NaN;
                if(upper>c.Time) upper=double.NaN;
                var appeared = double.IsNaN(lower) ? upper : double.IsNaN(upper) ? lower : Math.Max(lower,upper);
                return new NoiseZoneEvent(c.Sensor,c.Time,c.Value,motion==0 || double.IsNaN(appeared) ? 0 : Math.Max(0,c.Time-appeared));
            }).ToArray();
            Noise.Configure(NoiseEvents);
        }

        float NoiseMotionDuration(Change change) {
            if (HasMotion("blockarea", change.Time, false, false, false)) return AlphaNoiseParser.LeadTime(1) * change.NoiseTimeScale;
            float hs=HSpeed("blockarea",change.Time,change.NoiseHS,false,false,false);
            return Math.Abs(hs)<=AlphaVisualTiming.Epsilon ? 0 : AlphaNoiseParser.LeadTime(hs)*change.NoiseTimeScale;
        }
        public float NoiseWarningProgress(NoiseZoneEvent zone,double now) {
            if(now>=zone.Start) return -1;
            noiseChanges.TryGetValue((zone.Sensor, zone.Start), out var change);
            if(change==null) return -1;
            if(change.NoiseSource!=null) return change.NoiseSource.NoiseWarningProgress(zone,now);
            var duration=NoiseMotionDuration(change);
            if(duration==0) return 1;
            var curve = MotionCurve("blockarea", zone.Start, false, false, false);
            var distance=Math.Abs(AlphaVisualTiming.GetCumulativeScroll(curve,now)-AlphaVisualTiming.GetCumulativeScroll(curve,zone.Start));
            return distance>=duration ? -1 : (float)(1-distance/duration);
        }
        internal AlphaVisualTimeline ForNoise(NoiseZoneEvent zone) =>
            noiseChanges.TryGetValue((zone.Sensor, zone.Start), out var change) ? change.NoiseSource ?? this : this;

        internal AlphaVisualTimeline ClampNoise(double start, double end)
        {
            var result = new AlphaVisualTimeline(_changes.Where(c => c.Command != Kind.Noise ||
                c.Time < end && (c.Time >= start || c.Time + c.Value > start)).Select(c => c.Command != Kind.Noise ? c :
                new Change { Command = Kind.Noise, Position = c.Position, Sensor = c.Sensor,
                    Time = Math.Max(start, c.Time), ValueY = c.ValueY, NoiseHS=c.NoiseHS, NoiseTimeScale=c.NoiseTimeScale,
                    NoiseSource=c.NoiseSource?.ClampNoise(start,end),
                    Value = (float)(Math.Min(end, c.Time + c.Value) - Math.Max(start, c.Time)) }));
            foreach (var pair in NoteOverrides) result.NoteOverrides[pair.Key] = pair.Value;
            foreach (var pair in NoteContexts) result.NoteContexts[pair.Key] = pair.Value;
            return result;
        }

        public AlphaVisualTimeline TransformTime(double scale, double offset) {
            var transformed = new AlphaVisualTimeline(_changes.Select(x => new Change
        {
            Command = x.Command, Time = x.Time * scale + offset, Position = x.Position,
            Type = x.Type, Live = x.Live, Reset = x.Reset, NoiseHS=x.NoiseHS, NoiseTimeScale=x.NoiseTimeScale*(float)scale, NoiseSource=x.NoiseSource?.TransformTime(scale,offset), Value = x.Command is Kind.Bounce or Kind.Noise ? x.Value * (float)scale : x.Value, ValueY = x.Command==Kind.Noise ? x.ValueY*(float)scale : x.ValueY, Sensor=x.Sensor, RGB = x.RGB,
            Transition = ScaleTransition(x.Transition, scale), Token = x.Token, Effect = x.Effect?.Transform(scale, offset), Display = x.Display?.Transform(scale, offset)
        }));
            foreach (var pair in NoteOverrides) transformed.NoteOverrides[pair.Key] = pair.Value.TransformTime(scale, offset);
            foreach (var pair in NoteContexts) transformed.NoteContexts[pair.Key] = (pair.Value.Bpm / scale, pair.Value.HS, pair.Value.Each);
            return transformed;
        }

        ScrollPoint[] Curve(string type, bool each, bool brk, bool mine)
        {
            if (mine && _scroll.TryGetValue("mine", out var curve)) return curve;
            if (brk && _scroll.TryGetValue("break", out curve)) return curve;
            if (each && _scroll.TryGetValue("each", out curve)) return curve;
            if (_scroll.TryGetValue(type, out curve)) return curve;
            return _scroll.TryGetValue("", out curve) ? curve : Array.Empty<ScrollPoint>();
        }
        public float VisualTime(double now, double target, string type, bool each, bool brk, bool mine, double noteTime = double.NaN)
        {
            var curve = MotionCurve(type, double.IsNaN(noteTime) ? target : noteTime, each, brk, mine);
            return (float)(AlphaVisualTiming.GetCumulativeScroll(curve, now) - AlphaVisualTiming.GetCumulativeScroll(curve, target));
        }
        public bool HasSVFor(string type, bool each, bool brk, bool mine) =>
            _modifiedCurves.Contains(Curve(type, each, brk, mine));
        public float Radius(bool spawn, double noteTime, string type, bool each, bool brk, bool mine) =>
            Scalar(spawn ? Kind.Spawn : Kind.Destroy, type, noteTime, noteTime, each, brk, mine, spawn ? 1.225f : 4.8f);
        public bool HasRingEffects(string type, double noteTime, bool each, bool brk, bool mine) =>
            Radius(true, noteTime, type, each, brk, mine) != 1.225f || Radius(false, noteTime, type, each, brk, mine) != 4.8f;
        public float ScreenEffect(string name, float time, out ScreenEffectEvent dominant)
        {
            dominant = null;
            return _effects.TryGetValue(name, out var track) ? track.Evaluate(time, out dominant) : 0;
        }
        public float KaleidoscopeVisibility(float time) => kaleidoscopeVisibility.Evaluate(time);
        private AlphaEffectTrack pvRotation;
        public float RotationForPV(float time) => pvRotation == null ? 0 : pvRotation.Evaluate(time);
        public void ScreenMove(float time, out float x, out float y)
        {
            x = y = 0;
            if (_effects.TryGetValue("Move", out var track)) track.EvaluateVector(time, out x, out y);
        }
        public float AppearTime(float target, float original, float approach, string type, bool each, bool brk, bool mine, float speed = 0, float hs = 1, float baseSpeed = 0)
        {
            original = PrepareMotion(target, original, type, each, brk, mine, speed, baseSpeed);
            RememberAppearance(type, target, each, brk, mine, original);
            var bounce = BounceDuration(type, target, each, brk, mine);
            if (bounce > 0) original = Math.Min(original, (float)BounceStart(target, bounce, hs, type, each, brk, mine));
            if (HasMotion(type, target, each, brk, mine)) return original;
            if (!HasSVFor(type, each, brk, mine) && !HasRingEffects(type, target, each, brk, mine)) return original;
            if (Math.Abs(speed) > 0.0001f)
                approach += Math.Max(0, Math.Abs(Radius(false, target, type, each, brk, mine) - Radius(true, target, type, each, brk, mine)) - 3.575f) / Math.Abs(speed);
            var curve = MotionCurve(type, target, each, brk, mine);
            var history = Math.Min(-5, curve.Length == 0 ? 0 : curve[0].Time - approach);
            var threshold = AlphaVisualTiming.GetCumulativeScroll(curve, target) - approach;
            var first = AlphaVisualTiming.FindFirstTimeAtCumulativeScroll(curve, history, target, threshold, true);
            return float.IsNaN((float)first) ? original : Math.Min(original, (float)first);
        }
        public float SlideProgress(double now, double start, double length)
        {
            if (!_scroll.TryGetValue("$slide", out var curve))
                return length <= 0 ? 1 : Clamp((float)((now - start) / length));
            return AlphaVisualTiming.GetScrollProgress(curve, start, length, now);
        }
        public bool HasSlideSV => _scroll.ContainsKey("$slide");

        public float HSpeed(string type, double time, float fallback, bool each, bool brk, bool mine) =>
            State(Kind.HS, type, time, time, each, brk, mine) is { } state ? state.Transition?.from ?? state.Value : fallback;
        public bool HasHSFor(string type, double time, bool each, bool brk, bool mine) =>
            State(Kind.HS, type, time, time, each, brk, mine) != null;
        public float BounceDuration(string type, double time, bool each, bool brk, bool mine) =>
            State(Kind.Bounce, type, time, time, each, brk, mine)?.Value ?? 0;
        public SpawnVisualMode SpawnMode(string type, double time, bool each, bool brk, bool mine) =>
            (State(Kind.SpawnMode, type, time, time, each, brk, mine)?.Value ?? 0) == 1 ? SpawnVisualMode.Once : SpawnVisualMode.Rewind;
        public float TouchTiming(double now, double target, string type, bool each, bool brk, bool mine, float speed)
        {
            var value = Math.Abs(speed) <= AlphaVisualTiming.Epsilon ? 0 : Math.Sign(speed) * VisualTime(now, target, type, each, brk, mine);
            return now < target ? -Math.Abs(value) : value;
        }
        public double ScrollAt(double time, string type, bool each, bool brk, bool mine) =>
            AlphaVisualTiming.GetCumulativeScroll(Curve(type, each, brk, mine), time);
        public bool CrossedSpawn(ref SpawnCrossingMemo memo, double now, double target, string type, bool each, bool brk, bool mine, float speed, float spawn, float destroy, double noteTime = double.NaN)
        {
            var curve = MotionCurve(type, double.IsNaN(noteTime) ? target : noteTime, each, brk, mine);
            return memo.HasEverCrossed(curve, Math.Min(-5, curve.Length == 0 ? 0 : curve[0].Time), now, AlphaVisualTiming.GetCumulativeScroll(curve,target), speed, spawn, destroy);
        }
        public float BounceDirection(double target, string type, bool each, bool brk, bool mine, float hs)
        {
            var curve = MotionCurve(type, target, each, brk, mine);
            var current = AlphaVisualTiming.GetMultiplier(curve, target);
            if (Math.Abs(current) > AlphaVisualTiming.Epsilon) return Math.Sign(hs * current) < 0 ? -1 : 1;
            var index = curve.Length - 1;
            while (index >= 0 && curve[index].Time > target) index--;
            while (index >= 0 && Math.Abs(curve[index].Multiplier) <= AlphaVisualTiming.Epsilon) index--;
            return Math.Sign(hs * (index < 0 ? 1 : curve[index].Multiplier)) < 0 ? -1 : 1;
        }
        public double BounceStart(double target, float duration, float hs, string type, bool each, bool brk, bool mine)
        {
            if (duration <= 0 || Math.Abs(hs) <= AlphaVisualTiming.Epsilon) return target;
            var curve = MotionCurve(type, target, each, brk, mine);
            var direction = BounceDirection(target, type, each, brk, mine, hs);
            var crossing = AlphaVisualTiming.FindFirstTimeAtCumulativeScroll(curve, curve.Length == 0 ? 0 : curve[0].Time, target,
                ScrollForNote(target, target, type, each, brk, mine) - duration / (hs * direction), hs * direction > 0);
            return double.IsNaN(crossing) ? target : crossing;
        }
        public float BounceProgress(double now, double target, float duration, float hs, string type, bool each, bool brk, bool mine) =>
            duration <= 0 ? 1 : 1 + BounceDirection(target, type, each, brk, mine, hs) * hs *
                (float)(ScrollForNote(Math.Min(now, target), target, type, each, brk, mine) - ScrollForNote(target, target, type, each, brk, mine)) / duration;
        public float BounceRadius(float phase, float direction, float spawn, float destroy)
        {
            var apex = phase * 2 - 1;
            var excursion = (destroy - spawn) * (1 - apex * apex);
            return direction < 0 ? destroy + excursion : destroy - excursion;
        }
        public float BounceHoldBody(double now, double target, double length, float speed, string type, bool each, bool brk, bool mine, float spawn, float destroy) =>
            Math.Min(Math.Abs(destroy - spawn), Math.Max(-Math.Abs(destroy - spawn), Math.Sign(destroy - spawn) * speed *
                (float)(ScrollForNote(target + length, target, type, each, brk, mine) - ScrollForNote(Math.Max(now, target), target, type, each, brk, mine))));

        Change Lookup(Kind kind, string type, bool live, double time)
        {
            return _states.TryGetValue(Key(kind, type, live), out var track) ? track.At((float)time) : null;
        }
        sealed class StateTrack
        {
            readonly Change[] values;
            float sampledTime = float.NaN;
            Change sampled;
            internal StateTrack(Change[] values) { this.values = values; }
            internal Change At(float time)
            {
                if (sampledTime == time) return sampled;
                sampledTime = time;
                var lo = 0;
                var hi = values.Length - 1;
                var found = -1;
                while (lo <= hi)
                {
                    var mid = (lo + hi) / 2;
                    // Match the loader's float timestamps at command boundaries.
                    if ((float)values[mid].Time <= time) { found = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                sampled = found < 0 || values[found].Reset ? null : values[found];
                return sampled;
            }
        }
        Change Resolve(Kind kind, string type, bool live, double time, bool each, bool brk, bool mine)
        {
            if (mine)
            {
                var c = Lookup(kind, "mine", live, time);
                if (c != null || kind == Kind.Color) return c;
            }
            var result = (brk ? Lookup(kind, "break", live, time) : null)
                ?? (each ? Lookup(kind, "each", live, time) : null)
                ?? Lookup(kind, type, live, time);
            if (result == null && kind == Kind.Color && type == "star")
                result = Lookup(kind, "tap", live, time);
            return result;
        }
        Change State(Kind kind, string type, double noteTime, double now, bool each, bool brk, bool mine) =>
            Resolve(kind, type, true, now, each, brk, mine)
            ?? Resolve(kind, type, false, noteTime, each, brk, mine)
            ?? (kind == Kind.Color && mine || kind == Kind.HS && type == "slide" || kind == Kind.Angle && (type is "slide" or "slidestar" or "blockarea") || type == "blockarea" && (kind is Kind.Color or Kind.Alpha or Kind.Size) ? null : Lookup(kind, "", true, now) ?? Lookup(kind, "", false, noteTime));
        public uint? Color(string type, double noteTime, double now, bool each, bool brk, bool mine)
        {
            var state = State(Kind.Color, type, noteTime, now, each, brk, mine);
            if (state == null) return null;
            if (state.Transition == null) return state.RGB;
            var color = state.Transition.EvaluateColor(now - AnimationOrigin(state, type, noteTime, each, brk, mine));
            return (uint)Math.Round(Clamp(color.R) * 255) << 16 |
                (uint)Math.Round(Clamp(color.G) * 255) << 8 | (uint)Math.Round(Clamp(color.B) * 255);
        }
        public float Alpha(string type, double noteTime, double now, bool each, bool brk, bool mine) =>
            Clamp(Scalar(Kind.Alpha, type, noteTime, now, each, brk, mine, 1));
        public float Angle(string type, double noteTime, double now, bool each, bool brk, bool mine) =>
            Scalar(Kind.Angle, type, noteTime, now, each, brk, mine, 0);
        public (float X, float Y) Size(string type, double noteTime, double now, bool each, bool brk, bool mine)
        {
            var state = Resolve(Kind.Size, type, true, now, each, brk, mine)
                ?? (type != "slide" && type != "blockarea" ? Lookup(Kind.Size, "", true, now) : null)
                ?? Resolve(Kind.Size, type, false, noteTime, each, brk, mine)
                ?? (type != "slide" && type != "blockarea" ? Lookup(Kind.Size, "", false, noteTime) : null);
            if (state == null) return (1, 1);
            if (state.Transition == null) return (state.Value == 0 ? 1 : state.Value, state.ValueY == 0 ? 1 : state.ValueY);
            var elapsed = now - AnimationOrigin(state, type, noteTime, each, brk, mine);
            return (state.Transition.Evaluate(elapsed), state.Transition.EvaluateY(elapsed));
        }
        static (Kind, string, bool) Key(Kind kind, string type, bool live) => (kind, type, live);
        static float Clamp(float x) => Math.Max(0, Math.Min(1, x));

        // Unsupported directives are stripped by AlphaSimaiParser.
        internal static bool ParseCommand(string token, int position, ICollection<Change> changes)
        {
            var star = token.IndexOf('*');
            var prefix = (star < 0 ? token : token.Substring(0, star)).Trim().ToUpperInvariant();
            Kind kind;
            var live = prefix.EndsWith("V") && prefix != "SV";
            switch (prefix)
            {
                case "SV": kind = Kind.SV; break;
                case "HS": kind = Kind.HS; break;
                case "BOUNCE": kind = Kind.Bounce; break;
                case "SPAWNMODE": kind = Kind.SpawnMode; break;
                case "COLOR": case "COLORV": kind = Kind.Color; break;
                case "ALPHA": case "ALPHAV": kind = Kind.Alpha; break;
                case "SIZE": case "SIZEV": kind = Kind.Size; break;
                case "ANGLE": case "ANGLEV": kind = Kind.Angle; break;
                case "SPAWN": kind = Kind.Spawn; break;
                case "DESTROY": kind = Kind.Destroy; break;
                case "FAKE": kind = Kind.Fake; break;
                default:
                    if (AlphaDisplayParser.Supports(prefix))
                    {
                        changes.Add(new Change { Command = Kind.Display, Position = position, Token = token });
                        return true;
                    }
                    if (!AlphaCommandGrammar.TryReadCommand(token, out var descriptor) || descriptor.kind != AlphaCommandKind.Effect) return false;
                    changes.Add(new Change { Command = Kind.Effect, Position = position, Token = token });
                    return true;
            }
            if (star < 0) throw new FormatException($"{prefix} 缺少 * 参数（位置 {position + 1}）");
            var body = token.Substring(star + 1).Trim();
            if ((kind == Kind.Spawn || kind == Kind.Destroy || kind == Kind.Fake || kind == Kind.HS || kind == Kind.Bounce || kind == Kind.SpawnMode) && !AlphaCommandGrammar.TryValidate(token, 120, out var error))
                throw new FormatException(error);
            if (kind == Kind.SpawnMode && body.StartsWith("(")) body = body.Substring(1, body.Length - 2);
            if (!AlphaCommandGrammar.TrySplitValues(body, out var parts)) throw new FormatException("参数括号不完整");
            foreach (var part in parts)
            {
                var equals = part.IndexOf('=');
                var type = equals < 0 ? "" : AlphaCommandGrammar.NormalizeTarget(part.Substring(0, equals));
                var value = (equals < 0 ? part : part.Substring(equals + 1)).Trim();
                value = UnwrapScalar(value);
                if (equals >= 0 && (!Types.Contains(type) || kind == Kind.SV && type == "slidestar" ||
                    type == "blockarea" && !(kind is Kind.SV or Kind.HS or Kind.Color or Kind.Size or Kind.Alpha)))
                    throw new FormatException($"{prefix} 未知音符类型：{type}（位置 {position + 1}）");
                if (body.Contains("=") && equals < 0) throw new FormatException($"{prefix} 类型参数必须为 类型=值");
                var c = new Change { Command = kind, Type = type, Live = live, Position = position, Reset = value.Equals("NULL", StringComparison.OrdinalIgnoreCase) };
                if (!c.Reset)
                {
                    if (kind is not (Kind.Bounce or Kind.SpawnMode or Kind.Fake) &&
                        NoteValueTransition.TryParse(value, 120, ValueSpec(kind), out var transition))
                    {
                        c.Transition = transition; c.Token = value;
                        c.Value = transition.from; c.ValueY = transition.fromY;
                        changes.Add(c); continue;
                    }
                    if (kind == Kind.Bounce)
                    {
                        c.Token = value;
                        c.Reset = value.Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (kind == Kind.SpawnMode) c.Value = value.Equals("Once", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    else if (kind == Kind.Fake)
                    {
                        c.Value = value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1" ? 1 : 0;
                    }
                    else if (kind == Kind.Size)
                    {
                        if (!AlphaCommandGrammar.TryParseScalePair(value, out c.Value, out c.ValueY))
                        {
                            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out c.Value) || float.IsNaN(c.Value) || float.IsInfinity(c.Value)) throw new FormatException("SIZE 参数应为数值或 (x,y)");
                            c.ValueY = c.Value;
                        }
                    }
                    else if (kind == Kind.Color)
                    {
                        value = value.TrimStart('#');
                        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c.RGB))
                            throw new FormatException($"{prefix} 颜色应为六位十六进制：{value}");
                    }
                    else
                    {
                        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out c.Value) || float.IsNaN(c.Value) || float.IsInfinity(c.Value))
                            throw new FormatException($"{prefix} 参数应为有限数值：{value}");
                        if (kind == Kind.Alpha) c.Value = Clamp(c.Value);
                    }
                }
                changes.Add(c);
            }
            return true;
        }
    }
}
