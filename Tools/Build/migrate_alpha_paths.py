"""Port the existing Alpha parser/geometry without changing ordinary Play slides."""
from pathlib import Path
import re, shutil
root=Path(__file__).resolve().parents[2]
view=root.parent/'alpha'
dest=root/'Assets/Scripts/Alpha/Core'
dest.mkdir(parents=True,exist_ok=True)
for name in ('NoteExpression','NoteModifiers','SlidePathParser','SlideSyntaxValidator','SlideShapeResolver','SlideCodeParser','TouchSlideSensorRoute','TouchSlideDirection','ChartTokens'):
    shutil.copyfile(view/f'Assets/MajdataCore/{name}.cs',dest/f'{name}.cs')
# Bake the ordered local arrow centers from the existing View prefabs. Geometry,
# not prefab/rendering dependencies, is what Play needs for identical routes.
entries=[]
for path in sorted((view/'Assets/SlidePrefab').glob('Star_*.prefab')):
    stem=path.stem[len('Star_'):]
    key=stem.lower().replace('line_','line').replace('circle_','circle').replace('v_','v').replace('pq_','pq').replace('ppqq_','ppqq').replace('l_','L')
    if key=='S'.lower(): key='s'
    text=path.read_text(encoding='utf-8-sig')
    transforms={match[1]:match[2] for match in re.finditer(r'--- !u!4 &(\d+)\n(.*?)(?=--- !u!|\Z)',text,re.S)}
    parent=next((s for s in transforms.values() if 'm_Father: {fileID: 0}' in s),None)
    if not parent: continue
    children=re.findall(r'- \{fileID: (\d+)\}',parent.split('m_Children:')[1].split('m_Father:')[0])[:-1]
    coords=[]
    for child in children:
        p=re.search(r'm_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',transforms[child])
        coords.append(f'new Vector3({p[1]}f,{p[2]}f,{p[3]}f)')
    if coords: entries.append('{"'+key+'",new Vector3[]{'+','.join(coords)+'}}')
source='''using System; using System.Collections.Generic; using UnityEngine; using MajdataCore;
namespace MajdataPlay.Alpha {
internal sealed class AlphaRouteSource {
static readonly Dictionary<string,Vector3[]> Routes = new() { ENTRIES };
public bool TryGetSlideVisualRoute(string text,out List<Vector3> result) {
result=new(); if(!SlidePathParser.TryParsePath(text,out var parsed)||parsed.segments.Count!=1)return false;
var segment=parsed.segments[0]; if(!SlideShapeResolver.TryResolve(segment,out var shape,out _,out _))return false;
var mirror=shape.StartsWith("-"); if(mirror)shape=shape.Substring(1);
var reverse=shape.StartsWith("r");if(reverse)shape=shape.Substring(1);
if(!Routes.TryGetValue(shape,out var route))return false;
var index=reverse?segment.endPosition:segment.startPosition;
var rotation=Quaternion.Euler(0,0,-45*(mirror?index:index-1));
foreach(var point in route)result.Add(rotation*Vector3.Scale(point,mirror?new Vector3(-1,1,1):Vector3.one));
if(reverse)result.Reverse();return result.Count>0; }
} }
'''.replace('ENTRIES',',\n'.join(entries))
(root/'Assets/Scripts/Alpha/AlphaRouteSource.cs').write_text(source,encoding='utf-8')
# Copy the actual adaptive line/circle, lightning and PQ routines, not an imitation.
slide=(view/'Assets/Scripts/Notes/SlideDrop.cs').read_text(encoding='utf-8-sig')
begin=slide.index('    internal static Vector3[] BuildLightningRoute(')
end=slide.index('    private void AlignDZoneJudgeEffect(',begin)
geometry=slide[begin:end]
touch=(view/'Assets/Scripts/Notes/TouchSlideDrop.cs').read_text(encoding='utf-8-sig')
orbit=touch[touch.index('internal static class SelectableOrbitPathGeometry'):]
orbit=orbit.replace('JsonDataLoader','AlphaRouteSource').replace('SlideDrop.','AlphaPathCurves.')
orbit=orbit.replace('SlideCodePathGeometry.AppendSingleOrbit(','AlphaPathCurves.AppendSingleOrbit(')
(root/'Assets/Scripts/Alpha/AlphaPathCurves.cs').write_text('using System; using System.Collections.Generic; using UnityEngine; namespace MajdataPlay.Alpha { internal static class AlphaPathCurves {\n'+geometry+'\n public static bool AppendSingleOrbit(List<Vector3> p,Vector3 s,Vector3 e,int n,bool ccw) { return false; }\n}\n'+orbit+'\n}',encoding='utf-8')
# The same per-segment builder as TouchSlideDrop, adapted only for its container.
begin=touch.index('    private void AppendSelectableOrbitSegment(')
end=touch.index('    private void BuildSensorRoute()',begin)
methods=touch[begin:end].replace('SlideDrop.','AlphaPathCurves.')
position=touch[touch.index('    private static Vector3 AreaPosition('):touch.index('    private static SensorType GetSensor(')]
(root/'Assets/Scripts/Alpha/AlphaPathGeometry.cs').write_text('''using System; using System.Collections.Generic; using UnityEngine; using MajdataCore;
namespace MajdataPlay.Alpha { internal sealed class AlphaPathGeometry {
readonly List<Vector3> path=new(); readonly AlphaRouteSource slideRouteSource=new();
public static List<Vector3> Build(SlidePathData expression) {
var builder=new AlphaPathGeometry();
foreach(var segment in expression.segments) {
var a=ResolvePosition(segment.start,segment.startPosition,segment.startIsDZone);
var b=ResolvePosition(segment.end,segment.endPosition,segment.endIsDZone);
if(segment.shape=="V") {var m=ResolvePosition(segment.middle,segment.middlePosition,segment.middleIsDZone); builder.AppendSegment(a.Area,a.Position,a.IsDZone,m.Area,m.Position,m.IsDZone,"-"); builder.AppendSegment(m.Area,m.Position,m.IsDZone,b.Area,b.Position,b.IsDZone,"-");}
else if(segment.shape is "P" or "Q") { var m=ResolvePosition(segment.middle,segment.middlePosition,segment.middleIsDZone); builder.AppendSelectableOrbitSegment(a.Area,a.Position,a.IsDZone,m.Area,m.Position,m.IsDZone,segment.middle.source?.Length==1&&char.IsDigit(segment.middle.source[0]),b.Area,b.Position,b.IsDZone,segment.shape); }
else builder.AppendSegment(a.Area,a.Position,a.IsDZone,b.Area,b.Position,b.IsDZone,segment.shape);
} return builder.path; }
''' +methods+position+'''private static float ResolveAngleDelta(float a,float b,char shape,int pos)=>(float)TouchSlideDirection.Sweep(a,b,pos,shape);
} }''',encoding='utf-8')
print('Migrated shared parser and geometry; baked routes:',len(entries))
