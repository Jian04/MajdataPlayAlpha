using System;
using System.Collections.Generic;
namespace MajdataPlay.Alpha
{
    internal sealed class AlphaEffectTrack
    {
        static float Clamp01(float value) => Math.Max(0,Math.Min(1,value));
        static float Lerp(float from,float to,float amount) => from+(to-from)*Clamp01(amount);
        private readonly List<ScreenEffectEvent> allEvents;
        private readonly List<ScreenEffectEvent> legacyEvents = new();
        private readonly List<StateSegment> stateSegments = new();
        private readonly bool triangularEnvelope;
        private int cursor;
        private int stateCursor;
        private int upcomingCursor;
        private float lastTime = float.MinValue;
        private float lastUpcomingTime = float.MinValue;

        public AlphaEffectTrack(List<ScreenEffectEvent> events, bool triangularEnvelope)
        {
            allEvents = events;
            this.triangularEnvelope = triangularEnvelope;
            if (events == null)
                return;

            ScreenEffectEvent activeSource = null;
            foreach (var item in events)
            {
                if (!item.stateful)
                {
                    legacyEvents.Add(item);
                    continue;
                }

                var startValue = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ValueAt((float)item.time);
                var source = item.enabled
                    ? item
                    : activeSource ?? (stateSegments.Count > 0 ? stateSegments[^1].Source : null);
                var startParamA = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ParamAAt((float)item.time);
                var startParamB = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ParamBAt((float)item.time);
                stateSegments.Add(new StateSegment(item, startValue,
                    item.enabled ? item.intensity : 0f, source,
                    startParamA, startParamB,
                    item.enabled ? item.paramA : 0f,
                    item.enabled ? item.paramB : 0f));
                activeSource = item.enabled ? item : null;
            }
        }

        public float Evaluate(float time) => Evaluate(time, out _);

        public float Evaluate(float time, out ScreenEffectEvent dominant)
        {
            dominant = null;
            if ((allEvents == null || allEvents.Count == 0))
                return 0f;

            if (time < lastTime)
            {
                cursor = 0;
                stateCursor = 0;
            }
            lastTime = time;

            while (cursor < legacyEvents.Count &&
                   legacyEvents[cursor].time + legacyEvents[cursor].duration < time)
                cursor++;

            var result = 0f;
            for (var i = cursor; i < legacyEvents.Count; i++)
            {
                var item = legacyEvents[i];
                if (item.time > time)
                    break;
                // Events are ordered by start, not end. An older long effect
                // can keep the cursor before an already expired short effect.
                if (time > item.time + item.duration)
                    continue;

                var envelope = MajdataCore.EffectEnvelope.Evaluate(
                    time - (float)item.time, item.duration, item.attack,
                    item.holdTime, item.release, triangularEnvelope);
                var value = item.intensity * envelope;
                if (Math.Abs(value) > Math.Abs(result))
                {
                    result = value;
                    dominant = item;
                }
            }

            while (stateCursor + 1 < stateSegments.Count &&
                   stateSegments[stateCursor + 1].Time <= time)
                stateCursor++;

            if (stateCursor < stateSegments.Count && stateSegments[stateCursor].Time <= time)
            {
                var segment = stateSegments[stateCursor];
                var stateValue = segment.ValueAt(time);
                if (Math.Abs(stateValue) > Math.Abs(result))
                {
                    result = stateValue;
                    dominant = Math.Abs(stateValue) > 0.0001f ? segment.Source : null;
                }
            }
            return result;
        }

        public void EvaluateVector(float time, out float x, out float y)
        {
            var amount = Evaluate(time, out var dominant);
            if (stateCursor < stateSegments.Count && stateSegments[stateCursor].Time <= time)
            {
                var segment = stateSegments[stateCursor];
                x = segment.ParamAAt(time);
                y = segment.ParamBAt(time);
                return;
            }

            x = dominant == null ? 0f : dominant.paramA * amount;
            y = dominant == null ? 0f : dominant.paramB * amount;
        }

        public bool IsUpcoming(float time, float leadTime)
        {
            if (allEvents == null || allEvents.Count == 0)
                return false;

            if (time < lastUpcomingTime)
                upcomingCursor = 0;
            lastUpcomingTime = time;
            while (upcomingCursor < allEvents.Count && allEvents[upcomingCursor].time < time)
                upcomingCursor++;
            if (upcomingCursor >= allEvents.Count)
                return false;

            var delta = (float)allEvents[upcomingCursor].time - time;
            return delta >= 0f && delta <= leadTime;
        }

        private sealed class StateSegment
        {
            private readonly float startValue;
            private readonly float targetValue;
            private readonly float startParamA;
            private readonly float startParamB;
            private readonly float targetParamA;
            private readonly float targetParamB;
            private readonly float transition;

            public StateSegment(ScreenEffectEvent item, float startValue, float targetValue,
                ScreenEffectEvent source, float startParamA, float startParamB,
                float targetParamA, float targetParamB)
            {
                Time = (float)item.time;
                this.startValue = startValue;
                this.targetValue = targetValue;
                this.startParamA = startParamA;
                this.startParamB = startParamB;
                this.targetParamA = targetParamA;
                this.targetParamB = targetParamB;
                transition = Math.Max(0f, item.transition);
                Source = source;
            }

            public float Time { get; }
            public ScreenEffectEvent Source { get; }

            public float ValueAt(float time)
            {
                if (transition <= 0f)
                    return targetValue;
                return Lerp(startValue, targetValue,
                    Clamp01((time - Time) / transition));
            }

            public float ParamAAt(float time) => Interpolate(startParamA, targetParamA, time);
            public float ParamBAt(float time) => Interpolate(startParamB, targetParamB, time);

            private float Interpolate(float start, float target, float time)
            {
                if (transition <= 0f)
                    return target;
                return Lerp(start, target,
                    Clamp01((time - Time) / transition));
            }
        }
    }

}
