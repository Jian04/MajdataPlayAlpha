from pathlib import Path
root=Path(__file__).resolve().parents[2]
view=root.parent/'alpha'
source=(view/'MajdataEdit/SimaiProcess.cs').read_text(encoding='utf-8-sig')
methods=source[source.index('    private static bool TryParseSubtitleChange('):source.index('    private static bool TryParseScreenEffect(')]
header='''using System; using System.Linq; using System.Globalization; using MajdataCore;
namespace MajdataPlay.Alpha {
internal sealed class SubtitleChange { public double time; public string text=""; public float duration=-1,x,y,size,transition; public string font="",style="Fade"; public int index; }
internal sealed class AlphaDisplayEvent {
public string Name; public double Time; public float Value,Duration; public SubtitleChange Subtitle;
public AlphaDisplayEvent Transform(double scale,double offset) {
var result=(AlphaDisplayEvent)MemberwiseClone();result.Time=Time*scale+offset; result.Duration=Duration*(float)scale;
if(Subtitle!=null)result.Subtitle=new SubtitleChange {time=result.Time,text=Subtitle.text,duration=Subtitle.duration<0?-1:Subtitle.duration*(float)scale,x=Subtitle.x,y=Subtitle.y,size=Subtitle.size,font=Subtitle.font,index=Subtitle.index,style=Subtitle.style,transition=Subtitle.transition*(float)scale};return result; }
}
internal static class AlphaDisplayParser {
internal static bool Supports(string name)=>name is "TEXT" or "SHOWCOMBOINFO" or "SHOWJUDGEINFO" or "INNERBRIGHTNESS";
internal static AlphaDisplayEvent Parse(string token,double time,float bpm) {
if(!AlphaCommandGrammar.TryValidate(token,bpm,out var error))throw new FormatException(error);
AlphaCommandGrammar.TrySplitToken(token,out var name,out var body);name=name.ToUpperInvariant();
if(name=="TEXT") { if(!TryParseSubtitleChange(token,time,bpm,out var subtitle))throw new FormatException("字幕参数无法读取");return new AlphaDisplayEvent{Name=name,Time=time,Subtitle=subtitle}; }
if(body.StartsWith("("))body=body.Substring(1,body.Length-2);
if(!AlphaCommandGrammar.TrySplitValues(body,out var values))throw new FormatException("显示参数无法读取");
var value=name.StartsWith("SHOW")?(values[0].Equals("true",StringComparison.OrdinalIgnoreCase)||values[0]=="1"?1:0):float.Parse(values[0],CultureInfo.InvariantCulture);
float duration=0;if(values.Count>1&&!TryParseCommandDuration(values[1],bpm,out duration))throw new FormatException("过渡时长无法读取");
return new AlphaDisplayEvent{Name=name,Time=time,Value=Math.Clamp(value,0,1),Duration=duration}; }
static bool TryParseCommandDuration(string text,float bpm,out float seconds)=>AlphaCommandGrammar.TryParseDuration(text,bpm,out seconds);
'''
(root/'Assets/Scripts/Alpha/AlphaDisplayParser.cs').write_text(header+methods+'\n} }',encoding='utf-8')
# Keep the copied PQ circle builder (including 0/9 selectors) intact.
code=(view/'Assets/Scripts/Notes/SlideCodePathGeometry.cs').read_text(encoding='utf-8-sig')
code=code.replace('internal static class SlideCodePathGeometry','namespace MajdataPlay.Alpha { internal static class AlphaSlideCodePathGeometry')+'\n}'
(root/'Assets/Scripts/Alpha/AlphaSlideCodePathGeometry.cs').write_text(code,encoding='utf-8')
p=root/'Assets/Scripts/Alpha/AlphaPathCurves.cs'
code=p.read_text(encoding='utf-8').replace('return false; }\n}', 'return AlphaSlideCodePathGeometry.AppendSingleOrbit(p,s,e,n,ccw); }\n}')
if 'static Vector3[] BuildStraightRoute(' not in code: code=code.replace('internal static class AlphaPathCurves {','''internal static class AlphaPathCurves {
static Vector3[] BuildStraightRoute(int count,Vector3 start,Vector3 end) { var result=new Vector3[Math.Max(2,count)]; for(var i=0;i<result.Length;i++)result[i]=Vector3.Lerp(start,end,i/(float)(result.Length-1));return result; }
''')
p.write_text(code,encoding='utf-8')
for shader in [root/'Assets/Scripts/Alpha/AlphaNoteTint.shader',*list((root/'Assets/Rendering/Shaders').glob('*.shader'))]:
    code=shader.read_text(encoding='utf-8-sig')
    if '_NoteAlpha' not in code or '_NoteSize' in code:continue
    code=code.replace('Properties\n    {','Properties\n    {\n        _NoteSize ("Note Size", Vector) = (1,1,1,1)') if shader.name=='AlphaNoteTint.shader' else code.replace('_NoteAlpha("Note Opacity", Range(0,1)) = 1','_NoteAlpha("Note Opacity", Range(0,1)) = 1\n        _NoteSize ("Note Size", Vector) = (1,1,1,1)')
    if shader.name=='AlphaNoteTint.shader':code=code.replace('float _NoteAlpha;','float _NoteAlpha;\n            float4 _NoteSize;')
    code=code.replace('o.vertex = UnityObjectToClipPos(v.vertex);','v.vertex.xy *= _NoteSize.xy;\n                o.vertex = UnityObjectToClipPos(v.vertex);').replace('o.pos = TransformObjectToHClip(v.vertex.xyz);','v.vertex.xy *= _NoteSize.xy;\n                o.pos = TransformObjectToHClip(v.vertex.xyz);')
    shader.write_text(code,encoding='utf-8')
p=root/'Assets/Scripts/Alpha/AlphaNoteTint.hlsl'
code=p.read_text(encoding='utf-8')
if 'float4 _NoteSize;' not in code: p.write_text(code.replace('float _NoteAlpha;','float _NoteAlpha;\nfloat4 _NoteSize;'),encoding='utf-8')
print('Migrated TEXT parser, PQ circles and SIZE shaders')
