using System;
using System.Globalization;
using System.Linq;
using MajdataCore;
namespace MajdataPlay.Alpha
{
    internal static class AlphaScreenEffectParser
    {
        public static ScreenEffectEvent Parse(string token, double time, float bpm)
        {
            if(!AlphaCommandGrammar.TryValidate(token,bpm,out var error)) throw new FormatException(error);
            if(!TryParseScreenEffect(token,time,bpm,out var change)) throw new FormatException("未知滤镜："+token);
            return change;
        }
        static bool TryReadCommand(string token, AlphaCommandKind kind, out AlphaCommandDescriptor descriptor, out string body)
        {
            AlphaCommandGrammar.TrySplitToken(token,out _,out body);
            return AlphaCommandGrammar.TryReadCommand(token,out descriptor) && descriptor.kind==kind;
        }
        static bool TryParseCommandDuration(string text,float bpm,out float seconds) => AlphaCommandGrammar.TryParseDuration(text,bpm,out seconds);
        static bool TryParseFinite(string text,out float value) => float.TryParse(text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out value) && !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool TryParseScreenEffect(string token, double time, float bpm, out ScreenEffectEvent change)
    {
        change = new ScreenEffectEvent();
        if (!TryReadCommand(token, AlphaCommandKind.Effect, out var descriptor, out var body))
            return false;

        var effect = descriptor.Canonical;
        // FADE is FLASH aimed at black, so its strength is applied negatively.
        var negated = descriptor.negatesIntensity;
        if (body.Length < 3 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body.Substring(1, body.Length - 2).Split(',');
        var rotatePV = false;
        if (effect == "Rotate" && values.Length > 1 && bool.TryParse(values[^1].Trim(), out rotatePV))
            Array.Resize(ref values, values.Length - 1);
        var gradient = effect == "Vignette";
        if (effect == "Vignette" && values.Length >= 3 && bool.TryParse(values[^1].Trim(), out var requestedGradient)) {
            gradient=requestedGradient; Array.Resize(ref values, values.Length - 1);
        }
        float F(int i) => TryParseFinite(values[i], out var v) ? v : float.NaN;
        float D(int i) => TryParseCommandDuration(values[i], bpm, out var v) ? v : float.NaN;

        if (values.Length >= 1 &&
            string.Equals(values[0].Trim(), "Instant", StringComparison.OrdinalIgnoreCase))
        {
            var instant = new ScreenEffectEvent
            {
                time = time,
                effect = effect,
                gradient = gradient,
                rotatePV = rotatePV,
                attack = 0f,
                release = 0f
            };
            var durationIndex = -1;
            switch (effect)
            {
                case "Move":
                    if (values.Length != 4)
                        return true;
                    instant.intensity = 1f;
                    instant.paramA = F(1);
                    instant.paramB = F(2);
                    durationIndex = 3;
                    break;
                case "Tint":
                    if (values.Length != 4)
                        return true;
                    instant.color = values[1].Trim().TrimStart('#').ToUpperInvariant();
                    instant.intensity = F(2);
                    durationIndex = 3;
                    if (instant.color.Length != 6 || !instant.color.All(Uri.IsHexDigit))
                        return true;
                    break;
                case "Shake":
                    if (values.Length is < 4 or > 5)
                        return true;
                    instant.intensity = F(1);
                    instant.paramA = F(2);
                    if (values.Length == 5)
                    {
                        instant.hasDirection = true;
                        instant.paramB = F(3) * (float)Math.PI / 180f;
                    }
                    durationIndex = values.Length - 1;
                    break;
                default:
                    if (values.Length != 3)
                        return true;
                    instant.intensity = F(1);
                    if (effect == "Zoom")
                        instant.intensity -= 1f;
                    if (negated)
                        instant.intensity = -Math.Abs(instant.intensity);
                    durationIndex = 2;
                    break;
            }

            var duration = D(durationIndex);
            if (float.IsNaN(instant.intensity) || float.IsNaN(instant.paramA) ||
                float.IsNaN(instant.paramB) || float.IsNaN(duration))
                return true;
            instant.holdTime = Math.Max(0f, duration);
            instant.duration = instant.holdTime;
            change = instant;
            return true;
        }

        if (values.Length >= 1 && bool.TryParse(values[0].Trim(), out var enabled))
        {
            var state = new ScreenEffectEvent
            {
                time = time,
                effect = effect,
                gradient = gradient,
                rotatePV = rotatePV,
                stateful = true,
                enabled = enabled
            };

            if (!enabled)
            {
                if (values.Length > 2)
                    return true;
                var transition = values.Length == 2 ? D(1) : 0f;
                if (float.IsNaN(transition))
                    return true;
                state.transition = Math.Max(0f, transition);
                state.duration = state.transition;
                change = state;
                return true;
            }

            var transitionIndex = -1;
            switch (effect)
            {
                case "Move":
                {
                    if (values.Length is < 3 or > 4)
                        return true;
                    var dx = F(1);
                    var dy = F(2);
                    if (float.IsNaN(dx) || float.IsNaN(dy))
                        return true;
                    state.intensity = 1f;
                    state.paramA = dx;
                    state.paramB = dy;
                    transitionIndex = values.Length >= 4 ? 3 : -1;
                    break;
                }
                case "Tint":
                {
                    if (values.Length is < 3 or > 4)
                        return true;
                    var hex = values[1].Trim().TrimStart('#');
                    var amount = F(2);
                    if (float.IsNaN(amount) || hex.Length != 6 || !hex.All(Uri.IsHexDigit))
                        return true;
                    state.intensity = amount;
                    state.color = hex.ToUpperInvariant();
                    transitionIndex = values.Length >= 4 ? 3 : -1;
                    break;
                }
                case "Shake":
                {
                    if (values.Length is < 3 or > 5)
                        return true;
                    var strength = F(1);
                    var frequency = F(2);
                    if (float.IsNaN(strength) || float.IsNaN(frequency) || frequency <= 0f)
                        return true;
                    state.intensity = strength;
                    state.paramA = frequency;
                    if (values.Length >= 4 && !string.IsNullOrWhiteSpace(values[3]))
                    {
                        var angle = F(3);
                        if (float.IsNaN(angle))
                            return true;
                        state.hasDirection = true;
                        state.paramB = angle * (float)Math.PI / 180f;
                    }
                    if (values.Length == 5)
                    {
                        transitionIndex = 4;
                    }
                    break;
                }
                default:
                {
                    if (values.Length is < 2 or > 3)
                        return true;
                    var intensity = F(1);
                    if (float.IsNaN(intensity))
                        return true;
                    state.intensity = negated ? -Math.Abs(intensity) : intensity;
                    if (effect == "Zoom")
                        state.intensity -= 1f;
                    transitionIndex = values.Length >= 3 ? 2 : -1;
                    break;
                }
            }

            if (transitionIndex >= 0)
            {
                var transition = D(transitionIndex);
                if (float.IsNaN(transition))
                    return true;
                state.transition = Math.Max(0f, transition);
                state.duration = state.transition;
            }

            change = state;
            return true;
        }

        if (values.Length == 2)
        {
            var duration = D(0);
            var intensity = F(1);
            if (float.IsNaN(duration) || float.IsNaN(intensity))
                return true;

            change = new ScreenEffectEvent
            {
                time = time,
                effect = effect,
                gradient = gradient,
                rotatePV = rotatePV,
                duration = Math.Max(0f, duration),
                intensity = negated ? -Math.Abs(intensity) : intensity
            };
            if (effect == "Tint")
                change.color = "FFFFFF";
            else if (effect == "Move")
            {
                change.intensity = 1f;
                change.paramA = intensity;
                change.paramB = 0f;
            }
            return true;
        }

        // The oldest form is (attack,hold,release,strength) plus the one extra value
        // some effects take. Anything past that used to be dropped without a word.
        var envelopeMaximum = effect is "Move" or "Tint" or "Shake" ? 5 : 4;
        if (values.Length < 4 || values.Length > envelopeMaximum)
            return true;
        var attack = D(0);
        var holdFor = D(1);
        var release = D(2);
        if (float.IsNaN(attack) || float.IsNaN(holdFor) || float.IsNaN(release))
            return true;
        attack = Math.Max(0f, attack);
        holdFor = Math.Max(0f, holdFor);
        release = Math.Max(0f, release);

        var v2 = new ScreenEffectEvent
        {
            time = time,
            effect = effect,
            gradient = gradient,
            attack = attack,
            rotatePV = rotatePV,
            holdTime = holdFor,
            release = release,
            duration = attack + holdFor + release
        };

        switch (effect)
        {
            case "Move":
            {
                var dx = F(3);
                var dy = values.Length >= 5 ? F(4) : 0f;
                if (float.IsNaN(dx) || float.IsNaN(dy))
                    return true;
                v2.intensity = 1f;
                v2.paramA = dx;
                v2.paramB = dy;
                break;
            }
            case "Tint":
            {
                var amount = F(3);
                if (float.IsNaN(amount))
                    return true;
                var hex = values.Length >= 5 ? values[4].Trim().TrimStart('#') : "FFFFFF";
                if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
                    return true;
                v2.intensity = amount;
                v2.color = hex.ToUpperInvariant();
                break;
            }
            case "Shake":
            {
                var strength = F(3);
                if (float.IsNaN(strength))
                    return true;
                v2.intensity = strength;
                var freq = values.Length >= 5 ? F(4) : 18f;
                v2.paramA = float.IsNaN(freq) || freq <= 0f ? 18f : freq;
                break;
            }
            default:
            {
                var intensity = F(3);
                if (float.IsNaN(intensity))
                    return true;
                v2.intensity = negated ? -Math.Abs(intensity) : intensity;
                break;
            }
        }

        change = v2;
        return true;
    }

    }
}
