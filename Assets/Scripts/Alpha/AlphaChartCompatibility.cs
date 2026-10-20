using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MajdataPlay.Alpha
{
    internal sealed class AlphaParseWarning
    {
        public int Position { get; }
        public int Line { get; }
        public int Column { get; }
        public string Content { get; }
        public string Reason { get; }
        public AlphaParseWarning(string source, int position, string content, string reason)
        {
            Position = position;
            Content = content;
            Reason = reason;
            Line = Column = 1;
            for (var i = 0; i < Math.Min(position, source.Length); i++)
                if (source[i] == '\n') { Line++; Column = 1; } else Column++;
        }
        public override string ToString() => $"第 {Line} 行，第 {Column} 列：已跳过「{Content}」；{Reason}";
    }

    internal static class AlphaChartCompatibility
    {
        static readonly Regex Ring = new(@"^[1-8](?:[bxmcf!@$?]|h(?:\[[0-9eE.:#+\-]+\])?)*$", RegexOptions.CultureInvariant);
        static readonly Regex Touch = new(@"^(?:[ABDE][1-8]|C)(?:[bxfmc]|h(?:\[[0-9eE.:#+\-]+\])?)*$", RegexOptions.CultureInvariant);
        static readonly Regex Slide = new(@"^[1-8][0-9ebmxcf!@$?h\-^v<>Vpqs zwABC EPQK*\[\]:.# +]+$", RegexOptions.CultureInvariant);

        // Keep delimiters, timing directives and source offsets intact. Unknown note
        // tokens are blanked individually, so a valid / or ` sibling still plays.
        internal static void Sanitize(char[] text, string source, List<AlphaParseWarning> warnings)
        {
            var start = -1;
            var squareDepth = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                var c = i == text.Length ? ',' : text[i];
                if (start < 0 && c == '<' && i + 3 < text.Length &&
                    char.ToUpperInvariant(text[i + 1]) == 'H' && char.ToUpperInvariant(text[i + 2]) == 'S')
                {
                    var end = Array.IndexOf(text, '>', i + 3);
                    if (end >= 0) { i = end; continue; }
                }
                if (c == '|' && i + 1 < text.Length && text[i + 1] == '|')
                {
                    Finish(text, source, warnings, ref start, i);
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (squareDepth == 0 && (c == '(' || c == '{'))
                {
                    var closing = c == '(' ? ')' : '}';
                    var end = Array.IndexOf(text, closing, i + 1);
                    if (end >= 0) { Finish(text, source, warnings, ref start, i); i = end; continue; }
                    // Leave malformed time controls to the bounded recovery parser.
                }
                if (c == '@' && start < 0)
                {
                    // Native ||s time signatures are preserved above; Alpha's @...
                    // signature form is an unsupported line directive in this stage.
                    var end = i;
                    while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
                    warnings.Add(new AlphaParseWarning(source, i, source.Substring(i, end - i), "当前版本不支持此行指令"));
                    Blank(text, i, end);
                    i = end - 1;
                    continue;
                }
                if (c == '[') squareDepth++;
                if (c == ']') squareDepth = Math.Max(0, squareDepth - 1);
                if (squareDepth == 0 && (c == ',' || c == '/' || c == '`' || i == text.Length))
                {
                    Finish(text, source, warnings, ref start, i);
                    continue;
                }
                if (start < 0 && !char.IsWhiteSpace(c)) start = i;
            }
        }
        static void Finish(char[] text, string source, List<AlphaParseWarning> warnings, ref int start, int end)
        {
            if (start < 0) return;
            var token = Regex.Replace(new string(text, start, end - start), @"\s+", "");
            // The end marker is metadata, not a note for MajSimai's bounded parser.
            if(token=="E") Blank(text,start,end);
            // Already-stripped named directives are spaces.
            if (token.Length != 0 && token != "E" && !Recognizes(token))
            {
                warnings.Add(new AlphaParseWarning(source, start, source.Substring(start, end - start).Trim(), "当前版本无法读取此音符"));
                Blank(text, start, end);
            }
            start = -1;
        }
        internal static bool Recognizes(string token)
        {
            if (AlphaPathParser.TryRead(token, out _) || AlphaNoteDecoration.Read(token) is { } extended && !token.Contains('~')) return true;
            if (token.Length == 2 && token[0] is >= '1' and <= '8' && token[1] is >= '1' and <= '8') return true;
            if (token.Contains('*') && MajdataCore.NoteSlotParser.TrySplit(token, out var parts, out _) &&
                parts.TrueForAll(x => Recognizes(x.text))) return true;
            if (token.Contains('~')) return AlphaNoteDecoration.Read(token) != null && Recognizes(AlphaNoteDecoration.Strip(token));
            // Flags may appear between 'h' and its duration (e.g. B1hfx[8:1]).
            // Classification strips them, while the native parser receives the original.
            var basic = Regex.Replace(token, @"[bxmfc!@$?]", "");
            if (Ring.IsMatch(basic) || Touch.IsMatch(basic)) return true;
            // Touchslide/D-position trajectories are not part of the upstream grammar.
            if (!Slide.IsMatch(token) || token.IndexOf('[') < 0 || token.IndexOf(']') < 0) return false;
            return token.IndexOfAny(new[] { '-', '^', 'v', '<', '>', 'V', 'p', 'q', 's', 'z', 'w', 'K' }) >= 0;
        }
        internal static void Blank(char[] text, int start, int end)
        {
            for (var i = start; i < end; i++) if (text[i] != '\n' && text[i] != '\r') text[i] = ' ';
        }
    }
}
