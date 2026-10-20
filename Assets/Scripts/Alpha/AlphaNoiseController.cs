using System;
using System.Collections.Generic;
using System.Linq;
using MajdataCore;
using UnityEngine;
using NVector = System.Numerics.Vector2;

// One combined mesh per playfield, independent of the note object pool.
internal readonly struct NoiseZoneVisual
{
    internal readonly Color Color;
    internal readonly Vector2 Size;
    internal readonly bool CustomColor;
    internal NoiseZoneVisual(Color color, Vector2 size, bool customColor = true) { Color = color; Size = size; CustomColor = customColor; }
    internal static NoiseZoneVisual Default => new(Color.red, Vector2.one, false);
}

internal sealed class AlphaNoiseController : MonoBehaviour
{
    private readonly NoiseZoneTimeline timeline = new();
    private readonly float[] warnings = new float[33], warningShines = new float[33];
    private readonly bool[] active = new bool[33], visible = new bool[33], warningZones = new bool[33];
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uvs = new(), flags = new();
    private readonly List<Color> colors = new();
    private readonly List<int> triangles = new();
    private NVector[] centers;
    private List<NVector>[] cells;
    private readonly List<NVector>[] warningCells = new List<NVector>[33];
    private readonly NoiseZoneVisual[] styles = new NoiseZoneVisual[33];
    private NoiseZoneEvent[] visualEvents = Array.Empty<NoiseZoneEvent>();
    private MajdataPlay.Alpha.AlphaVisualTimeline visualTimeline;
    private readonly List<Vector2> projected = new();
    internal Func<double> Clock;
    private GameObject visual;
    private Mesh mesh;
    private Material material;
    private double lastTime = double.NaN;
    private const float Radius = 4.8f;
    internal bool HasActive
    {
        get { foreach (var value in visible) if (value) return true; return false; }
    }
    internal double Time => Clock?.Invoke() ?? 0;
    internal bool HasWarnings => mesh != null && mesh.vertexCount > 0;
    internal Mesh WarningMesh => mesh;
    internal Material WarningMaterial => material;
    internal Matrix4x4 FieldMatrix => visual==null ? Matrix4x4.identity : visual.transform.localToWorldMatrix;

