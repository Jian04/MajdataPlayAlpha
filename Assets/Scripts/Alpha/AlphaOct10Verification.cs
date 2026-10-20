#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MajdataPlay.Alpha;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MajdataPlay.Editor
{
    public static class AlphaOct10Verification
    {
        public static void Run()
        {
            var root = new GameObject("Image and trajectory opacity regression");
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "CarrierOpacity")); Directory.CreateDirectory(folder);
            var image = new Texture2D(32,32,TextureFormat.RGBA32,false);
            image.SetPixels(Enumerable.Repeat(new Color(.2f,.8f,1,1),32*32).ToArray()); image.Apply();
            File.WriteAllBytes(Path.Combine(folder,"skin.png"),image.EncodeToPNG());
            var model = Sprite.Create(image,new Rect(0,0,32,32),Vector2.one*.5f,32);
            var previousClock = Majdata<INoteController>.Instance; var previousTarget = RenderTexture.active;
            var clock = new Clock(); Majdata<INoteController>.Instance = clock;
            var target = RenderTexture.GetTemporary(64,64,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            NoteSkinLibrary.SetChartFolder(folder);
            try
            {
                var cameraObject = new GameObject("Opacity camera"); cameraObject.transform.SetParent(root.transform,false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.orthographic=true; camera.orthographicSize=1;
                camera.aspect=1; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.cullingMask=1<<31;
                cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                var tick = typeof(AlphaTrajectoryCarrier).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                var inheritedSlot="1~[skin.png]~[B1-B4[8:3]]";
                var streamChart="(222){8}<ZOOM*(True,2.2,8:1)><JUDGELINE*(0)>,\n@{32}<SIZE*tap=(0.8,0.6)>"+inheritedSlot+"/1~[skin.png]~[E8-E6[8:3]],,,,,,"+inheritedSlot+"{64},,,,"+inheritedSlot+",\n,,,{32}<ALPHA*0><DESTROY*10>4/5/6,,,,,,4,";
                var cases=new System.Collections.Generic.List<string>{"", "<ALPHA*slide=0.2>", "<ALPHA*tap=0.4>",streamChart,
                    "(120){4}1,\n@{4}(240)1~[skin.png]~[B1-B4[0##0.8]],\n2,"};
                var arguments=Environment.GetCommandLineArgs();
                var fixtureIndex=Array.IndexOf(arguments,"-alphaCarrierFixture");
                var fixture=fixtureIndex>=0 ? arguments[fixtureIndex+1] : null;
                if(fixture!=null) cases.AddRange(new[]{"original-note.png","original-note_b.png"});
                foreach (var command in cases)
                {
                    var isOriginal=command.StartsWith("original-");
                    var isStream=isOriginal || command.Contains("@{");
                    var source=command;
                    if(isOriginal) {
                        using var input=File.OpenRead(Path.Combine(fixture,"maidata.txt"));
                        source=MajSimai.SimaiParser.ParseMetadata(input).Fumens[1];
                        NoteSkinLibrary.SetChartFolder(fixture);
                    } else NoteSkinLibrary.SetChartFolder(folder);
                    var chart=AlphaSimaiParser.ParseChart("","",isStream ? source : "(120){4}"+command+"1~[skin.png]~[1-5[1:1]],E");
                    if (isStream) chart=chart.AddOffset(4.47f).Scale(1.25f).AddOffset(-.03f);
                    AlphaSimaiParser.Activate(chart);
                    var expectedSkin=isOriginal ? command.Substring("original-".Length) : "skin.png";
                    bool Matches(MajSimai.SimaiNote n) => AlphaNoteDecoration.IsCarrier(n) && AlphaNoteDecoration.Read(n.RawContent).position.skin==expectedSkin;
                    var timing=chart.NoteTimings.ToArray().First(t=>(!isOriginal || t.Timing>10) && t.Notes.Any(Matches));
                    var note=timing.Notes.First(Matches);
                    var expression=AlphaNoteDecoration.Read(note.RawContent);
                    if(expression?.trajectory==null || expression.position.skin!=expectedSkin) throw new Exception("Combined image/path inheritance lost one suffix");
                    var obj=new GameObject("Carrier"); obj.transform.SetParent(root.transform,false); obj.layer=31;
                    var carrier=obj.AddComponent<AlphaTrajectoryCarrier>(); carrier.Configure(note,timing,model,model);
                    var renderer=obj.GetComponent<SpriteRenderer>();
                    var alpha=command=="<ALPHA*tap=0.4>"?.4f:1;
                    var duration=command.Contains("(240)") ? .8f/1.25f : isStream ? (float)(240d/222*3/8/1.25) : 2;
                    var actualDuration=(float)typeof(AlphaTrajectoryCarrier).GetField("_duration",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(carrier);
                    if(Math.Abs(actualDuration-duration)>.00001f) throw new Exception("Carrier duration lost stream BPM or playback speed");
                    foreach(var fraction in new[]{.1f,.4f,.9f})
                    {
                        clock.Time=(float)timing.Timing+duration*fraction; tick.Invoke(carrier,null);
                        if(renderer.forceRenderingOff) throw new Exception("Carrier hidden during its inherited duration");
                        if(Vector3.Distance(obj.transform.localPosition,carrier.Evaluate(fraction))>.0001f) throw new Exception("Carrier lags chart clock");
                        if(isStream && !command.Contains("(240)") && (Math.Abs(obj.transform.localScale.x-.8f)>.0001f || Math.Abs(obj.transform.localScale.y-.6f)>.0001f)) throw new Exception("Overlay SIZE leaked or lost");
                        if(renderer.color.a!=1 || renderer.sprite.texture.GetPixel(renderer.sprite.texture.width/2,renderer.sprite.texture.height/2).a<.99f) throw new Exception("Inherited skin alpha was changed");
                        var properties=new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                        if(Math.Abs(properties.GetFloat("_NoteAlpha")-alpha)>.001f) throw new Exception("Inherited Tap opacity mismatch: "+properties.GetFloat("_NoteAlpha")+" != "+alpha+" in "+command);
                        cameraObject.transform.position=obj.transform.position+new Vector3(0,0,-10);
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                        RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();var inherited=pixels.GetPixel(32,32);
                        renderer.forceRenderingOff=true;
                        var comparison=new GameObject("Ordinary image Tap"); comparison.transform.SetParent(root.transform,false);comparison.layer=31;
                        comparison.transform.position=obj.transform.position;comparison.transform.localScale=obj.transform.localScale;
                        var native=comparison.AddComponent<SpriteRenderer>();native.sprite=renderer.sprite;
                        MaterialPropertyBlock nativeProperties=null;AlphaNoteMaterial.Apply(native,ref nativeProperties,null,alpha);
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                        RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();var expected=pixels.GetPixel(32,32);
                        if(expected.maxColorComponent<.1f || Vector3.Distance(new Vector3(inherited.r,inherited.g,inherited.b),new Vector3(expected.r,expected.g,expected.b))>.015f)
                            throw new Exception("Image/path carrier is more transparent than ordinary custom Tap: "+inherited+" vs "+expected);
                        UnityEngine.Object.DestroyImmediate(comparison);
                    }
                    UnityEngine.Object.DestroyImmediate(obj);
                    if(isOriginal) Debug.Log("SPIN ORIGINAL IMAGE GPU PASS: "+expectedSkin+", center fully opaque, overlay SIZE/ALPHA isolated, carrier motion matches chart clock.");
                }
                Debug.Log("ALPHA CARRIER OPACITY PASS: image plus path suffixes, default opaque, Slide alpha excluded, explicit Tap alpha, real GPU pixels match ordinary custom Tap, Spin overlay/main isolation, song offset, merged stream BPM, explicit seconds and playback speed.");
            }
            finally
            {
                Majdata<INoteController>.Instance=previousClock;AlphaSimaiParser.Clear();AlphaNoteMaterial.Clear();RenderTexture.active=previousTarget;
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(pixels);
                RenderTexture.ReleaseTemporary(target);
            }
        }
        sealed class Clock:INoteController
        {
            public float Time;public float ThisFrameSec=>Time;public float AudioLength=>10;
            public bool IsStart=>true;public bool IsAutoplay=>true;public GameModInfo ModInfo=>default;public JudgeGrade AutoplayGrade=>JudgeGrade.Perfect;
            public Material BreakMaterial=>null;public Material DefaultMaterial=>null;public Material HoldShineMaterial=>null;
        }
    }
}
#endif
