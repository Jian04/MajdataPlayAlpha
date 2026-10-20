using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MajSimai;

namespace MajdataPlay.Alpha
{
    internal static class AlphaSimaiParser
    {
        static AlphaSimaiParser()
        {
            SimaiParser.ExtensionNoteParser = AlphaNoteDecoration.Parse;
            SimaiParser.ExtensionSlotParser = AlphaPathParser.ParseSlot;
        }
        static readonly ConditionalWeakTable<SimaiChart, AlphaVisualTimeline> Timelines = new();
        static readonly ConditionalWeakTable<SimaiChart, AlphaParseWarning[]> Warnings = new();
        internal static AlphaVisualTimeline Current { get; private set; } = AlphaVisualTimeline.Empty;

        public static Task<SimaiChart> ParseChartAsync(string level, string designer, string fumen) =>
            Task.Run(() => ParseChart(level, designer, fumen));
        public static SimaiChart ParseChart(string level, string designer, string fumen) =>
            ParseChart(level, designer, fumen, false);
        static SimaiChart ParseChart(string level, string designer, string fumen, bool overlayStream)
        {
            var commands = new List<AlphaVisualTimeline.Change>();
            var warnings = new List<AlphaParseWarning>();
            fumen ??= "";
            var text = (fumen ?? "").ToCharArray();
            var overlayStreams = AlphaStreamSyntax.Prepare(text);
            var comment = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') comment = false;
                if (comment) continue;
                if (text[i] == '|' && i + 1 < text.Length && text[i + 1] == '|') { comment = true; continue; }
                if (text[i]=='X' && AlphaNoiseParser.TryRead(text,i,out var noiseEnd,out var sensor,out var duration))
                {
                    commands.Add(new AlphaVisualTimeline.Change { Command=AlphaVisualTimeline.Kind.Noise, Position=i, Sensor=sensor, Token=duration });
                    AlphaChartCompatibility.Blank(text,i,noiseEnd); i=noiseEnd-1; continue;
                }
                if (text[i] != '<') continue;
                // In a TouchSlide, <B5> or <D5> is two consecutive turns,
                // not a named extension. Sensor destinations are reserved here.
                if (i + 2 < text.Length &&
                    (text[i + 1] == 'C' && text[i + 2] == '>' ||
                     text[i + 1] is 'A' or 'B' or 'D' or 'E' && text[i + 2] is >= '1' and <= '8' &&
                     i + 3 < text.Length && text[i + 3] == '>')) continue;
                // A named <command> is an extension; an ordinary '<' slide is not.
                var nameEnd = i + 1;
                if (nameEnd >= text.Length || !char.IsLetter(text[nameEnd])) continue;
                while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_')) nameEnd++;
                var name = new string(text, i + 1, nameEnd - i - 1).ToUpperInvariant();
                var headerEnd = nameEnd;
                while (headerEnd < text.Length && char.IsWhiteSpace(text[headerEnd])) headerEnd++;
                if (headerEnd >= text.Length || (text[headerEnd] != '*' && text[headerEnd] != '>')) continue;
                // Plain HS remains MajSimai's native control. Typed Alpha HS is
                // separate display state and must not change timing/slide duration.
                if (name == "HS" && !new string(text, headerEnd, Math.Max(0, CommandEnd(text, headerEnd) - headerEnd)).Contains('=') && !new string(text, headerEnd, Math.Max(0, CommandEnd(text, headerEnd) - headerEnd)).Contains(','))
                {
                    var nativeEnd = CommandEnd(text, headerEnd);
                    if (nativeEnd >= 0)
                    {
                        var count = commands.Count;
                        try
                        {
                            // CommaTimings deliberately stores HS=1 upstream. Keep
                            // the global display state for noise, including empty slots.
                            AlphaVisualTimeline.ParseCommand(new string(text, i + 1, nativeEnd - i - 1), i, commands);
                            for (var j = headerEnd + 1; j < nativeEnd; j++)
                                if (text[j] == '(' || text[j] == ')') text[j] = ' ';
                            if (new string(text, headerEnd + 1, nativeEnd - headerEnd - 1).Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
                            {
                                text[headerEnd + 1] = '1';
                                for (var j = headerEnd + 2; j < nativeEnd; j++) text[j] = ' ';
                            }
                        }
                        catch (FormatException) { commands.RemoveRange(count, commands.Count - count); }
                    }
                    continue;
                }
                var supported = name == "SV" || name == "HS" || name == "BOUNCE" || name == "SPAWNMODE" || name == "COLOR" || name == "COLORV" || name == "ALPHA" || name == "ALPHAV" || name == "SIZE" || name == "SIZEV" || name == "ANGLE" || name == "ANGLEV" || AlphaDisplayParser.Supports(name) || name == "SPAWN" || name == "DESTROY" || name == "FAKE" ||
                    MajdataCore.AlphaCommandGrammar.TryFind(name, out var descriptor) && descriptor.kind == MajdataCore.AlphaCommandKind.Effect;
                var end = CommandEnd(text, headerEnd);
                var terminated = end >= 0;
                if (end < 0)
                {
                    // An incomplete unsupported directive may only consume its own slot.
                    end = headerEnd;
                    while (end + 1 < text.Length && text[end + 1] != ',' && text[end + 1] != '\n' && text[end + 1] != '\r') end++;
                }
                if (!terminated) warnings.Add(new AlphaParseWarning(fumen, i, fumen.Substring(i, end - i + 1), "指令缺少 >，已跳过"));
                else if (supported)
                {
                    var count = commands.Count;
                    try { AlphaVisualTimeline.ParseCommand(new string(text, i + 1, end - i - 1), i, commands); }
                    catch (FormatException ex)
                    {
                        commands.RemoveRange(count, commands.Count - count);
                        warnings.Add(new AlphaParseWarning(fumen, i, fumen.Substring(i, end - i + 1), ex.Message));
                    }
                }
                else warnings.Add(new AlphaParseWarning(fumen, i, fumen.Substring(i, end - i + 1), "当前版本不支持此指令"));
                // Keep newlines and offsets: upstream still reports the original note's location.
                for (var j = i; j <= end; j++) if (text[j] != '\r' && text[j] != '\n') text[j] = ' ';
                i = end;
            }
            AlphaChartCompatibility.Sanitize(text, fumen, warnings);
            var parsed = ParseRecovering(level, designer, text, fumen, warnings, overlayStream);
            foreach (var timing in parsed.NoteTimings)
                if (timing.IsEmpty && timing.RawContent.Length != 0)
                    warnings.Add(new AlphaParseWarning(fumen, Math.Max(0, timing.RawTextPosition - timing.RawContent.Length), timing.RawContent, "当前版本无法解析此槽位，已跳过"));
            var commas = parsed.CommaTimings;
            foreach (var command in commands)
            {
                var index = 0;
                while (index < commas.Length - 1 && commas[index].RawTextPosition < command.Position) index++;
                command.Time = commas[index].Timing;
                if (command.Transition != null)
                    MajdataCore.NoteValueTransition.TryParse(command.Token, (float)commas[index].Bpm,
                        AlphaVisualTimeline.ValueSpec(command.Command), out command.Transition);
                if (command.Command == AlphaVisualTimeline.Kind.Bounce && !command.Reset)
                {
                    if (!MajdataCore.AlphaCommandGrammar.TryParseDuration(command.Token, (float)commas[index].Bpm, out command.Value))
                        command.Reset = true;
                }
                else if(command.Command==AlphaVisualTimeline.Kind.Noise)
                {
                    if (index == commas.Length - 1) { command.Sensor = -1; continue; }
                    if(!AlphaNoiseParser.TryDuration(command.Token,(float)commas[index].Bpm,out command.Value)) {
                        warnings.Add(new AlphaParseWarning(fumen,command.Position,command.Token,"噪域时长无法读取")); command.Sensor=-1;
                    }
                    command.NoiseHS=(float)commas[index].HSpeed;
                    command.ValueY=AlphaNoiseParser.LeadTime(command.NoiseHS);
                }
                else if (command.Command == AlphaVisualTimeline.Kind.Effect)
                {
                    try { command.Effect = AlphaScreenEffectParser.Parse(command.Token, command.Time, (float)commas[index].Bpm); }
                    catch (FormatException ex) { warnings.Add(new AlphaParseWarning(fumen, command.Position, command.Token, ex.Message)); }
                }
                else if (command.Command == AlphaVisualTimeline.Kind.Display)
                {
                    try { command.Display = AlphaDisplayParser.Parse(command.Token, command.Time, (float)commas[index].Bpm); }
                    catch (FormatException ex) { warnings.Add(new AlphaParseWarning(fumen, command.Position, command.Token, ex.Message)); }
                }
            }
            var noteTimings = parsed.NoteTimings.ToArray().ToList();
            var overrides = new Dictionary<SimaiNote, AlphaVisualTimeline>();
            var contexts = new Dictionary<SimaiNote, (double Bpm, float HS, bool Each)>();
            static bool Each(SimaiTimingPoint point) => point.Notes.Count(n => !n.IsMine && !n.IsSlideNoHead && !AlphaNoteDecoration.IsCarrier(n)) > 1;
            if (overlayStreams.Count != 0)
                foreach (var point in parsed.NoteTimings) foreach (var note in point.Notes) contexts[note] = (point.Bpm, point.HSpeed, Each(point));
            foreach (var stream in overlayStreams)
            {
                var anchor = 0;
                while (anchor < commas.Length - 1 && commas[anchor].RawTextPosition < stream.Position) anchor++;
                var point = commas[anchor];
                var prefix = "(" + point.Bpm.ToString(CultureInfo.InvariantCulture) + ")";
                var overlay = ParseChart(level, designer, prefix + stream.Content, true);
                var localTimeline = GetTimeline(overlay).TransformTime(1, point.Timing);
                foreach (var warning in GetWarnings(overlay))
                    warnings.Add(new AlphaParseWarning(fumen, stream.ContentStart + Math.Max(0, warning.Position - prefix.Length), warning.Content, warning.Reason));
                foreach (var local in overlay.NoteTimings)
                {
                    foreach (var note in local.Notes)
                    {
                        note.SlideStartTime += point.Timing;
                        overrides[note] = localTimeline;
                        contexts[note] = (local.Bpm, local.HSpeed, Each(local));
                    }
                    noteTimings.Add(new SimaiTimingPoint(local.Timing + point.Timing, local.Notes, local.RawContent,
                        local.RawTextPositionX, local.RawTextPositionY, local.Bpm, local.HSpeed, local.SVeloc,
                        stream.ContentStart + Math.Max(0, local.RawTextPosition - prefix.Length), local.SignatureNumerator, local.SignatureDenominator));
                }
                foreach (var ev in localTimeline.Changes) ev.Position += stream.ContentStart - prefix.Length;
                foreach(var change in localTimeline.Changes.Where(c => c.Command is AlphaVisualTimeline.Kind.Display or AlphaVisualTimeline.Kind.Effect or AlphaVisualTimeline.Kind.Noise))
                {
                    // Copy the event: assigning its context on the original change
                    // would create a self-reference in the stream's own timeline.
                    commands.Add(change.Command!=AlphaVisualTimeline.Kind.Noise ? change : new AlphaVisualTimeline.Change {
                        Command=change.Command,Time=change.Time,Position=change.Position,Sensor=change.Sensor,
                        Value=change.Value,ValueY=change.ValueY,NoiseHS=change.NoiseHS,
                        NoiseTimeScale=change.NoiseTimeScale,NoiseSource=localTimeline });
                }
            }
            var merged = new List<SimaiTimingPoint>();
            foreach (var point in noteTimings.OrderBy(t => t.Timing))
            {
                if (merged.Count > 0 && Math.Abs(merged[merged.Count - 1].Timing - point.Timing) < 1e-7)
                {
                    var previous = merged[merged.Count - 1];
                    merged[merged.Count - 1] = new SimaiTimingPoint(previous.Timing, previous.Notes.Concat(point.Notes).ToArray(), previous.RawContent,
                        previous.RawTextPositionX, previous.RawTextPositionY, previous.Bpm, previous.HSpeed, previous.SVeloc,
                        previous.RawTextPosition, previous.SignatureNumerator, previous.SignatureDenominator);
                }
                else merged.Add(point);
            }
            var chart = new SimaiChart(level, designer, fumen, merged.ToArray(), parsed.CommaTimings);
            commands.RemoveAll(x => x.Command==AlphaVisualTimeline.Kind.Noise && x.Sensor<0 || x.Command == AlphaVisualTimeline.Kind.Effect && x.Effect == null || x.Command == AlphaVisualTimeline.Kind.Display && x.Display == null);
            if (commands.Count != 0 || overrides.Count != 0)
            {
                var timeline = new AlphaVisualTimeline(commands);
                foreach (var pair in overrides) timeline.NoteOverrides[pair.Key] = pair.Value;
                foreach (var pair in contexts) timeline.NoteContexts[pair.Key] = pair.Value;
                Timelines.Add(chart, timeline);
            }
            if (warnings.Count != 0) Warnings.Add(chart, warnings.ToArray());
            return chart;
        }
        static SimaiChart ParseRecovering(string level, string designer, char[] text, string source, List<AlphaParseWarning> warnings, bool overlayStream)
        {
            // Timing markup errors have a source location. Consume only that control
            // and retry; a bad note never causes an unbounded retry loop.
            for (var attempt = 0; attempt <= text.Length; attempt++)
            {
                try { return SimaiParser.ParseChart(level, designer, new string(text), overlayStream); }
                catch (InvalidSimaiMarkupException ex)
                {
                    var index = 0;
                    for (var line = 1; line < ex.Line && index < text.Length; index++) if (text[index] == '\n') line++;
                    index = Math.Min(text.Length - 1, index + Math.Max(0, ex.Column - 1));
                    if (index < 0) return SimaiChart.Empty;
                    // The exception often points at the closing character of a control.
                    var start = index;
                    while (start > 0 && text[start] != '(' && text[start] != '{' && text[start] != '<' && text[start - 1] != ',' && text[start - 1] != '\n') start--;
                    var end = index + 1;
                    if (text[start] == '(' || text[start] == '{' || text[start] == '<')
                    {
                        var closing = text[start] == '(' ? ')' : text[start] == '{' ? '}' : '>';
                        var close = Array.IndexOf(text, closing, start + 1);
                        if (close >= 0) end = close + 1;
                    }
                    if (new string(text, start, end - start).Trim().Length == 0) throw;
                    warnings.Add(new AlphaParseWarning(source, start, source.Substring(start, end - start), "无法读取，沿用前面的时间设置"));
                    AlphaChartCompatibility.Blank(text, start, end);
                }
            }
            throw new FormatException("无法恢复谱面的时间设置");
        }
        static int CommandEnd(char[] text, int start)
        {
            var quote = '\0';
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') quote = c;
                else if (c == '<') depth++;
                else if (c == '>') { if (depth == 0) return i; depth--; }
            }
            return -1;
        }
        internal static AlphaVisualTimeline ForNote(SimaiNote note) => Current.ForNote(note);
        internal static void BindAlias(SimaiNote source, SimaiNote alias)
        {
            var timeline = ForNote(source);
            if (!ReferenceEquals(timeline, Current)) Current.NoteOverrides[alias] = timeline;
            if (Current.NoteContexts.TryGetValue(source, out var context)) Current.NoteContexts[alias] = context;
        }
        internal static bool? StateEach(SimaiNote note) => Current.NoteContexts.TryGetValue(note, out var context) ? context.Each : null;
        internal static SimaiTimingPoint ForNoteTiming(SimaiNote note, SimaiTimingPoint timing) => Current.NoteContexts.TryGetValue(note, out var context)
            ? new SimaiTimingPoint(timing.Timing, timing.Notes, timing.RawContent, timing.RawTextPositionX, timing.RawTextPositionY, (float)context.Bpm, context.HS, timing.SVeloc, timing.RawTextPosition, timing.SignatureNumerator, timing.SignatureDenominator) : timing;
        public static async Task<SimaiFile> ParseAsync(Stream stream)
        {
            using (stream) return await ParseAsync(await SimaiParser.ParseMetadataAsync(stream));
        }
        public static async Task<SimaiFile> ParseAsync(SimaiMetadata metadata)
        {
            var tasks = new Task<SimaiChart>[7];
            var charts = new SimaiChart[7];
            for (var i = 0; i < 7; i++)
                tasks[i] = ParseChartAsync(metadata.Levels[i], metadata.Designers[i], metadata.Fumens[i]);
            // Preserve upstream import behavior: one broken difficulty does not hide
            // every other chart. Loading that difficulty still reports its parse error.
            try { await Task.WhenAll(tasks); } catch { }
            for (var i = 0; i < 7; i++)
                charts[i] = tasks[i].Status == TaskStatus.RanToCompletion ? tasks[i].Result
                    : new SimaiChart(metadata.Levels[i], metadata.Designers[i], metadata.Fumens[i], Array.Empty<SimaiTimingPoint>());
            return new SimaiFile(metadata.Title, metadata.Artist, metadata.Offset, metadata.Hash, charts, metadata.Commands.ToArray(), metadata.FinalDesigner);
        }
        internal static AlphaVisualTimeline GetTimeline(SimaiChart chart)
        {
            if (Timelines.TryGetValue(chart, out var timeline)) return timeline;
            return AlphaVisualTimeline.Empty;
        }
        internal static IReadOnlyList<AlphaParseWarning> GetWarnings(SimaiChart chart) =>
            Warnings.TryGetValue(chart, out var warnings) ? warnings : Array.Empty<AlphaParseWarning>();
        internal static void AddWarnings(SimaiChart chart, IEnumerable<AlphaParseWarning> extra)
        {
            var warnings = new List<AlphaParseWarning>(GetWarnings(chart));
            warnings.AddRange(extra);
            Warnings.Remove(chart);
            Warnings.Add(chart, warnings.ToArray());
        }
        internal static SimaiChart CopyTimeline(SimaiChart source, SimaiChart target, double? noiseStart = null, double? noiseEnd = null)
        {
            var timeline = GetTimeline(source);
            if (noiseStart.HasValue && noiseEnd.HasValue) timeline = timeline.ClampNoise(noiseStart.Value, noiseEnd.Value);
            if (timeline.HasEffects) Timelines.Add(target, timeline);
            if (Warnings.TryGetValue(source, out var warnings)) Warnings.Add(target, warnings);
            return target;
        }
        internal static void TransformTimeline(SimaiChart chart, double scale, double offset)
        {
            var timeline = GetTimeline(chart);
            Timelines.Remove(chart);
            if (timeline.HasEffects) Timelines.Add(chart, timeline.TransformTime(scale, offset));
        }
        internal static void Activate(SimaiChart chart) => Current = GetTimeline(chart);
        internal static void Clear() => Current = AlphaVisualTimeline.Empty;
    }
}