    internal void Configure(IEnumerable<NoiseZoneEvent> events, Func<double> clock,
        MajdataPlay.Alpha.AlphaVisualTimeline appearance = null)
    {
        Clear(); Clock=clock; visualEvents = events?.ToArray() ?? Array.Empty<NoiseZoneEvent>();
        visualTimeline = appearance; timeline.Configure(visualEvents);
        centers=new NVector[33];
        for(int i=0;i<33;i++) { var p=MajdataPlay.Scenes.Game.Utils.NoteHelper.GetTouchAreaPosition((MajdataPlay.IO.SensorArea)i); centers[i]=new NVector(p.x,p.y); }
        cells = NoiseZoneGeometry.BuildCells(centers, Radius);
        visual = new GameObject("Noise zones");
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = new Vector3(0, 0, -.06f);
        mesh = new Mesh { name = "Noise zone union" };
        material = new Material(Resources.Load<Shader>("AlphaNoiseZone"));
        material.SetTexture("_DisplaceTex",Resources.Load<Texture2D>("NoiseZone/BlockNoise1"));
        material.SetTexture("_SparkTex",Resources.Load<Texture2D>("NoiseZone/PointNoise"));
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // Warning polygons share the normal sprite ordering below notes;
        // active refraction is composited before the screen filters.
        renderer.enabled = true;
        renderer.sortingLayerName = "BGCover";
        renderer.sortingOrder = 32000;
        lastTime = double.NaN;
        RenderAt(Time);
    }
    private void LateUpdate()
    {
        if (Clock != null && visual != null) RenderAt(Time);
    }
    readonly NoiseZoneEvent?[] selectedEvents = new NoiseZoneEvent?[33];
    internal void RenderAt(double time)
    {
        if (time == lastTime || visual == null) return;
        lastTime = time;
        Array.Clear(warnings, 0, warnings.Length);
        Array.Clear(selectedEvents, 0, selectedEvents.Length);
        // Select all 33 cells in one pass; the previous per-cell scan repeated
        // every future event 33 times, then searched the command list again.
        foreach (var entry in visualEvents)
        {
            if (time >= entry.End) continue;
            var begun = time >= entry.Start;
            if (!begun)
            {
                var progress = visualTimeline != null ? visualTimeline.NoiseWarningProgress(entry, time) :
                    entry.WarningStart > time ? -1 : (float)((time-entry.WarningStart)/(entry.Start-entry.WarningStart));
                if (progress < 0) continue;
                warnings[entry.Sensor] = Math.Max(warnings[entry.Sensor], 1f - .55f * progress);
            }
            var previous = selectedEvents[entry.Sensor];
            var previousBegun = previous.HasValue && previous.Value.Start <= time;
            if (!previous.HasValue || begun && !previousBegun || begun == previousBegun &&
                (begun ? entry.Start >= previous.Value.Start : entry.Start < previous.Value.Start))
                selectedEvents[entry.Sensor] = entry;
        }
        for (int i = 0; i < 33; i++) {
            active[i] = timeline.TryGetActiveSpan(i,time,out _,out _);
            visible[i] = active[i];
            warningZones[i]=!visible[i] && warnings[i]>0;
            var selected = selectedEvents[i];
            var context=selected.HasValue ? visualTimeline?.ForNoise(selected.Value) : null;
            var tint = context?.Color("blockarea",selected.Value.Start,time,false,false,false);
            var alpha = context?.Alpha("blockarea",selected.Value.Start,time,false,false,false) ?? 1;
            var size = context?.Size("blockarea",selected.Value.Start,time,false,false,false) ?? (1f,1f);
            var color = tint.HasValue ? new Color((tint.Value>>16&255)/255f,(tint.Value>>8&255)/255f,(tint.Value&255)/255f,alpha) : new Color(1,0,0,alpha);
            styles[i] = new NoiseZoneVisual(color,new Vector2(size.Item1,size.Item2),tint.HasValue);
            warningCells[i] ??= new List<NVector>(); warningCells[i].Clear();
            foreach (var p in cells[i]) warningCells[i].Add(ScalePoint(p, i));
        }
        material.SetFloat("_ZoneTime", (float)time);
        if (Array.IndexOf(warningZones, true) < 0)
        {
            if (mesh.vertexCount > 0) mesh.Clear();
            return;
        }
        for(int i=0;i<33;i++) warningShines[i]=warningZones[i]
            ? (float)(.06*(1+.5*Math.Sin(time*37.9))) : 0;
        NoiseZoneGeometry.MergeWarningLevels(cells,centers,warningZones,Radius,warnings,warningShines);
        vertices.Clear(); uvs.Clear(); flags.Clear(); colors.Clear(); triangles.Clear();
        for (int i = 0; i < 33; i++)
        {
            if (!warningZones[i] || cells[i].Count < 3) continue;
            var cell = warningCells[i];
            float alpha = warnings[i];
            float shine=warningShines[i];
            // Native Phira-Pro DisabledBlock has a dark warm-red textured fill,
            // then a separate white ready flash. Pure red outlines alone do
            // not reproduce its warning palette. Custom COLOR remains exact.
            int disabledOrigin = vertices.Count;
            var tint = styles[i].CustomColor ? styles[i].Color : Color.white;
            tint.a = alpha * styles[i].Color.a;
            foreach (var point in cell) AddVertex(point, tint, 1);
            for (int v = 1; v + 1 < cell.Count; v++) AddTriangle(disabledOrigin, disabledOrigin + v, disabledOrigin + v + 1);
            // Ready flashes cover the area softly and brighten its warning frame.
            if(shine>0) {
                int origin=vertices.Count;
                foreach(var point in cell) AddVertex(point,new Color(1,1,1,shine * styles[i].Color.a),0);
                for(int v=1;v+1<cell.Count;v++) AddTriangle(origin,origin+v,origin+v+1);
            }
        }
        foreach(var contour in NoiseZoneGeometry.UnionContours(warningCells,centers,warningZones,Radius))
        {
            float shine=warningShines[contour.Sensor];
            float flash=shine>0 ? .35f*Mathf.Pow(Mathf.Clamp01(shine/.09f),2) : 0;
            var style = styles[contour.Sensor].Color;
            var warningColor = styles[contour.Sensor].CustomColor ? style : new Color(1, .33018857f, .33018857f, 1);
            var color=Color.Lerp(warningColor,Color.white,flash);
            color.a=warnings[contour.Sensor]*style.a;
            var polygon=contour.Points; int start=vertices.Count;
            for(int n=0;n<polygon.Count;n++) {
                var previous=polygon[(n+polygon.Count-1)%polygon.Count];
                var point=polygon[n]; var next=polygon[(n+1)%polygon.Count];
                var e1=NVector.Normalize(point-previous); var e2=NVector.Normalize(next-point);
                var normal1=new NVector(-e1.Y,e1.X); var normal2=new NVector(-e2.Y,e2.X);
                var summed=normal1+normal2;
                var miter=summed.LengthSquared()>.000001f ? NVector.Normalize(summed) : normal2;
                var inset=miter*(.022f/Math.Max(.35f,NVector.Dot(miter,normal2)));
                AddVertex(point,color,0); AddVertex(point+inset,color,0);
            }
            for(int n=0;n<polygon.Count;n++) {
                int a=start+n*2,b=start+((n+1)%polygon.Count)*2;
                AddTriangle(a,b,b+1); AddTriangle(a,b+1,a+1);
            }
        }
        mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetUVs(1, flags);
        mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
    }
    private void AddVertex(NVector point, Color color, float kind)
    {
        vertices.Add(new Vector3(point.X, point.Y, 0));
        uvs.Add(new Vector2(point.X, point.Y)); flags.Add(new Vector2(kind, 0)); colors.Add(color);
    }
    private void AddTriangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
    private NVector ScalePoint(NVector p, int sensor) => centers[sensor] +
        (p - centers[sensor]) * new NVector(styles[sensor].Size.x, styles[sensor].Size.y);

