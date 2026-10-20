using System;
using System.Globalization;

namespace MajdataCore
{
    // Values are parsed once. Playback evaluates only this numeric curve.
    public sealed class NoteValueTransition
    {
        public float from;
        public float to;
        public float fromY;
        public float toY;
        // Colour curves use the scalar, Y and Z channels for R, G and B.
        public float fromZ;
        public float toZ;
        public float duration;
        public string easing = "linear";
        public float parameter = 2f;

        public float Progress(double elapsed)
        {
            var t = duration <= 0f ? 1d : Math.Max(0d, Math.Min(1d, elapsed / duration));
            return easing switch
            {
                "easein" => (float)Math.Pow(t, parameter),
                "easeout" => (float)(1d - Math.Pow(1d - t, parameter)),
                "easeinout" => (float)(t < .5d
                    ? .5d * Math.Pow(2d * t, parameter)
                    : 1d - .5d * Math.Pow(2d * (1d - t), parameter)),
                "sigmoid" => (float)((Logistic(t, parameter) - Logistic(0d, parameter)) /
                    (Logistic(1d, parameter) - Logistic(0d, parameter))),
                _ => (float)t
            };
        }

        private static double Logistic(double t, float steepness) =>
            1d / (1d + Math.Exp(-steepness * (t - .5d)));

        public float Evaluate(double elapsed) => from + (to - from) * Progress(elapsed);
        public float EvaluateY(double elapsed) => fromY + (toY - fromY) * Progress(elapsed);
        public (float R,float G,float B) EvaluateColor(double elapsed)
        {
            var progress=Progress(elapsed);
            return (from+(to-from)*progress, fromY+(toY-fromY)*progress, fromZ+(toZ-fromZ)*progress);
        }

        public static bool TryParse(string text, float bpm, AlphaValueSpec spec,
            out NoteValueTransition transition)
        {
            transition = null;
            if (text.Length < 2 || text[0] != '(' || text[text.Length - 1] != ')' ||
                !AlphaCommandGrammar.TrySplitValues(text.Substring(1, text.Length - 2), out var args) ||
                args.Count < 4 || args.Count > 5)
                return false;
            var easing = args[3].ToLowerInvariant();
            if (easing != "linear" && easing != "easein" && easing != "easeout" &&
                easing != "easeinout" && easing != "sigmoid") return false;
            if (!AlphaCommandGrammar.TryParseDuration(args[2], bpm, out var duration) || duration < 0f ||
                !spec.Matches(args[0], bpm) || !spec.Matches(args[1], bpm) ||
                spec.IsReset(args[0]) || spec.IsReset(args[1])) return false;
            var parameter = easing == "sigmoid" ? 10f : 2f;
            if (args.Count == 5 &&
                (easing == "linear" || !float.TryParse(args[4], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parameter) ||
                 float.IsNaN(parameter) || parameter < .1f || parameter > 100f)) return false;
            if (!TryValue(args[0], bpm, spec.kind, out var from, out var fromY, out var fromZ) ||
                !TryValue(args[1], bpm, spec.kind, out var to, out var toY, out var toZ)) return false;
            transition = new NoteValueTransition
            {
                from = from, to = to, fromY = fromY, toY = toY, fromZ=fromZ, toZ=toZ,
                duration = duration, easing = easing, parameter = parameter
            };
            return true;
        }

        private static bool TryValue(string text, float bpm, AlphaValueKind kind,
            out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (kind == AlphaValueKind.Color)
            {
                var value=AlphaCommandGrammar.Unwrap(text).TrimStart('#');
                if (value.Length!=6 || !uint.TryParse(value,NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,out var rgb)) return false;
                x=((rgb>>16)&255)/255f; y=((rgb>>8)&255)/255f; z=(rgb&255)/255f;
                return true;
            }
            if (kind == AlphaValueKind.Scale && AlphaCommandGrammar.TryParseScalePair(text, out x, out y))
                return true;
            var valid = kind == AlphaValueKind.Duration
                ? AlphaCommandGrammar.TryParseDuration(text, bpm, out x)
                : float.TryParse(AlphaCommandGrammar.Unwrap(text), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out x);
            y = x;
            return valid && !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
