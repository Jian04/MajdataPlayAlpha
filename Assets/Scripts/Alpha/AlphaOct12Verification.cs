#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace MajdataPlay.Alpha
{
    public static class AlphaOct12Verification
    {
        public static void Build()
        {
            Run();
            MajdataPlay.Editor.AlphaNoisePatchBuild.Build();
        }

        public static void Run()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(AlphaScreenRenderFeature).GetNestedType("ScreenPass", BindingFlags.NonPublic);
            var pass = Activator.CreateInstance(type, true);
            var prepare = type.GetMethod("Prepare");
            var configure = type.GetMethod("Configure", fields);
            var source = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
                    texture.SetPixel(x, y, Math.Abs(x - 128) < 8 && Math.Abs(y - 128) < 8 ? Color.white : Color.black);
                texture.Apply(); Graphics.Blit(texture, source);
                foreach (var angle in new[] { 0, 90 })
                {
                    var chart = AlphaSimaiParser.ParseChart("", "", $"(120){{4}}<SHAKE*(Instant,1,10,{angle},0.2)>1,");
                    var timeline = AlphaSimaiParser.GetTimeline(chart);
                    foreach (var time in new[] { .05f, .1f, .15f })
                    {
                        prepare.Invoke(pass, new object[] { timeline, time, new Vector4(0, 0, 1, 1) });
                        configure.Invoke(pass, new object[] { 256, 256, false });
                        var material = (Material)type.GetField("_material", fields).GetValue(pass);
                        Graphics.Blit(source, target, material);
                        RenderTexture.active = target;
                        texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
                        float weight = 0, px = 0, py = 0;
                        for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
                        {
                            var light = texture.GetPixel(x, y).r;
                            weight += light; px += x * light; py += y * light;
                        }
                        px = px / weight - 128; py = py / weight - 128;
                        if (Mathf.Abs(angle == 0 ? py : px) > .05f || Mathf.Abs(angle == 0 ? px : py) < .1f)
                            throw new Exception($"Play Shake {angle} axis incorrect at {time}: ({px}, {py})");
                        Debug.Log($"PLAY SHAKE GPU PASS: Instant,1,10,{angle},0.2 at {time}: ({px}, {py}) pixels");
                    }
                }
            }
            finally
            {
                RenderTexture.active = previous;
                type.GetMethod("Dispose").Invoke(pass, null);
                RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
