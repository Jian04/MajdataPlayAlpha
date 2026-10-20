#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using MajdataPlay.Alpha;
using UnityEngine;

namespace MajdataPlay.Editor
{
    internal static class AlphaOct08Verification
    {
        internal static void Run()
        {
            var root=new GameObject("PV and inherited note verification");
            var texture=new Texture2D(2,2);
            var sprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f));
            try
            {
                var rotation=root.AddComponent<AlphaPvRotation>();
                var pv=new GameObject("PV source"); pv.transform.SetParent(root.transform,false);
                pv.transform.localRotation=Quaternion.Euler(0,0,12);
                rotation.Register(pv.transform);
                rotation.Apply(-30);
                if(Math.Abs(Mathf.DeltaAngle(pv.transform.localEulerAngles.z,-18))>.001f)
                    throw new Exception("PV counterrotation lost its original pose");
                rotation.Apply(0);
                if(Math.Abs(Mathf.DeltaAngle(pv.transform.localEulerAngles.z,12))>.001f)
                    throw new Exception("PV did not restore after rotation/reset");

                var chart=AlphaSimaiParser.ParseChart("","","(120){4}1~[1p5[1:1]],E");
                AlphaSimaiParser.Activate(chart);
                var timing=chart.NoteTimings[0];
                var obj=new GameObject("Inherited Tap"); obj.transform.SetParent(root.transform,false);
                var carrier=obj.AddComponent<AlphaTrajectoryCarrier>();
                carrier.Configure(timing.Notes.Single(),timing,sprite,sprite);
                var type=(string)typeof(AlphaTrajectoryCarrier).GetField("_type",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(carrier);
                if(type!="tap") throw new Exception("Native stand-in turned the inherited Tap into a Star");
                foreach(var progress in new[]{0f,.2f,.5f,.8f,1f})
                {
                    obj.transform.localRotation=Quaternion.Euler(0,0,79);
                    carrier.SetPose(progress);
                    if(Quaternion.Angle(obj.transform.localRotation,Quaternion.identity)>.001f)
                        throw new Exception("Inherited Tap rotates with its path tangent");
                }
                Debug.Log("ALPHA OCT08 POSE PASS: PV compensation/restoration and inherited Tap fixed orientation along a curved path.");
                VerifyPixels();
                foreach (var source in new[] { "1$~[1-5[1:1]]", "1$$~[1-5[1:1]]", "A1~[A1-A4[1:1]]" })
                {
                    var inherited=AlphaSimaiParser.ParseChart("","","(120){4}"+source+",");
                    var beat=inherited.NoteTimings[0];
                    var visual=new GameObject("Inherited star/touch"); visual.transform.SetParent(root.transform,false);
                    var sample=visual.AddComponent<AlphaTrajectoryCarrier>(); sample.Configure(beat.Notes[0],beat,sprite,sprite);
                    var rotate=(bool)typeof(AlphaTrajectoryCarrier).GetField("_rotatingStar",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sample);
                    if(rotate!=source.Contains("$$")) throw new Exception("Inherited rotating-star modifier lost: "+source);
                    if(source[0]=='A' && typeof(AlphaTrajectoryCarrier).GetField("_line",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sample)!=null)
                        throw new Exception("Inherited Touch must not create a Tap guide arc");
                    if(rotate)
                    {
                        sample.SetPose(.3f); var initial=visual.transform.localRotation;
                        typeof(AlphaTrajectoryCarrier).GetField("_starRotation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sample,40f);
                        sample.SetPose(.3f);
                        if(Math.Abs(Quaternion.Angle(initial,visual.transform.localRotation)-40f)>.01f)
                            throw new Exception("Inherited double-dollar star does not spin");
                    }
                }
                Debug.Log("PLAY INHERITED STAR PASS: dollar/double-dollar modifiers, spin, fixed Tap and Touch without guide arcs");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }
        static void VerifyPixels()
        {
            var root=new GameObject("PV rotation GPU verification");
            var previous=RenderTexture.active;
            var source=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var target=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            var pixels=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var markerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var filter=new Material(Resources.Load<Shader>("AlphaScreenEffects"));
            try
            {
                var colors=new Color[64*64];
                for(int y=0;y<64;y++) for(int x=0;x<64;x++) colors[y*64+x]=new Color(.15f+.6f*x/63,.15f+.6f*y/63,.1f,1);
                texture.SetPixels(colors); texture.Apply(); material.SetTexture("_BaseMap",texture);
                var pv=GameObject.CreatePrimitive(PrimitiveType.Quad); pv.transform.SetParent(root.transform,false); pv.transform.localScale=new Vector3(10,10,1);
                pv.GetComponent<MeshRenderer>().sharedMaterial=material;
                var marker=GameObject.CreatePrimitive(PrimitiveType.Quad); marker.transform.SetParent(root.transform,false);
                marker.transform.localPosition=new Vector3(2,0,-.1f); marker.transform.localScale=Vector3.one*.5f;
                marker.GetComponent<MeshRenderer>().sharedMaterial=markerMaterial; markerMaterial.SetColor("_BaseColor",Color.red);
                root.layer=pv.layer=marker.layer=31;
                var cameraObject=new GameObject("PV test camera"); cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.orthographic=true; camera.orthographicSize=5;
                camera.aspect=1; camera.cullingMask=1<<31; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
                cameraObject.transform.position=new Vector3(0,0,-10);
                cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=false;
                var rotation=root.AddComponent<AlphaPvRotation>(); rotation.Register(pv.transform);
                filter.SetVector("_PlayfieldRect",new Vector4(0,0,1,1));
                Color[] Render(float chartAngle,bool includePV)
                {
                    rotation.Apply(includePV?0:-chartAngle);
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=source});
                    filter.SetFloat("_Rotate",-chartAngle*Mathf.Deg2Rad);
                    Graphics.Blit(source,target,filter); RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,256,256),0,0); pixels.Apply(); return pixels.GetPixels();
                }
                var baseline=Render(0,false); var excluded=Render(30,false); var included=Render(30,true);
                foreach(var point in new[]{new Vector2Int(98,94),new Vector2Int(146,162),new Vector2Int(91,151)})
                {
                    var index=point.y*256+point.x;
                    if(Vector3.Distance(new Vector3(baseline[index].r,baseline[index].g,baseline[index].b),new Vector3(excluded[index].r,excluded[index].g,excluded[index].b))>.03f)
                        throw new Exception("Excluded PV still rotates in the actual filtered pixels");
                }
                var sample=94*256+98;
                if(Math.Abs(included[sample].r-baseline[sample].r)+Math.Abs(included[sample].g-baseline[sample].g)<.03f)
                    throw new Exception("Explicit PV rotation has no visual effect");
                var red=excluded[153*256+172];
                if(red.r<.7f || red.g>.3f) throw new Exception("Excluding PV stopped the chart marker from rotating");
                Debug.Log("ALPHA OCT08 PV GPU PASS: excluded PV stays stationary, explicit PV follows, chart marker still rotates.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(markerMaterial); UnityEngine.Object.DestroyImmediate(filter);
                RenderTexture.active=previous; RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
#endif
