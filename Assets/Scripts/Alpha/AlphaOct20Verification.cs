#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
namespace MajdataPlay.Editor
{
    public static class AlphaOct20Verification
    {
        public static void Run()
        {
            AlphaFakeVerification.Run();
            AlphaVisualVerification.Run();
            AlphaNoiseVerification.Run();
            MajdataPlay.Alpha.AlphaOct12Verification.Run();
            CheckShatterVariant();
        }
        static void CheckShatterVariant()
        {
            var type = typeof(MajdataPlay.Alpha.AlphaScreenRenderFeature).GetNestedType("ScreenPass", BindingFlags.NonPublic);
            var pass = Activator.CreateInstance(type, true);
            var source = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            var target = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
                    source.SetPixel(x, y, new Color(x / 63f, y / 63f, (x / 8 + y / 8) % 2, 1));
                source.Apply();
                var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SHATTER*(Instant,1,0.2)>1,");
                var timeline = MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(chart);
                Color32[] Sample(float time)
                {
                    type.GetMethod("Prepare").Invoke(pass, new object[] { timeline, time, new Vector4(0, 0, 1, 1) });
                    type.GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pass, new object[] { 64, 64, false });
                    var material = (Material)type.GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
                    if (material.IsKeywordEnabled("ALPHA_SHATTER") != (time >= 0 && time < .2f)) throw new Exception("Shatter variant did not follow its active interval");
                    Graphics.Blit(source, target, material);
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                    return pixels.GetPixels32();
                }
                var before = Sample(-.1f); var active = Sample(.08f); var after = Sample(.3f);
                var changed = 0;
                for (var i = 0; i < before.Length; i++)
                {
                    if (!before[i].Equals(after[i])) throw new Exception("Disabling Shatter changed the base picture");
                    if (!before[i].Equals(active[i])) changed++;
                }
                if (changed < 100) throw new Exception("Active Shatter variant did not render");
                Debug.Log("SHATTER VARIANT GPU PASS: active glass fragments rendered; inactive/reset pixels identical.");
            }
            finally
            {
                RenderTexture.active = previous; type.GetMethod("Dispose").Invoke(pass, null);
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
#endif