    internal void FillActiveMask(Camera camera,Matrix4x4 field,int width,int height,byte[] pixels, Color32[] stylePixels = null)
    {
        Array.Clear(pixels,0,pixels.Length);
        if (stylePixels != null) Array.Clear(stylePixels,0,stylePixels.Length);
        for(int sensor=0;sensor<active.Length;sensor++)
        {
            if(!visible[sensor]) continue;
            if(!timeline.TryGetVisualSpan(sensor,Time,out double start,out double end)) continue;
            // Short pulses need a full field, not a mostly empty grow/shrink mask.
            double transition=end-start<=.25 ? 0 : Math.Min(.24,(end-start)*.3);
            bool entering=Time<start+transition, leaving=Time>end-transition;
            bool anchored=false;
            if(entering || leaving) {
                var cell=cells[sensor];
                for(int edge=0;edge<cell.Count;edge++) {
                    var a=cell[edge]; var b=cell[(edge+1)%cell.Count];
                    int neighbor=NoiseZoneGeometry.EdgeNeighbor(sensor,a,b,centers,Radius);
                    if(neighbor<0) continue;
                    double boundary=entering ? start : end;
                    if(!timeline.TryGetActiveSpan(neighbor,boundary,out double neighborStart,out _) || neighborStart>=boundary) continue;
                    float progress=(float)((entering ? Time-start : end-Time)/transition);
                    RasterCell(NoiseZoneGeometry.GrowFromEdge(cell,a,b,progress),sensor,camera,field,width,height,pixels,stylePixels);
                    anchored=true;
                }
            }
            if(!anchored) {
                var polygon=leaving ? NoiseZoneGeometry.ShrinkToCenter(cells[sensor],centers[sensor],(float)((end-Time)/transition)) : cells[sensor];
                RasterCell(polygon,sensor,camera,field,width,height,pixels,stylePixels);
            }
        }
    }
    private void RasterCell(List<NVector> cell,int sensor,Camera camera,Matrix4x4 field,int width,int height,byte[] pixels,Color32[] stylePixels)
    {
            if(cell.Count<3) return;
            projected.Clear();
            float minX=width,minY=height,maxX=0,maxY=0;
            foreach(var vertex in cell)
            {
                var scaled = ScalePoint(vertex, sensor);
                var world=field.MultiplyPoint3x4(new Vector3(scaled.X,scaled.Y,0));
                var uv=camera.WorldToViewportPoint(world);
                var p=new Vector2(uv.x*width,uv.y*height); projected.Add(p);
                minX=Mathf.Min(minX,p.x); maxX=Mathf.Max(maxX,p.x); minY=Mathf.Min(minY,p.y); maxY=Mathf.Max(maxY,p.y);
            }
            int left=Mathf.Clamp(Mathf.FloorToInt(minX),0,width),right=Mathf.Clamp(Mathf.CeilToInt(maxX),0,width);
            int bottom=Mathf.Clamp(Mathf.FloorToInt(minY),0,height),top=Mathf.Clamp(Mathf.CeilToInt(maxY),0,height);
            for(int y=bottom;y<top;y++) for(int x=left;x<right;x++)
            {
                if(pixels[y*width+x]!=0) continue;
                var p=new Vector2(x+.5f,y+.5f); bool positive=false,negative=false;
                for(int v=0;v<projected.Count;v++)
                {
                    var a=projected[v]; var b=projected[(v+1)%projected.Count];
                    float cross=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
                    positive |= cross>.00001f; negative |= cross<-.00001f;
                    if(positive && negative) break;
                }
                if(!(positive && negative)) {
                    pixels[y*width+x]=255;
                    if (stylePixels != null) stylePixels[y*width+x]=styles[sensor].Color;
                }
            }
    }
    public void Clear()
    {
        timeline.Configure(null);
        visualEvents=Array.Empty<NoiseZoneEvent>(); visualTimeline=null;
        Array.Clear(active,0,active.Length); Array.Clear(visible,0,visible.Length); Array.Clear(warnings,0,warnings.Length);
        if (visual != null) { visual.SetActive(false); Retire(visual); }
        if (mesh != null) Retire(mesh);
        if (material != null) Retire(material);
        visual = null; mesh = null; material = null; lastTime = double.NaN;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
    private static void Retire(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
