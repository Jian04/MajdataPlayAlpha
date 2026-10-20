using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using MajdataPlay.Scenes.Game;

namespace MajdataPlay.Alpha
{
    internal sealed class AlphaNoiseRenderPass : ScriptableRenderPass
    {
        AlphaVisualTimeline timeline;
        AlphaNoiseController owner;
        readonly AlphaNoiseRenderer compositor=new();
        RTHandle maskHandle;
        internal AlphaNoiseRenderPass() { renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing-1; requiresIntermediateTexture=true; ConfigureInput(ScriptableRenderPassInput.Color); }
        internal bool Prepare(AlphaVisualTimeline value)
        {
            if(timeline!=value || !owner) {
                Reset(); timeline=value;
                if(!value.HasNoise) return false;
                owner=new GameObject("Alpha noise fields").AddComponent<AlphaNoiseController>();
                owner.Configure(value.NoiseEvents,()=>Majdata<INoteController>.Instance?.ThisFrameSec ?? 0,value);
            }
            owner.RenderAt(owner.Time);
            return value.HasNoise;
        }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
        {
            if(!owner || (!owner.HasActive && !owner.HasWarnings)) return;
            var resources=frameData.Get<UniversalResourceData>(); if(resources.isActiveTargetBackBuffer) return;
            var camera=frameData.Get<UniversalCameraData>();
            if (owner.HasActive)
            {
                var descriptor=graph.GetTextureDesc(resources.activeColorTexture);
                var material=compositor.Prepare(owner,camera.camera,descriptor.width,descriptor.height); if(!material) return;
                var masks=(RenderTexture)material.GetTexture("_Masks");
                if(maskHandle==null || maskHandle.rt!=masks) { maskHandle?.Release(); maskHandle=RTHandles.Alloc(masks); }
                var mask=graph.ImportTexture(maskHandle);
                var properties=new MaterialPropertyBlock();
                properties.SetTexture("_Masks",masks);
                properties.SetTexture("_Styles",material.GetTexture("_Styles"));
                properties.SetTexture("_DisplaceTex",material.GetTexture("_DisplaceTex"));
                properties.SetTexture("_SparkTex",material.GetTexture("_SparkTex"));
                properties.SetVector("_OutputSize",material.GetVector("_OutputSize"));
                properties.SetFloat("_ZoneTime",(float)owner.Time);
                properties.SetFloat("_AlphaClipSpace",1);
                properties.SetVector("_PlayfieldRect",AlphaScreenRenderFeature.FieldRect(camera.camera));
                descriptor.name="Alpha noise final composite"; descriptor.clearBuffer=false; descriptor.msaaSamples=MSAASamples.None;
                var destination=graph.CreateTexture(descriptor);
                var parameters=new RenderGraphUtils.BlitMaterialParameters(resources.activeColorTexture,destination,material,3,properties,sourceTexturePropertyID:Shader.PropertyToID("_MainTex"));
                using(var builder=graph.AddBlitPass(parameters,"Alpha noise final composite",returnBuilder:true)) builder.UseTexture(mask);
                resources.cameraColor=destination;
            }
        }
        internal void Reset() { timeline=null; if(owner) { owner.Clear(); if(Application.isPlaying) Object.Destroy(owner.gameObject); else Object.DestroyImmediate(owner.gameObject); } owner=null; }
        internal void Dispose() { Reset(); maskHandle?.Release(); maskHandle=null; compositor.Dispose(); }
    }
}
