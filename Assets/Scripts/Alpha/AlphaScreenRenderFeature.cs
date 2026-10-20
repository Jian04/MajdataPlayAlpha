using System;
using System.Linq;
using MajdataPlay.Scenes.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace MajdataPlay.Alpha
{
    // Final image processing only: no transform, collider, input or judgment writes.
    public sealed class AlphaScreenRenderFeature : ScriptableRendererFeature
    {
        ScreenPass _pass;
        AlphaNoiseRenderPass _noise;
        public override void Create()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            _pass?.Dispose();
            _noise?.Dispose(); _noise=new AlphaNoiseRenderPass();
            _pass = new ScreenPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing, requiresIntermediateTexture = true };
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera.cameraType!=CameraType.Game || camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType!=CameraRenderType.Base) return;
            var controller=Majdata<INoteController>.Instance;
            if(controller is GamePlayManager gameplay && !gameplay.IsStart) { _noise?.Reset();return; }
            // Build warning geometry before culling, including the first frame
            // after loading or seeking. AddRenderPasses runs after culling.
            if(controller!=null && AlphaSimaiParser.Current.HasNoise) _noise?.Prepare(AlphaSimaiParser.Current);
            else _noise?.Reset();
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game || renderingData.cameraData.renderType != CameraRenderType.Base) return;
            var controller = Majdata<INoteController>.Instance;
            // Loading/error screens must not sample the uninitialized song clock.
            if (controller is GamePlayManager gameplay && !gameplay.IsStart) { _pass?.Reset(); _noise?.Reset(); return; }
            var timeline = AlphaSimaiParser.Current;
            if(controller!=null && timeline.HasNoise) { if(_noise.Prepare(timeline)) renderer.EnqueuePass(_noise); } else _noise?.Reset();
            if (controller == null || !timeline.HasScreenEffects) { _pass?.Reset(); return; }
            if (!timeline.NeedsScreenRender(controller.ThisFrameSec)) { _pass?.Reset(); return; }
            if (_pass != null && _pass.Prepare(timeline, controller.ThisFrameSec, FieldRect(renderingData.cameraData.camera))) renderer.EnqueuePass(_pass);
        }
        static RectTransform _playfield;
        static readonly Vector3[] _corners = new Vector3[4];
        internal static Vector4 FieldRect(Camera camera)
        {
            if (!_playfield)
            {
                var mover = UnityEngine.Object.FindObjectsByType<ScreenPosCanvasMover>(FindObjectsSortMode.None).FirstOrDefault();
                if (mover) _playfield = mover.GetComponent<RectTransform>();
            }
            if (!_playfield) return new Vector4(0, 0, 1, 1);
            _playfield.GetWorldCorners(_corners);
            var lower = camera.WorldToViewportPoint(_corners[0]);
            var upper = camera.WorldToViewportPoint(_corners[2]);
            return new Vector4(Mathf.Min(lower.x, upper.x), Mathf.Min(lower.y, upper.y), Mathf.Max(lower.x, upper.x), Mathf.Max(lower.y, upper.y));
        }
        protected override void Dispose(bool disposing) { RenderPipelineManager.beginCameraRendering-=BeforeCamera; _pass?.Dispose(); _noise?.Dispose(); }

        sealed class ScreenPass : ScriptableRenderPass
        {
            Material _material;
            AlphaVisualTimeline _timeline;
            float _time, _lastTime = float.NaN;
            readonly RTHandle[] _history = new RTHandle[3];
            readonly TextureHandle[] _historyHandles = new TextureHandle[3];
            bool _keepHistory;
            bool _seed;
            int _frames;
            Vector4 _playfieldRect;
            static readonly int InputId = Shader.PropertyToID("_AlphaScreenInput");
            static readonly int OutputId = Shader.PropertyToID("_AlphaScreenOutput");
            static readonly int MainTexId = Shader.PropertyToID("_MainTex");
            static readonly string[] Floats = { "_Blur", "_Neon", "_Fade", "_Trail", "_Brightness", "_Saturation", "_Contrast", "_Rainbow", "_EffectTime", "_Flash", "_Vignette", "_VignetteGradient", "_Zoom", "_Glitch", "_TVNoise", "_Hue", "_OffsetX", "_OffsetY", "_Rotate", "_ShatterPhase", "_RipplePhase", "_KaleidoscopeAngle", "_AlphaClipSpace" };
            public bool Prepare(AlphaVisualTimeline timeline, float time, Vector4 playfieldRect)
            {
                if (_timeline != timeline || !float.IsNaN(_lastTime) && (time < _lastTime || time - _lastTime > 0.5f)) Reset();
                _timeline = timeline;
                _time = time;
                _playfieldRect = playfieldRect;
                _keepHistory = timeline.NeedsTrailHistory(time);
                if (!_keepHistory && _history[0] != null) ReleaseHistory();
                if (!_material)
                {
                    var shader = Resources.Load<Shader>("AlphaScreenEffects");
                    if (shader) _material = CoreUtils.CreateEngineMaterial(shader);
                }
                return _material;
            }
            float Value(string name) => _timeline.ScreenEffect(name, _time, out _);
            void Configure(int width, int height, bool graph)
            {
                _material.SetVector("_PlayfieldRect", _playfieldRect);
                foreach(var filter in MajdataCore.AlphaExtraFilters.All) {
                    var amount=_timeline.ScreenEffect(filter.Canonical,_time,out var ev);
                    _material.SetFloat(filter.Uniform,filter.Canonical=="Kaleidoscope" ? _timeline.KaleidoscopeVisibility(_time) : amount);
                    if(filter.Canonical=="Shatter") CoreUtils.SetKeyword(_material,"ALPHA_SHATTER",amount>.0001f);
                    if(filter.Canonical=="Kaleidoscope") _material.SetFloat("_KaleidoscopeAngle",amount*Mathf.Deg2Rad);
                    if(filter.Canonical is "Shatter" or "Ripple") _material.SetFloat("_"+filter.Canonical+"Phase",ev==null?0:_time-(float)ev.time);
                }
                _material.SetFloat("_Blur", Value("Gaussian"));
                _material.SetFloat("_Neon", Value("Neon"));
                _material.SetFloat("_Fade", 0);
                _material.SetFloat("_Trail", Mathf.Clamp01(Value("Trail")));
                _material.SetFloat("_Brightness", Value("Brightness"));
                _material.SetFloat("_Saturation", Mathf.Clamp01(Value("Saturation")));
                _material.SetFloat("_Contrast", Value("Contrast"));
                _material.SetFloat("_Rainbow", Mathf.Clamp01(Value("Rainbow")));
                _material.SetFloat("_EffectTime", _time);
                _material.SetFloat("_Flash", Mathf.Clamp(Value("Flash"), -1, 1));
                var vignette = _timeline.ScreenEffect("Vignette", _time, out var vignetteEvent);
                _material.SetFloat("_Vignette", Mathf.Clamp01(vignette));
                _material.SetFloat("_VignetteGradient", vignetteEvent?.gradient == true ? 1f : 0f);
                _material.SetFloat("_Zoom", Value("Zoom"));
                _material.SetFloat("_Glitch", Mathf.Clamp01(Value("Glitch")));
                _material.SetFloat("_TVNoise", Mathf.Clamp01(Value("TVNoise")));
                _material.SetFloat("_Hue", Value("Hue") * Mathf.Deg2Rad);
                // Sampling uses the inverse transform of ViewAlpha's visual rotation.
                _material.SetFloat("_Rotate", -Value("Rotate") * Mathf.Deg2Rad);
                var tint = _timeline.ScreenEffect("Tint", _time, out var tintEvent);
                var color = Color.black;
                if (tintEvent != null && !string.IsNullOrEmpty(tintEvent.color)) ColorUtility.TryParseHtmlString("#" + tintEvent.color, out color);
                color.a = Mathf.Clamp01(tint);
                _material.SetColor("_TintColor", color);
                _timeline.ScreenMove(_time, out var x, out var y);
                var shake = _timeline.ScreenEffect("Shake", _time, out var shakeEvent);
                if (shakeEvent != null && Mathf.Abs(shake) > 0.0001f)
                {
                    var phase = _time * (shakeEvent.paramA > 0 ? shakeEvent.paramA : 18);
                    var displacement = (Mathf.PerlinNoise(phase, 0.37f) - 0.5f) * 2 * shake * 0.05f;
                    x += shakeEvent.hasDirection ? Mathf.Cos(shakeEvent.paramB) * displacement : displacement;
                    y += shakeEvent.hasDirection ? Mathf.Sin(shakeEvent.paramB) * displacement : (Mathf.PerlinNoise(0.71f, phase) - 0.5f) * 2 * shake * 0.05f;
                }
                _material.SetFloat("_OffsetX", x);
                _material.SetFloat("_OffsetY", y);
                _material.SetFloat("_AlphaClipSpace", graph ? 1 : 0);
                _material.SetVector("_MainTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
                for (var i = 0; i < 3; i++) _material.SetTexture(i == 0 ? "_HistoryTex" : "_HistoryTex" + (i + 1), _history[i] != null ? _history[i].rt : Texture2D.blackTexture);
            }
            void EnsureHistory(RenderTextureDescriptor descriptor)
            {
                if (!_keepHistory) return;
                if (_history[0] != null && _history[0].rt.width == descriptor.width && _history[0].rt.height == descriptor.height) return;
                ReleaseHistory();
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                for (var i = 0; i < 3; i++) _history[i] = RTHandles.Alloc(descriptor, name: "AlphaTrail" + i);
                _seed = true;
            }
            bool AdvanceHistory() => _keepHistory && _time != _lastTime && ++_frames >= 5;

#if !UNITY_6000_4_OR_NEWER
#pragma warning disable CS0672, CS0618
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                EnsureHistory(descriptor);
                Configure(descriptor.width, descriptor.height, false);
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var cmd = CommandBufferPool.Get("Alpha screen filters");
                try
                {
                    cmd.GetTemporaryRT(InputId, descriptor, FilterMode.Bilinear);
                    cmd.GetTemporaryRT(OutputId, descriptor, FilterMode.Bilinear);
                    cmd.Blit(source.nameID, InputId);
                    if (_seed)
                    {
                        foreach (var history in _history) cmd.Blit(InputId, history.nameID);
                        _seed = false;
                    }
                    cmd.Blit(InputId, OutputId, _material, 0);
                    cmd.Blit(OutputId, source.nameID);
                    if (AdvanceHistory())
                    {
                        cmd.Blit(_history[1].nameID, _history[2].nameID);
                        cmd.Blit(_history[0].nameID, _history[1].nameID);
                        cmd.Blit(InputId, _history[0].nameID);
                        _frames = 0;
                    }
                    cmd.ReleaseTemporaryRT(InputId);
                    cmd.ReleaseTemporaryRT(OutputId);
                    context.ExecuteCommandBuffer(cmd);
                    _lastTime = _time;
                }
                finally { CommandBufferPool.Release(cmd); }
            }
#pragma warning restore CS0672, CS0618
#endif
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var camera = frameData.Get<UniversalCameraData>();
                EnsureHistory(camera.cameraTargetDescriptor);
                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Alpha screen filters";
                descriptor.clearBuffer = false;
                descriptor.msaaSamples = MSAASamples.None;
                var destination = graph.CreateTexture(descriptor);
                var histories = _historyHandles;
                if (_keepHistory)
                {
                    for (var i = 0; i < 3; i++) histories[i] = graph.ImportTexture(_history[i]);
                    if (_seed)
                    {
                        foreach (var history in histories) graph.AddBlitPass(source, history, Vector2.one, Vector2.zero, passName: "Alpha trail seed");
                        _seed = false;
                    }
                }
                Configure(descriptor.width, descriptor.height, true);
                var properties = new MaterialPropertyBlock();
                foreach (var name in Floats) properties.SetFloat(name, _material.GetFloat(name));
                foreach(var filter in MajdataCore.AlphaExtraFilters.All) properties.SetFloat(filter.Uniform,_material.GetFloat(filter.Uniform));
                properties.SetColor("_TintColor", _material.GetColor("_TintColor"));
                properties.SetVector("_PlayfieldRect", _playfieldRect);
                properties.SetVector("_MainTex_TexelSize", _material.GetVector("_MainTex_TexelSize"));
                for (var i = 0; i < 3; i++) properties.SetTexture(i == 0 ? "_HistoryTex" : "_HistoryTex" + (i + 1), _history[i] != null ? _history[i].rt : Texture2D.blackTexture);
                var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0, properties, sourceTexturePropertyID: MainTexId);
                using (var builder = graph.AddBlitPass(parameters, "Alpha screen filters", returnBuilder: true))
                    if (_keepHistory) foreach (var history in histories) builder.UseTexture(history);
                resources.cameraColor = destination;
                if (AdvanceHistory())
                {
                    graph.AddBlitPass(histories[1], histories[2], Vector2.one, Vector2.zero, passName: "Alpha trail oldest");
                    graph.AddBlitPass(histories[0], histories[1], Vector2.one, Vector2.zero, passName: "Alpha trail middle");
                    graph.AddBlitPass(source, histories[0], Vector2.one, Vector2.zero, passName: "Alpha trail recent");
                    _frames = 0;
                }
                _lastTime = _time;
            }
            void ReleaseHistory()
            {
                for (var i = 0; i < 3; i++) { _history[i]?.Release(); _history[i] = null; }
                _seed = false;
                _frames = 0;
            }
            public void Reset() { ReleaseHistory(); _keepHistory = false; _lastTime = float.NaN; _timeline = null; }
            public void Dispose() { Reset(); CoreUtils.Destroy(_material); _material = null; }
        }
    }
}
