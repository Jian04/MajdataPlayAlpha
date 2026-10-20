using System;
using System.Collections.Generic;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    internal static class AlphaStreamSyntax
    {
        internal sealed class Stream
        {
            internal int Position, ContentStart;
            internal string Content;
        }
        // Blank in place so original diagnostics remain at their authored line/column.
        internal static List<Stream> Prepare(char[] text)
        {
            var streams = new List<Stream>();
            var source = new string(text);
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && quoted) { i++; continue; }
                if (text[i] == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (text[i] == '|' && i + 1 < text.Length && text[i + 1] is '*' or '|')
                {
                    var block = text[i + 1] == '*';
                    var end = block ? source.IndexOf("*|", i + 2, StringComparison.Ordinal) : Array.IndexOf(text, '\n', i + 2);
                    end = end < 0 ? text.Length : end + (block ? 2 : 0);
                    AlphaChartCompatibility.Blank(text, i, end); i = end - 1; continue;
                }
                if (text[i] == '@' && TrySkipMeter(source, i, out var meterEnd))
                {
                    AlphaChartCompatibility.Blank(text, i, meterEnd); i=meterEnd-1; continue;
                }
                if (text[i] is not ('@' or '&') || !EditorDirectiveScanner.TryRead(source, i, out var directive)) continue;
                var endPosition = i + directive.length;
                if (directive.kind == EditorDirectiveKind.Overlay)
                {
                    var block = text[i + 1] == '*';
                    if (!block) { endPosition = Array.IndexOf(text, '\n', i); if (endPosition < 0) endPosition = text.Length; }
                    var contentStart = i + (block ? 2 : 1);
                    streams.Add(new Stream { Position = i, ContentStart = contentStart, Content = new string(text, contentStart, endPosition - contentStart - (block ? 2 : 0)) });
                }
                AlphaChartCompatibility.Blank(text, i, endPosition); i = endPosition - 1;
            }
            return streams;
        }
        static bool TrySkipMeter(string text, int index, out int end)
        {
            end=index;
            var p=index+1;
            while(p<text.Length && (text[p]==' ' || text[p]=='\t')) p++;
            var start=p;
            while(p<text.Length && text[p]>='0' && text[p]<='9') p++;
            if(p==start) return false;
            while(p<text.Length && (text[p]==' ' || text[p]=='\t')) p++;
            if(p>=text.Length || text[p++]!='/') return false;
            while(p<text.Length && (text[p]==' ' || text[p]=='\t')) p++;
            start=p;
            while(p<text.Length && text[p]>='0' && text[p]<='9') p++;
            if(p==start) return false;
            while(p<text.Length && (text[p]==' ' || text[p]=='\t')) p++;
            if(p<text.Length && text[p]!='\r' && text[p]!='\n') return false;
            end=p;
            return true;
        }
    }
}
