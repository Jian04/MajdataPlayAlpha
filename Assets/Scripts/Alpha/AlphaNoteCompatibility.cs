using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MajSimai;
using MajdataPlay.Alpha;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Utils;

namespace MajdataPlay.Scenes.Game
{
    public partial class NoteLoader
    {
        internal static SimaiChart FilterUnsupportedNotes(SimaiChart chart)
        {
            var timings = new List<SimaiTimingPoint>();
            var warnings = new List<AlphaParseWarning>();
            foreach (var timing in chart.NoteTimings)
            {
                var notes = new List<SimaiNote>();
                foreach (var note in timing.Notes)
                {
                    try
                    {
                        var decoration = AlphaNoteDecoration.Read(note.RawContent);
                        if (decoration?.trajectory != null)
                        {
                            if (AlphaTrajectoryPathGeometry.TryBuild(new AlphaRouteSource(), decoration.trajectory.segments, new List<UnityEngine.Vector3>()) == false)
                                throw new FormatException("Borrowed trajectory could not be built");
                        }
                        else if (note.Type == SimaiNoteType.Slide) ValidateSlideGeometry(note.RawContent);
                        notes.Add(note);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is FormatException ||
                        ex is IndexOutOfRangeException || ex is KeyNotFoundException || ex.GetType() == typeof(Exception))
                    {
                        warnings.Add(new AlphaParseWarning(chart.Fumen,
                            Math.Max(0, timing.RawTextPosition - timing.RawContent.Length),
                            note.RawContent, "当前版本不支持此轨迹，已跳过"));
                    }
                }
                timings.Add(new SimaiTimingPoint(timing.Timing, notes.ToArray(), timing.RawContent,
                    timing.RawTextPositionX, timing.RawTextPositionY, timing.Bpm, timing.HSpeed,
                    timing.SVeloc, timing.RawTextPosition, timing.SignatureNumerator, timing.SignatureDenominator));
            }
            if (warnings.Count == 0) return chart;
            var filtered = new SimaiChart(chart.Level, chart.Designer, chart.Fumen, timings.ToArray(), chart.CommaTimings);
            AlphaSimaiParser.CopyTimeline(chart, filtered);
            AlphaSimaiParser.AddWarnings(filtered, warnings);
            return filtered;
        }
        static void ValidateSlideGeometry(string raw)
        {
            if (AlphaPathParser.TryRead(raw, out var alphaPath))
            {
                if (AlphaPathGeometry.Build(alphaPath.path).Count < 2) throw new FormatException("Empty Alpha path");
                return;
            }
            raw = NormalizeSlideContent(raw);
            var endFlag = raw.IndexOf('K');
            if (endFlag >= 0)
            {
                if (endFlag + 1 >= raw.Length) throw new FormatException("Missing SlideCode end");
                var path = SlideCodeParser.Parse(raw.Substring(0, endFlag + 2));
                ExtendSlideHelper.CreateSlideEntry(path);
                return;
            }
            var route = Regex.Replace(raw, @"\[[^\]]*\]", "");
            route = Regex.Replace(route, @"[bxmf!@$?c]", "");
            var segments = Regex.Matches(route, @"(?<start>[1-8])?(?<shape>pp|qq|[-<>^vpqszw]|V)(?<end>[1-8]{1,2})");
            var start = raw[0];
            var consumed = 0;
            foreach (Match segment in segments)
            {
                if (segment.Index != consumed) throw new FormatException("Unknown slide section");
                var op = segment.Groups["shape"].Value;
                var end = segment.Groups["end"].Value;
                if (end.Length != (op == "V" ? 2 : 1)) throw new FormatException("Unknown slide endpoint");
                if (segment.Groups["start"].Success) start = segment.Groups["start"].Value[0];
                var shape = NoteCreateHelper.DetectShapeFromText(start + op + end).TrimStart('-');
                if (!SLIDE_PREFAB_MAP.ContainsKey(shape)) throw new FormatException("Unsupported slide shape");
                start = end[end.Length - 1];
                consumed += segment.Length;
            }
            if (consumed != route.Length || segments.Count == 0) throw new FormatException("Unknown slide route");
            if (segments.Count > 1 && route.Contains("w")) throw new FormatException("Unsupported connected WiFi");
            // Match the native chain's timing rules before it creates any scene objects.
            var timedRoute = Regex.Replace(raw, @"[bxmf!@$?c]", "");
            var timedSegments = Regex.Matches(timedRoute,
                @"[1-8]?(?:pp|qq|[-<>^vpqszw]|V)[1-8]{1,2}(?<time>\[[^\]]*\])?");
            var timingMode = 0;
            foreach (Match segment in timedSegments)
            {
                if (segment.Groups["time"].Success)
                {
                    if (timingMode == 3) throw new FormatException("Invalid chain timing");
                    timingMode = timingMode == 1 ? 3 : 2;
                }
                else
                {
                    if (timingMode >= 2) throw new FormatException("Missing chain section timing");
                    timingMode = 1;
                }
            }
            if (timingMode < 2) throw new FormatException("Missing slide timing");
        }
        internal static string NormalizeSlideContent(string raw) => AlphaNoteDecoration.Strip(raw).Replace("f", "");
    }
}
