#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using MajdataPlay.Alpha;
using System.Linq;
using UnityEngine.Rendering.Universal;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;

namespace MajdataPlay.Editor
{
    // Optional batch check: -executeMethod MajdataPlay.Editor.AlphaVisualVerification.Run
    public static class AlphaVisualVerification
    {
        public static void Run()
        {
            var fallback = Resources.Load<Material>("AlphaNote");
            Require(fallback && fallback.shader.name == "Sprites/AlphaNoteTint", "Alpha material missing from Resources");
            foreach (var path in new[] {
                "Assets/Scripts/Alpha/AlphaNoteTint.shader",
                "Assets/Rendering/Shaders/BrightnessAdjustAnimate.shader",
                "Assets/Rendering/Shaders/HoldShineShader.shader",
                "Assets/Rendering/Shaders/ColorAdjustEffect.shader" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Require(shader, "Missing shader: " + path);
                using var holder = new MaterialHolder(shader);
                Require(holder.Material.HasProperty("_NoteAlpha") && holder.Material.HasProperty("_NoteColor"), "Missing Alpha properties: " + path);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", path + ": " + message.message);
            }
            CheckScreenFilters();
            VignetteGradientVerification.Run();
            CheckRenderPipeline();
            var obj = new GameObject("Alpha material verification");
            try
            {
                var renderer = obj.AddComponent<SpriteRenderer>();
                var original = renderer.sharedMaterial;
                MaterialPropertyBlock block = null;
                AlphaNoteMaterial.Apply(renderer, ref block, 0x00FF00, 0.25f);
                Require(renderer.sharedMaterial == fallback, "Default sprite material was not adapted");
                renderer.GetPropertyBlock(block);
                Require(Math.Abs(block.GetFloat("_NoteAlpha") - 0.25f) < 0.001f, "Opacity missing from sprite properties");
                Require(block.GetColor("_NoteColor").g == 1, "Color missing from sprite properties");
                block.SetFloat("_OtherAnimation", .6f);
                renderer.SetPropertyBlock(block);
                AlphaNoteMaterial.Apply(renderer, ref block, 0x00FF00, 0.25f);
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_OtherAnimation") == .6f, "Cached Alpha properties overwrite native animation");
                AlphaNoteMaterial.Apply(renderer, ref block, null, 1);
                Require(renderer.sharedMaterial == original, "Default material did not restore after reset");
                foreach (var shaderName in new[] { "Custom/BrightnessAdjustAnimate", "Custom/HoldShineShader", "Custom/ColorAdjustEffect" })
                {
                    var shader = Shader.Find(shaderName);
                    // Paths above validate shaders even if upstream changes their public name.
                    if (!shader) continue;
                    using var holder = new MaterialHolder(shader);
                    renderer.sharedMaterial = holder.Material;
                    AlphaNoteMaterial.Apply(renderer, ref block, 0xFF0000, 0);
                    Require(renderer.sharedMaterial == holder.Material, "Native shine material was replaced");
                    renderer.GetPropertyBlock(block);
                    Require(block.GetFloat("_NoteAlpha") == 0, "Zero opacity was lost");
                }
            }
            finally
            {
                AlphaNoteMaterial.Clear();
                UnityEngine.Object.DestroyImmediate(obj);
            }
            Debug.Log("ALPHA VISUAL CHECK PASS: resources, shaders, sprite overrides/reset, renderer feature and filter pixels.");
        }
        static void CheckScreenFilters()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Pipelines/UniversalRenderPipelineAsset_Renderer.asset");
            Require(renderer && renderer.rendererFeatures.Any(feature => feature is AlphaScreenRenderFeature && feature.isActive), "Alpha screen render feature missing");
            var shader = Resources.Load<Shader>("AlphaScreenEffects");
            Require(shader, "Screen effect shader missing");
            using var holder = new MaterialHolder(shader);
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            texture.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.4f, 0.6f, 1), 16).ToArray());
            texture.Apply();
            var source = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var destination = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            Color Sample()
            {
                Graphics.Blit(source, destination, holder.Material, 0);
                RenderTexture.active = destination;
                pixel.ReadPixels(new Rect(8, 8, 1, 1), 0, 0);
                pixel.Apply();
                return pixel.GetPixel(0, 0);
            }
            try
            {
                Graphics.Blit(texture, source);
                var original = Sample();
                Require(Math.Abs(original.r - 0.2f) < 0.035f && Math.Abs(original.b - 0.6f) < 0.035f, "Disabled filters changed pixels");
                holder.Material.SetFloat("_Flash", 1);
                Require(Sample().r > 0.95f, "FLASH did not render white");
                holder.Material.SetFloat("_Flash", -1);
                Require(Sample().b < 0.035f, "FADE did not render black");
                holder.Material.SetVector("_PlayfieldRect",new Vector4(0,0,1,.5f));
                holder.Material.SetFloat("_Flash",1);
                Graphics.Blit(source,destination,holder.Material,0);
                RenderTexture.active=destination;
                pixel.ReadPixels(new Rect(8,12,1,1),0,0);pixel.Apply();
                Require(Math.Abs(pixel.GetPixel(0,0).b-.6f)<.035f,"Filter affected the score panel outside the playfield");
                pixel.ReadPixels(new Rect(8,2,1,1),0,0);pixel.Apply();
                Require(pixel.GetPixel(0,0).b>.95f,"Filter did not affect the isolated playfield");
                holder.Material.SetVector("_PlayfieldRect",new Vector4(0,0,1,1));
                holder.Material.SetFloat("_Flash", 0);
                holder.Material.SetColor("_TintColor", new Color(1, 0, 0, 1));
                var tinted = Sample();
                Require(tinted.r > tinted.g && tinted.r > tinted.b, "TINT did not recolor pixels");
                // Exercise the same 0..1 quad geometry used by URP's render graph.
                holder.Material.SetColor("_TintColor", Color.clear);
                holder.Material.SetFloat("_AlphaClipSpace", 1);
                holder.Material.SetTexture("_MainTex", source);
                var quad = new Mesh
                {
                    vertices = new[] { new Vector3(0, 1), new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1) },
                    uv = new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 }
                };
                var command = new UnityEngine.Rendering.CommandBuffer();
                try
                {
                    command.SetRenderTarget(destination);
                    command.ClearRenderTarget(false, true, Color.black);
                    command.DrawMesh(quad, Matrix4x4.identity, holder.Material, 0, 0);
                    Graphics.ExecuteCommandBuffer(command);
                    RenderTexture.active = destination;
                    foreach (var point in new[] { new Vector2(2, 2), new Vector2(13, 2), new Vector2(2, 13), new Vector2(13, 13) })
                    {
                        pixel.ReadPixels(new Rect(point.x, point.y, 1, 1), 0, 0);
                        pixel.Apply();
                        Require(Math.Abs(pixel.GetPixel(0, 0).b - 0.6f) < 0.035f, "RenderGraph filter did not cover the full frame");
                    }
                }
                finally { command.Dispose(); UnityEngine.Object.DestroyImmediate(quad); }
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", message.message);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(source);
                RenderTexture.ReleaseTemporary(destination);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(pixel);
            }
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void CheckRenderPipeline()
        {
            Require(Majdata<INoteController>.IsNull, "Run the batch check outside gameplay");
            var obj = new GameObject("Alpha URP verification camera");
            var camera = obj.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            obj.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            var clock = new VerificationClock();
            Majdata<INoteController>.Instance = clock;
            void Render()
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
            }
            float GreenAt(int x, int y)
            {
                pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
                pixel.Apply();
                return pixel.GetPixel(0, 0).g;
            }
            try
            {
                AlphaSimaiParser.Activate(AlphaSimaiParser.ParseChart("", "", "(120){4}<FLASH*(Instant,1,1)>1,E"));
                clock.Time = 0.25f;
                Render();
                Require(GreenAt(3, 3) > 0.95f && GreenAt(28, 28) > 0.95f, "URP did not run FLASH across the full camera image");
                AlphaSimaiParser.Activate(AlphaSimaiParser.ParseChart("", "", "(120){4}<TRAIL*(True,1)>1,E"));
                camera.backgroundColor = Color.green;
                Render();
                camera.backgroundColor = Color.black;
                clock.Time += 0.1f;
                Render();
                Require(GreenAt(16, 16) > 0.5f, "URP trail history was not retained/read correctly");
                AlphaSimaiParser.Clear();
                Render();
                Require(GreenAt(16, 16) < 0.035f, "Previous chart screen effect leaked after reset");
                // Solid colors cannot detect an upside-down or mirrored frame.
                // A later command still enables the pass at song entry, as in 7wonders.
                camera.orthographic = true;
                camera.orthographicSize = 1;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.cullingMask = 1 << 31;
                var white = new Texture2D(2, 2);
                white.SetPixels(Enumerable.Repeat(Color.white, 4).ToArray()); white.Apply();
                var sprite = Sprite.Create(white, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2);
                var corners = new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(-.5f, .5f), new Vector2(.5f, .5f) };
                var colors = new[] { Color.red, Color.green, Color.blue, Color.yellow };
                var markers = new GameObject[4];
                Color[] CaptureCorners()
                {
                    Render();
                    var result = new Color[4];
                    for (var i = 0; i < 4; i++)
                    {
                        pixel.ReadPixels(new Rect(i % 2 == 0 ? 8 : 24, i < 2 ? 8 : 24, 1, 1), 0, 0);
                        pixel.Apply(); result[i] = pixel.GetPixel(0, 0);
                    }
                    return result;
                }
                try
                {
                    for (var i = 0; i < 4; i++)
                    {
                        markers[i] = new GameObject("Orientation marker " + i) { layer = 31 };
                        markers[i].transform.position = corners[i];
                        var marker = markers[i].AddComponent<SpriteRenderer>(); marker.sprite = sprite; marker.color = colors[i];
                    }
                    var baseline = CaptureCorners();
                    Require(baseline.Distinct().Count() == 4, "Orientation markers were not rendered distinctly");
                    AlphaSimaiParser.Activate(AlphaSimaiParser.ParseChart("", "", "(120){4}1,,,,<ROTATE*(True,360,10)>1,E"));
                    clock.Time = 0;
                    var filtered = CaptureCorners();
                    for (var i = 0; i < 4; i++)
                        Require(Vector4.Distance(baseline[i], filtered[i]) < .06f, "Inactive future filter flipped camera corner " + i + ": " + baseline[i] + " -> " + filtered[i]);
                    Debug.Log("ALPHA ORIENTATION CHECK PASS: four distinct camera corners unchanged before a future chart filter.");
                }
                finally
                {
                    foreach (var marker in markers) if (marker) UnityEngine.Object.DestroyImmediate(marker);
                    UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(white);
                }
                Debug.Log("ALPHA URP CHECK PASS: camera pass, full-frame FLASH, trail graph resources and chart reset.");
            }
            finally
            {
                AlphaSimaiParser.Clear();
                Majdata<INoteController>.Instance = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixel);
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        sealed class VerificationClock : INoteController
        {
            public float Time;
            public float ThisFrameSec => Time;
            public float AudioLength => 10;
            public bool IsStart => true;
            public bool IsAutoplay => true;
            public GameModInfo ModInfo => default;
            public JudgeGrade AutoplayGrade => JudgeGrade.Perfect;
            public Material BreakMaterial => null;
            public Material DefaultMaterial => null;
            public Material HoldShineMaterial => null;
        }
        sealed class MaterialHolder : IDisposable
        {
            public Material Material { get; }
            public MaterialHolder(Shader shader) => Material = new Material(shader);
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Material);
        }
    }
}
#endif
