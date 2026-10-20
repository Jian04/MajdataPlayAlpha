#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MajSimai;
using MajdataPlay.Alpha;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEngine;

namespace MajdataPlay.Editor
{
    public static class AlphaFeatureVerification
    {
        public static void Run()
        {
            MajdataPlay.Scenes.Game.Parsing.SlideDataBuilder.InitializeSlideAreaLookup();
            var count = 0;
            foreach (var token in new[] { "1rp5[4:1]", "1rq5[4:1]", "1P35[4:1]", "1Q35[4:1]", "A1>A4[4:1]", "A1-4[4:1]", "A1ppA4[4:1]", "A1qqA4[4:1]", "1d-5d[4:1]", "8P91K1[4:1]", "1dw5d[4:1]", "1dw5[4:1]", "1w5d[4:1]", "1d-5[4:1]", "1-5d[4:1]", "1dV35d[4:1]", "1V3d5[4:1]", "B1?<B5>B8m[1.02##18.36]", "D1?<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1m[2.04##16.32]" })
            {
                var chart = AlphaSimaiParser.ParseChart("", "", "(120){4}" + token + ",");
                if (chart.NoteTimings.Length != 1) throw new Exception("Not parsed: " + token);
                var filtered = NoteLoader.FilterUnsupportedNotes(chart);
                if (filtered.NoteTimings[0].Notes.Length != 1) throw new Exception("Geometry filtered: " + token);
                if (!AlphaPathParser.TryRead(token, out var expression)) continue;
                var metadata = AlphaPathMetadata.Build(expression.path);
                if (metadata.JudgeAreaQueue.Length < 2 || metadata.ArrowPoses.Length < 3 || metadata.SlideLength <= 0) throw new Exception("Incomplete path: " + token);
                var expectedCount = Math.Max(2, Mathf.RoundToInt(metadata.SlideLength / (float)MajdataPlay.Scenes.Game.Parsing.SlideGeo.DefaultDistance));
                if (metadata.ArrowPoses.Length != expectedCount + 1) throw new Exception("Alpha arrows use a different density from native slides: " + token);
                var head = AlphaPathMetadata.Position(expression.position.area, expression.position.position, expression.position.isDZone);
                var first = metadata.ArrowPoses[0];
                if (Vector2.Distance(new Vector2(first.X,first.Y),head) > .01f) throw new Exception("Wrong path head: " + token);
                var end = expression.path.segments.Last().end;
                var tail = AlphaPathMetadata.Position(end.area,end.position,end.isDZone);
                var last = metadata.ArrowPoses.Last();
                if (Vector2.Distance(new Vector2(last.X,last.Y),tail) > .01f) throw new Exception("Wrong path tail: " + token);
                for (var i = 1; i < metadata.JudgeAreaQueue.Length; i++)
                    if (metadata.JudgeAreaQueue[i].ArrowProgressFinish < metadata.JudgeAreaQueue[i-1].ArrowProgressFinish) throw new Exception("Nonmonotonic judge route: " + token);
                if (token=="A1>A4[4:1]")
                {
                    var route=string.Join(",",metadata.JudgeAreaQueue.Select(a=>a.SensorA));
                    if(route!="A1,D2,A2,D3,A3,D4,A4")throw new Exception("Unexpected TouchSlide zones: "+route);
                    Debug.Log("TOUCHSLIDE ROUTE: "+route);
                }
                if (token is "1d-5d[4:1]" or "1d-5[4:1]" or "1-5d[4:1]")
                {
                    var route = string.Join(",", metadata.JudgeAreaQueue.Select(a => a.SensorA));
                    Debug.Log("D SLIDE ROUTE: " + token + " = " + route);
                    if (token == "1d-5d[4:1]" && route != "D1,E1,C,E5,D5") throw new Exception("D-only sensor route differs from ViewAlpha: " + route);
                    if (!metadata.IsAlphaPath) throw new Exception("D slide used native fixed judge table");
                }
                if (expression.path.segments[0].shape == "w")
                {
                    var rays = MajdataPlay.Scenes.Game.Notes.Behaviours.WifiDrop.AlphaWifiRoutes(expression);
                    if (rays.Length != 3 || rays.Any(r=>r.JudgeAreaQueue.Length < 3)) throw new Exception("Incomplete D Wifi sensor rays");
                    for (var i = 0; i < 3; i++)
                    {
                        var expected = AlphaPathMetadata.Position('K', (end.position + i + 6) % 8 + 1, end.isDZone);
                        var endpoint = rays[i].ArrowPoses.Last();
                        if (Vector2.Distance(new Vector2(endpoint.X, endpoint.Y), expected) > .01f) throw new Exception("Wrong D Wifi ray " + i);
                    }
                }
                count++;
            }
            var skinRoot=Path.Combine(Application.streamingAssetsPath,"Skins","default");
            foreach(var name in new[]{"TapSkins/tap_mine.png","StarSkins/star_mine.png","SlideSkins/slide_mine.png","HoldSkins/hold_mine.png"})
                if(!File.Exists(Path.Combine(skinRoot,name)))throw new Exception("Official Mine skin missing: "+name);
            Sprite mine=null;
            var builder=new AtlasBuilder(Path.Combine(Application.temporaryCachePath,"MissingAlphaSkin"));
            builder.Add("TapSkins/tap_mine.png",s=>mine=s);
            var atlas=builder.BuildAndAssign();
            if(!mine||!atlas)throw new Exception("Official Mine fallback did not load");
            UnityEngine.Object.DestroyImmediate(mine); UnityEngine.Object.DestroyImmediate(atlas);
            AlphaVisualVerification.Run();
            AlphaInputVerification.Run();
            Debug.Log("ALPHA FEATURE CHECK PASS: "+count+" Alpha paths, endpoints/judge areas; official Mine fallback; shader SIZE/COLOR and isolated filters.");
        }
        public static void Build()
        {
            MajdataPlay.Scenes.Game.Parsing.SlideDataBuilder.InitializeSlideAreaLookup();
            Run();
            AlphaFakeVerification.Run();
            AlphaNoiseVerification.Run();
            AlphaMediaTimelineVerification.Run();
            Type.GetType("MajdataPlay.Editor.CustomBuild, Assembly-CSharp-Editor",true).GetMethod("BuildRelease").Invoke(null,null);
        }
    }
}
#endif
