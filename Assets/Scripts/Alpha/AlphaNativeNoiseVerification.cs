#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace MajdataPlay.Editor {
internal static class AlphaNativeNoiseVerification {
public static void Run() => VerifyNativeColorAndParticles();
    private static void VerifyNativeColorAndParticles()
    {
        var material = new Material(Resources.Load<Shader>("AlphaNoiseComposite"));
        Texture2D Constant(Color color) {
            var texture = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point };
            texture.SetPixel(0,0,color); texture.Apply(); return texture;
        }
        var displacement=Constant(new Color(.5f,.5f,.5f,1));
        var masks=Constant(new Color(1,0,0,1)); var styles=Constant(Color.red);
        var sparks=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat };
        float Spark(int x,int y) => ((x/3+y/5)%4)*.125f;
        for(int y=0;y<128;y++) for(int x=0;x<128;x++) sparks.SetPixel(x,y,new Color(Spark(x,y),0,0,1));
        sparks.Apply(); material.SetTexture("_DisplaceTex",displacement); material.SetTexture("_SparkTex",sparks);
        material.SetTexture("_Masks",masks); material.SetTexture("_Styles",styles); material.SetFloat("_ZoneTime",0);
        var oldTarget=RenderTexture.active;
        try {
            foreach(var size in new[]{new Vector2Int(256,256),new Vector2Int(512,256),new Vector2Int(1024,512)}) {
                var source=new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true);
                var palette=new[]{new Color(1,.8f,.05f),new Color(.05f,.5f,1),new Color(.12f,.12f,.12f)};
                for(int y=0;y<size.y;y++) for(int x=0;x<size.x;x++) source.SetPixel(x,y,palette[Math.Min(2,x*3/size.x)]);
                source.Apply();
                var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create();
                var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false,true);
                try {
                    material.SetVector("_OutputSize",new Vector4(size.x,size.y,1f/size.x,1f/size.y));
                    Graphics.Blit(source,target,material,3); RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0); pixels.Apply();
                    // Independent transcription of the reference ActiveBlock
                    // HSV/overlay/premultiplied blend, not a red-pixel count.
                    for(int bar=0;bar<3;bar++) for(int step=0;step<13;step++) {
                        int x=(int)((bar+.5f)*size.x/3),y=9+step*(size.y-20)/13;
                        float u=(x+.5f)/size.x,v=(y+.5f)/size.y;
                        var sparkColor=new Color(1,.28490567f,.28490567f)*Spark((int)(Mathf.Repeat(u*3,1)*128),(int)(Mathf.Repeat(v*1.2f,1)*128))*.5f*5.69f;
                        Color.RGBToHSV(palette[bar],out var h,out var s,out var value);
                        var shifted=Color.HSVToRGB(Mathf.Repeat(h+sparkColor.r*.2f,1),s+sparkColor.g*.2f,value+sparkColor.b*.2f,true);
                        float Overlay(float a,float b) => Mathf.Clamp01(a>=.5f ? 1-2*(1-a)*(1-b) : 2*a*b);
                        var mixed=new Color(Overlay(shifted.r,sparkColor.r),Overlay(shifted.g,sparkColor.g),Overlay(shifted.b,sparkColor.b));
                        var expected=(new Color(.7132075f,.23549296f,.23549296f)-Color.white*(.5f*.411f))*(1-.667f)+mixed*.667f+palette[bar]*(1-.667f);
                        var actual=pixels.GetPixel(x,y);
                        if(Math.Abs(actual.r-Mathf.Clamp01(expected.r))>.02f || Math.Abs(actual.g-Mathf.Clamp01(expected.g))>.02f || Math.Abs(actual.b-Mathf.Clamp01(expected.b))>.02f)
                            throw new Exception($"Native noise color/particle mismatch {size} at {x},{y}: {actual} vs {expected}");
                    }
                    VerifyNativeWarningParticles(size,displacement,sparks,Spark);
                    if(size.x==512) {
                        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","FilterPreview")); Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder,"NOISE_NOTE_COLOR_REFERENCE.png"),pixels.EncodeToPNG());
                    }
                } finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(pixels); }
            }
        } finally {
            RenderTexture.active=oldTarget;
            foreach(var item in new UnityEngine.Object[]{material,displacement,masks,styles,sparks}) UnityEngine.Object.DestroyImmediate(item);
        }
        Debug.Log("NOISE NATIVE COLOR PASS: yellow/blue note colors, HSV refraction, premultiplied blending and native particle UV at three resolutions/aspect ratios.");
    }

    private static void VerifyNativeWarningParticles(Vector2Int size,Texture displacement,Texture sparks,Func<int,int,float> sparkAt)
    {
        var material=new Material(Resources.Load<Shader>("AlphaNoiseZone"));
        material.SetTexture("_DisplaceTex",displacement); material.SetTexture("_SparkTex",sparks); material.SetFloat("_ZoneTime",0);
        var mesh=new Mesh { vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up},
            uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},
            colors=Enumerable.Repeat(new Color(1,1,1,.75f),4).ToArray(),triangles=new[]{0,1,2,0,2,3} };
        mesh.SetUVs(1,Enumerable.Repeat(Vector2.right,4).ToList());
        var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create();
        var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false,true);
        var oldTarget=RenderTexture.active;
        try {
            RenderTexture.active=target; GL.Clear(true,true,new Color(.12f,.12f,.12f,1));
            GL.PushMatrix();
            try { GL.LoadOrtho(); material.SetPass(0); Graphics.DrawMeshNow(mesh,Matrix4x4.identity); }
            finally { GL.PopMatrix(); }
            pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0); pixels.Apply();
            for(int step=0;step<25;step++) {
                int x=7+step*(size.x-16)/25,y=17+(step*37)%(size.y-34);
                float u=(x+.5f)/size.x,v=(y+.5f)/size.y;
                float spark=sparkAt((int)(Mathf.Repeat(u*3,1)*128),(int)(Mathf.Repeat(v*1.2f,1)*128));
                var expected=new Color(.12f,.12f,.12f)+(new Color(.497f,.13766898f,.13766898f)*.4f+
                    new Color(.31132078f,.077830195f,.077830195f)*spark*.5f*3.5f)*.75f;
                var actual=pixels.GetPixel(x,y);
                if(Math.Abs(actual.r-expected.r)>.02f || Math.Abs(actual.g-expected.g)>.02f || Math.Abs(actual.b-expected.b)>.02f)
                    throw new Exception($"Native warning blend/particle size mismatch {size} at {x},{y}: {actual} vs {expected}");
            }
        } finally {
            RenderTexture.active=oldTarget; target.Release();
            foreach(var item in new UnityEngine.Object[]{material,mesh,target,pixels}) UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
}
#endif
