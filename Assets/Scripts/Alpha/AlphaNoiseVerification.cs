#if UNITY_EDITOR
using System;
using System.Linq;
using MajdataCore;
using MajdataPlay.Alpha;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MajdataPlay.Editor
{
    internal static class AlphaNoiseVerification
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        public static void Run()
        {
            AlphaNativeNoiseVerification.Run();
            var root = new GameObject("Noise and filter verification");
            var clock = new Clock();
            var previousClock = Majdata<INoteController>.Instance;
            var previousTarget = RenderTexture.active;
            var source = RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var target = RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var texture = new Texture2D(256,256,TextureFormat.RGBA32,false,true);
            var pixels = new Texture2D(256,256,TextureFormat.RGBA32,false,true);
            var filter = new Material(Resources.Load<Shader>("AlphaScreenEffects"));
            using var composite = new AlphaNoiseRenderer();
            try
            {
                var colors=new Color[256*256];
                for(var y=0;y<256;y++) for(var x=0;x<256;x++)
                    colors[y*256+x]=new Color(x/256f,y/256f,((x/13+y/13)%2)*.6f+.2f,1);
                texture.SetPixels(colors); texture.Apply(); Graphics.Blit(texture,source);
                Color[] Draw(Material material,int pass=0)
                {
                    Graphics.Blit(source,target,material,pass); RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();return pixels.GetPixels();
                }
                float Difference(Color[] a,Color[] b) => a.Zip(b,(x,y)=>(Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b))/3).Average();
                filter.SetVector("_PlayfieldRect",new Vector4(0,0,1,1));
                filter.SetFloat("_EffectTime",.45f);
                var baseline=Draw(filter);
                foreach(var effect in AlphaExtraFilters.All)
                {
                    filter.SetFloat(effect.Uniform,.6f);
                    Require(Difference(Draw(filter),baseline)>.0005f,"Play filter produced no pixels: "+effect.Name);
                    filter.SetFloat(effect.Uniform,0);
                    Require(Difference(Draw(filter),baseline)<.001f,"Play filter did not clear: "+effect.Name);
                }
                var chart=AlphaSimaiParser.ParseChart("14","blockarea","(120){4}XC[4:1]/XD1[8:1]/1,");
                AlphaSimaiParser.Activate(chart); Majdata<INoteController>.Instance=clock;
                clock.Time=.1f;
                Require(AlphaNoiseInputGate.IsBlocked(SensorArea.C),"C input was not disabled");
                Require(AlphaNoiseInputGate.IsBlocked(SensorArea.D1),"D input was not disabled");
                Require(!AlphaNoiseInputGate.IsBlocked(SensorArea.A4),"Unrelated sensor disabled");
                clock.Time=.3f; Require(!AlphaNoiseInputGate.IsBlocked(SensorArea.D1),"Short noise input did not recover");
                Require(AlphaNoiseInputGate.IsBlocked(SensorArea.C),"Long noise ended with its short neighbour");
                clock.Time=.6f; Require(!AlphaNoiseInputGate.IsBlocked(SensorArea.C),"Noise input never recovered");
                clock.Time=.2f;
                var owner=root.AddComponent<AlphaNoiseController>();
                owner.Configure(AlphaSimaiParser.Current.NoiseEvents,()=>clock.Time);
                var camera=root.AddComponent<Camera>(); camera.orthographic=true;camera.orthographicSize=5.2f;
                camera.aspect=1;camera.transform.position=new Vector3(0,0,-10);
                camera.enabled=false;
                var material=composite.Prepare(owner,camera,256,256);
                Require(material!=null,"Noise compositor missing");
                Require(material.GetTexture("_Masks")!=null,"Noise mask texture was not exposed for the render graph");
                var rendered=Draw(material,3);
                Require(Difference(rendered,baseline)>.0005f,"Noise compositor produced no field");
                var centre=rendered[128*256+128];
                Require(centre.r>centre.g,"C noise did not render red");
                material.SetFloat("_AlphaClipSpace",1);material.SetVector("_PlayfieldRect",new Vector4(0,0,.3f,1));
                rendered=Draw(material,3);
                Require(Math.Abs(rendered[128*256+128].g-baseline[128*256+128].g)<.01f,"Noise leaked outside playfield");
                clock.Time=3;owner.RenderAt(clock.Time);Require(!owner.HasActive,"Noise stayed after expiry");
                clock.Time=.2f;owner.RenderAt(clock.Time);Require(owner.HasActive,"Noise did not restore after seeking");
                var isolated=AlphaSimaiParser.ParseChart("14","isolated","(120){4}XC[1:1],");
                clock.Time=1;owner.Configure(AlphaSimaiParser.GetTimeline(isolated).NoiseEvents,()=>clock.Time);
                var mask=new byte[64*64];
                int Area(float time) { clock.Time=time;owner.RenderAt(time);owner.FillActiveMask(camera,owner.FieldMatrix,64,64,mask);return mask.Count(v=>v!=0); }
                var exact=AlphaSimaiParser.GetTimeline(AlphaSimaiParser.ParseChart("14","exact","(120){4}XC[1:0],XD1[384:1],E"));
                owner.Configure(exact.NoiseEvents,()=>clock.Time,exact);
                clock.Time=-.1f;owner.RenderAt(clock.Time);
                Require(owner.WarningMesh.vertexCount>0,"Zero Play noise lost its warning mesh");
                Require(Area(0)==0 && Area(.05f)==0,"Zero Play noise must not render a live field");
                Require(Area(.502f)>0 && Area(.506f)==0,"384th Play noise does not render for its exact duration");
                foreach(var zeroToken in new[]{"XC","XC[1:0]"})
                {
                    var zeroTimeline=AlphaSimaiParser.GetTimeline(AlphaSimaiParser.ParseChart("","","(120){4}"+zeroToken+",E")).ClampNoise(0,1);
                    owner.Configure(zeroTimeline.NoiseEvents,()=>clock.Time,zeroTimeline);
                    Area(-.01f); Require(owner.WarningMesh.vertexCount>0,"Clipped zero noise lost prewarning");
                    foreach(var sample in new[]{0f,.001f,.05f,.1f})
                        Require(Area(sample)==0 && owner.WarningMesh.vertexCount==0,"Clipped zero noise rendered after its warning: "+zeroToken);
                }
                Debug.Log("NOISE EXACT DURATION PASS: zero warning-only; 384th active mask ends at its real duration.");
                owner.Configure(AlphaSimaiParser.GetTimeline(isolated).NoiseEvents,()=>clock.Time);
                var fullArea=Area(1);var halfArea=Area(1.88f);var lastArea=Area(1.97f);
                Require(fullArea>halfArea && halfArea>lastArea,"Single Play noise did not shrink toward its center");
                var sized = AlphaSimaiParser.ParseChart("14","sized","(120){4}<SIZE*blockarea=(0.5,0.75)>XC[1:1],");
                var sizeTimeline = AlphaSimaiParser.GetTimeline(sized);
                owner.Configure(sizeTimeline.NoiseEvents,()=>clock.Time,sizeTimeline);
                var smallArea = Area(1);
                Require(smallArea > 0 && smallArea < fullArea * .55f && smallArea > fullArea * .25f,
                    $"Noise SIZE must scale the visible mask around its sensor center: native={fullArea}, sized={smallArea}");
                clock.Time = -.2f; owner.RenderAt(clock.Time);
                var warningSize = owner.WarningMesh.bounds.size;
                owner.Configure(AlphaSimaiParser.GetTimeline(isolated).NoiseEvents,()=>clock.Time);
                var nativeWarningSize = owner.WarningMesh.bounds.size;
                Require(warningSize.x < nativeWarningSize.x * .6f && warningSize.y < nativeWarningSize.y * .85f,
                    "Noise SIZE must scale its warning polygon as well as its active mask");
                Require(sizeTimeline.Noise.IsBlocked((int)SensorArea.C,1) && !sizeTimeline.Noise.IsBlocked((int)SensorArea.B1,1),
                    "Noise SIZE changed the original disabled input areas");
                Debug.Log("ALPHA NOISE SIZE CHECK PASS: warning polygons and active masks scale around each sensor; input areas unchanged.");
                clock.Time=.2f;owner.Configure(AlphaSimaiParser.Current.NoiseEvents,()=>clock.Time);
                // Exercise the actual renderer feature for a noise-only chart:
                // it must run even when no ordinary screen filter is enabled.
                camera.cullingMask=0;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.4f,.4f,.4f,1);
                root.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                var realCentre=pixels.GetPixel(128,128);
                Require(realCentre.r>realCentre.g+.02f,"URP noise-only chart did not composite a red field");
                // Hide the isolated manual test mesh; include the real warning
                // renderer even if URP rebuilds the feature between requests.
                owner.GetComponentInChildren<MeshRenderer>().enabled=false;
                camera.cullingMask=~0;
                var warningStart = AlphaSimaiParser.Current.NoiseEvents.Min(zone => zone.WarningStart);
                foreach (var warningTime in new[] { (float)warningStart + .01f, -.2f })
                {
                    clock.Time = warningTime;
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                    Require(pixels.GetPixel(128,128).r > pixels.GetPixel(5,5).r + .005f,
                        "URP warning missing at " + warningTime+"; center="+pixels.GetPixel(128,128)+", background="+pixels.GetPixel(5,5));
                    Require(!AlphaNoiseInputGate.IsBlocked(SensorArea.C), "Prewarning disabled input");
                }
                // The same frame can contain active C and a neighbouring D warning.
                var mixed = AlphaSimaiParser.ParseChart("14","mixed","(120){4}XC[1:1],XD1[4:1],E");
                AlphaSimaiParser.Activate(mixed);clock.Time=.3f;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                Require(pixels.GetPixel(128,128).r > pixels.GetPixel(128,128).g + .02f, "Warning pass cleared the active noise field");
                Debug.Log("ALPHA NOISE WARNING CHECK PASS: prewarning-only real URP pixels, input stays enabled, active field and neighbouring warning coexist.");
                var filteredNoise=AlphaSimaiParser.ParseChart("14","filtered noise","(120){4}<SATURATION*(True,1)>XC[1:1],E");
                AlphaSimaiParser.Activate(filteredNoise);clock.Time=.3f;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                var filteredCentre=pixels.GetPixel(128,128);
                Require(Math.Abs(filteredCentre.r-filteredCentre.g)<.015f && Math.Abs(filteredCentre.g-filteredCentre.b)<.015f,
                    "Screen filters must process the noise composite as well as ordinary notes");
                Debug.Log("ALPHA NOISE FILTER ORDER PASS: desaturation applies after active noise composition.");
                AlphaSimaiParser.Clear();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
                var cleared=pixels.GetPixel(128,128);
                Require(Math.Abs(cleared.r-cleared.g)<.01f,"URP noise field leaked after chart reset");
                foreach(var shader in new[]{filter.shader,Resources.Load<Shader>("AlphaNoiseComposite"),Resources.Load<Shader>("AlphaNoiseZone")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader)) Require(message.severity.ToString()!="Error",message.message);
                Debug.Log("ALPHA NOISE FILTER CHECK PASS: 10 GPU filters/reset, input disable/recovery, red composite, playfield isolation, seeking and real URP noise-only/reset.");
            }
            finally
            {
                Majdata<INoteController>.Instance=previousClock;AlphaSimaiParser.Clear();RenderTexture.active=previousTarget;
                RenderTexture.ReleaseTemporary(source);RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(filter);UnityEngine.Object.DestroyImmediate(root);
            }
        }
        sealed class Clock : INoteController
        {
            public float Time;
            public float ThisFrameSec=>Time; public float AudioLength=>10;
            public bool IsStart=>true;public bool IsAutoplay=>true;
            public GameModInfo ModInfo=>default; public JudgeGrade AutoplayGrade=>JudgeGrade.Perfect;
            public Material BreakMaterial=>null;public Material DefaultMaterial=>null;public Material HoldShineMaterial=>null;
        }
    }
}
#endif
