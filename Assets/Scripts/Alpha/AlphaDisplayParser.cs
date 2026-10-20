using System; using System.Linq; using System.Globalization; using MajdataCore;
namespace MajdataPlay.Alpha {
internal sealed class SubtitleChange { public double time; public string text=""; public float duration=-1,x,y,size,transition; public string font="",style="Fade",color="FFFFFF"; public int index; public uint RGB=0xFFFFFF; }
internal sealed class AlphaDisplayEvent {
public string Name; public string Path; public double Time; public float Value,Duration; public bool ResetToDefault; public uint? RGBA; public SubtitleChange Subtitle;
public AlphaDisplayEvent Transform(double scale,double offset) {
var result=(AlphaDisplayEvent)MemberwiseClone();result.Time=Time*scale+offset; result.Duration=Duration*(float)scale;
if(Subtitle!=null)result.Subtitle=new SubtitleChange {time=result.Time,text=Subtitle.text,duration=Subtitle.duration<0?-1:Subtitle.duration*(float)scale,x=Subtitle.x,y=Subtitle.y,size=Subtitle.size,font=Subtitle.font,index=Subtitle.index,style=Subtitle.style,color=Subtitle.color,RGB=Subtitle.RGB,transition=Subtitle.transition*(float)scale};return result; }
}
internal static class AlphaDisplayParser {
internal static bool Supports(string name)=>name is "TEXT" or "AUDIO" or "PVOVERLAY" or "SHOWCOMBOINFO" or "SHOWJUDGEINFO" or "SHOWJUDGETEXT" or "COMBODISPLAY" or "INNERBRIGHTNESS" or "JLINE" or "JUDGELINE" or "JUDGELINEEXPAND";
internal static AlphaDisplayEvent Parse(string token,double time,float bpm) {
if(!AlphaCommandGrammar.TryValidate(token,bpm,out var error))throw new FormatException(error);
AlphaCommandGrammar.TrySplitToken(token,out var name,out var body);name=name.ToUpperInvariant();
if(name=="TEXT") { if(!TryParseSubtitleChange(token,time,bpm,out var subtitle))throw new FormatException("字幕参数无法读取");return new AlphaDisplayEvent{Name=name,Time=time,Subtitle=subtitle}; }
if(body.StartsWith("("))body=body.Substring(1,body.Length-2);
if(!AlphaCommandGrammar.TrySplitValues(body,out var values))throw new FormatException("显示参数无法读取");
if(AlphaCommandGrammar.TryFind(name,out var descriptor) && descriptor.SupportsDefaultReset && values[0].Equals("Default",StringComparison.OrdinalIgnoreCase)) {
float duration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Duration=duration,ResetToDefault=true}; }
if(name is "AUDIO" or "PVOVERLAY") {
var enabled=values[0].Equals("true",StringComparison.OrdinalIgnoreCase)||values[0]=="1";
float duration=0;var at=enabled?2:1;
if(values.Count>at&&!TryParseCommandDuration(values[at],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Value=enabled?1:0,Path=enabled?values[1].Trim().Trim('"'):"",Duration=duration}; }
if(name=="COMBODISPLAY") {
AlphaCommandGrammar.TryParseComboMode(values[0],out var mode);
if(mode is not (0 or 1 or 2 or 3 or 4 or 11 or 12 or 13)) throw new FormatException("此中央显示类型没有 Play 对应项，已跳过");
float duration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Duration=duration,Value=mode}; }
if(name=="JLINE") {
uint? rgba=null;var raw=values[0].Trim().TrimStart('#');
if(!raw.Equals("NULL",StringComparison.OrdinalIgnoreCase))rgba=uint.Parse(raw.Length==6?raw+"FF":raw,NumberStyles.HexNumber,CultureInfo.InvariantCulture);
float duration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,RGBA=rgba,Duration=duration}; }
if(name=="JUDGELINEEXPAND") {
var hasState=bool.TryParse(values[0],out var enabled);
if(hasState&&!enabled)return new AlphaDisplayEvent{Name=name,Time=time,Value=0};
var offset=hasState?1:0;float duration=0;
if(values.Count>offset&&!TryParseCommandDuration(values[offset],bpm,out duration))throw new FormatException("过渡时长无法读取");
var direction=values.Count>offset+1?values[offset+1].ToUpperInvariant():"1";
return new AlphaDisplayEvent{Name=name,Time=time,Duration=duration,Value=direction is "1" or "CW" ? -22.5f : 22.5f}; }
if(name=="JUDGELINE") {
var raw=values[0].ToUpperInvariant();
if(raw is not ("0" or "1" or "FALSE" or "TRUE" or "NULL"))throw new FormatException("Play 保留原版判定线，仅支持 0/false 关闭、1/true 开启，不切换样式");
float duration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Duration=duration,Value=raw is "0" or "FALSE" ? 0 : 1}; }
var value=name.StartsWith("SHOW")?(values[0].Equals("true",StringComparison.OrdinalIgnoreCase)||values[0]=="1"?1:0):float.Parse(values[0],CultureInfo.InvariantCulture);
float transitionDuration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out transitionDuration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Value=Math.Clamp(value,0,1),Duration=transitionDuration}; }
static bool TryParseCommandDuration(string text,float bpm,out float seconds)=>AlphaCommandGrammar.TryParseDuration(text,bpm,out seconds);
    private static bool TryParseSubtitleChange(string token, double time, float bpm, out SubtitleChange change)
    {
        change = new SubtitleChange();
        if (!token.StartsWith("TEXT*", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = token.Substring(5).Trim();
        var duration = -1f;
        var content = string.Empty;
        var x = 0f;
        var y = 0f;
        var size = 0f;
        var font = string.Empty;
        var color = "FFFFFF";
        var subtitleIndex = 0;
        var style = "Fade";
        var transition = 0f;

        if (body.Length < 4 || body[0] != '(' || body[^1] != ')' ||
            !AlphaCommandGrammar.TrySplitValues(
                body.Substring(1, body.Length - 2), out var values) ||
            values.Count == 0 || values[0].Length < 2 ||
            values[0][0] != '"' || values[0][^1] != '"')
            return false;

        content = values[0].Substring(1, values[0].Length - 2)
            .Replace("\\\"", "\"");
        var positional = values.Count > 1 &&
                         values.Skip(1).All(value => !value.Contains('='));
        if (positional)
        {
            if (values[1].Length > 0)
            {
                if (!TryParseCommandDuration(values[1], bpm, out duration))
                    return false;
                duration = Math.Max(0f, duration);
            }
            if (values.Count > 2 && values[2].Length > 0 &&
                !TryParseSubtitleNumber(values[2], float.MinValue, float.MaxValue, out x))
                return false;
            if (values.Count > 3 && values[3].Length > 0 &&
                !TryParseSubtitleNumber(values[3], float.MinValue, float.MaxValue, out y))
                return false;
            if (values.Count > 4 && values[4].Length > 0 &&
                !TryParseSubtitleNumber(values[4], float.MinValue, float.MaxValue, out size))
                return false;
            if (values.Count > 5 && values[5].Length > 0 &&
                !AlphaCommandGrammar.TryNormalizeSubtitleFont(values[5], out font))
                return false;
            if (values.Count > 6 && values[6].Length > 0 &&
                (!int.TryParse(values[6], NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out subtitleIndex) ||
                 subtitleIndex < 0))
                return false;
            if (values.Count > 7 && values[7].Length > 0 &&
                !AlphaCommandGrammar.TryNormalizeSubtitleStyle(values[7], out style))
                return false;
            if (values.Count > 8 && values[8].Length > 0)
            {
                if (!TryParseCommandDuration(values[8], bpm, out transition))
                    return false;
                transition = Math.Max(0f, transition);
            }
            if (values.Count > 9 && values[9].Length > 0) color = values[9].TrimStart('#');
        }
        else
        for (var index = 1; index < values.Count; index++)
        {
            var value = values[index];
            if (TryParseSubtitlePlacement(value, ref x, ref y, ref size))
                continue;
            if (TryParseSubtitleOption(
                    value, bpm, ref font, ref subtitleIndex,
                    ref style, ref transition, ref color))
                continue;
            if (duration < 0f &&
                TryParseCommandDuration(value, bpm, out var parsedDuration))
            {
                duration = Math.Max(0f, parsedDuration);
                continue;
            }
            return false;
        }

        change = new SubtitleChange
        {
            time = time,
            text = content,
            duration = duration,
            x = x,
            y = y,
            size = size,
            font = font,
            index = subtitleIndex,
            style = style,
            transition = transition, color = color, RGB = uint.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        };
        return true;
    }

    private static bool TryParseSubtitleNumber(
        string text, float minimum, float maximum, out float value)
    {
        if (!float.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) ||
            float.IsNaN(value) || float.IsInfinity(value))
            return false;
        value = Math.Clamp(value, minimum, maximum);
        return true;
    }

    private static bool TryParseSubtitlePlacement(
        string text, ref float x, ref float y, ref float size)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0)
            return false;
        var key = text.Substring(0, equals).Trim().ToLowerInvariant();
        if (!float.TryParse(
                text.Substring(equals + 1).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            float.IsNaN(value) || float.IsInfinity(value))
            return false;
        switch (key)
        {
            case "x":
                x = value;
                return true;
            case "y":
                y = value;
                return true;
            case "size":
                size = value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseSubtitleOption(
        string text,
        float bpm,
        ref string font,
        ref int index,
        ref string style,
        ref float transition,
        ref string color)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0)
            return false;
        var key = text.Substring(0, equals).Trim().ToLowerInvariant();
        var value = text.Substring(equals + 1).Trim();
        switch (key)
        {
            case "color":
                color = value.TrimStart('#');
                return true;
            case "font":
                if (!AlphaCommandGrammar.TryNormalizeSubtitleFont(value, out font))
                    return false;
                return true;
            case "index":
                return int.TryParse(
                           value, NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out index) &&
                       index >= 0;
            case "style":
                return AlphaCommandGrammar.TryNormalizeSubtitleStyle(value, out style);
            case "transition":
                if (!TryParseCommandDuration(value, bpm, out transition))
                    return false;
                transition = Math.Max(0f, transition);
                return true;
            default:
                return false;
        }
    }


} }
