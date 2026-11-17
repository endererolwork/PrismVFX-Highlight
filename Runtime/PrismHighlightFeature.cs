using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace PrismFX
{
    // Lets the same draw code target CommandBuffer (compat) and RasterCommandBuffer (Render Graph)
    // without boxing: Render<T> is constrained to structs.
    public interface IPrismDrawSink
    {
        void DrawRenderer(Renderer renderer, Material material, int submesh, int pass);
        void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int pass);
        void DrawFullscreen(Material material, int pass, MaterialPropertyBlock properties);
    }

    public struct PrismCmdSink : IPrismDrawSink
    {
        public CommandBuffer cmd;
        public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass) => cmd.DrawRenderer(renderer, material, submesh, pass);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int pass) => cmd.DrawMesh(mesh, matrix, material, 0, pass);
        public void DrawFullscreen(Material material, int pass, MaterialPropertyBlock properties)
            => cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 1, properties);
    }

#if UNITY_6000_0_OR_NEWER
    public struct PrismRasterSink : IPrismDrawSink
    {
        public RasterCommandBuffer cmd;
        public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass) => cmd.DrawRenderer(renderer, material, submesh, pass);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int pass) => cmd.DrawMesh(mesh, matrix, material, 0, pass);
        public void DrawFullscreen(Material material, int pass, MaterialPropertyBlock properties)
            => cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 1, properties);
    }
#endif

    /// <summary>
    /// Add this to your URP renderer (or run Tools > PrismFX > Setup Project).
    /// </summary>
    public class PrismHighlightFeature : ScriptableRendererFeature
    {
        [Tooltip("Before post processing so the HDR glow gets picked up by bloom.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public bool renderInSceneView = true;
        [Tooltip("URP leaves bits 0-3 to users. Change this if something else in your project already uses 8.")]
        public StencilBitChoice stencilBit = StencilBitChoice.Bit3_Value8;

        public enum StencilBitChoice { Bit0_Value1 = 1, Bit1_Value2 = 2, Bit2_Value4 = 4, Bit3_Value8 = 8 }

        [SerializeField, HideInInspector] Shader _focusShader;

        PrismHighlightPass _pass;
        Material _focusMaterial;

        // only used for the inspector warning
        public static bool IsPresent { get; private set; }

        const string FocusShaderName = "Hidden/PrismFX/Focus";

        static readonly int ID_StencilBit = Shader.PropertyToID("_PrismStencilBit");
        static readonly int ID_FocusColor = Shader.PropertyToID("_FocusColor");
        static readonly int ID_FocusParams = Shader.PropertyToID("_FocusParams");
        static readonly int ID_FocusBlurRadius = Shader.PropertyToID("_FocusBlurRadius");

        void OnValidate()
        {
            if (_focusShader == null) _focusShader = Shader.Find(FocusShaderName);
        }

        public override void Create()
        {
            if (_focusShader == null) _focusShader = Shader.Find(FocusShaderName);
            if (_focusMaterial == null && _focusShader != null) _focusMaterial = CoreUtils.CreateEngineMaterial(_focusShader);

            _pass = new PrismHighlightPass { focusMaterial = _focusMaterial };
            IsPresent = true;
            if (PrismHighlight.StencilBit != (int)stencilBit)
            {
                PrismHighlight.StencilBit = (int)stencilBit;
                PrismHighlight.RefreshAllMaterials();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            CoreUtils.Destroy(_focusMaterial);
            _focusMaterial = null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PrismHighlight.Active.Count == 0) return;
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (type == CameraType.SceneView && !renderInSceneView) return;
            _pass.renderPassEvent = renderPassEvent;
#if UNITY_6000_0_OR_NEWER
            // Focus reads the camera color, which needs an intermediate texture. Only ask for one
            // while a focus is actually showing, so normal frames keep rendering straight through.
            _pass.requiresIntermediateTexture = _focusMaterial != null && PrismHighlight.FindFocus(out _) != null;
#endif
            renderer.EnqueuePass(_pass);
        }

        internal static void DrawAll<T>(ref T sink, Camera camera) where T : struct, IPrismDrawSink
        {
            var list = PrismHighlight.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                if (h != null) h.Render(ref sink, camera);
            }
        }

        // Mask every focused object, one fullscreen triangle for the rest of the scene, clear the mask.
        internal static void DrawFocus<T>(ref T sink, Camera camera, Material material, MaterialPropertyBlock props)
            where T : struct, IPrismDrawSink
        {
            var dominant = PrismHighlight.FindFocus(out float amount);
            if (dominant == null || material == null) return;

            var s = dominant.settings;
            material.SetFloat(ID_StencilBit, PrismHighlight.StencilBit);
            material.SetColor(ID_FocusColor, s.focusColor);
            material.SetVector(ID_FocusParams, new Vector4(amount, s.focusDim, s.focusDesaturate, s.focusBlur));
            material.SetFloat(ID_FocusBlurRadius, s.focusBlurRadius);

            var list = PrismHighlight.Active;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) list[i].DrawFocusMask(ref sink, camera);

            sink.DrawFullscreen(material, 0, props);

            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) list[i].DrawFocusClear(ref sink, camera);
        }
    }

    class PrismHighlightPass : ScriptableRenderPass
    {
        const string PassName = "PrismFX Highlight";
        internal Material focusMaterial;

        static readonly int ID_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int ID_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly MaterialPropertyBlock s_FocusProps = new MaterialPropertyBlock();

        RTHandle _focusCopy; // compatibility mode only

        public void Dispose()
        {
            _focusCopy?.Release();
            _focusCopy = null;
        }

        static void SetFocusSource(RTHandle source)
        {
            s_FocusProps.Clear();
            s_FocusProps.SetTexture(ID_BlitTexture, source);
            s_FocusProps.SetVector(ID_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
        }

#if UNITY_6000_0_OR_NEWER
        class PassData
        {
            public Camera camera;
        }

        class CopyData
        {
            public TextureHandle source;
        }

        class FocusData
        {
            public TextureHandle source;
            public Camera camera;
            public Material material;
        }

        static readonly BaseRenderFunc<PassData, RasterGraphContext> s_RenderFunc = ExecutePass;
        static readonly BaseRenderFunc<CopyData, RasterGraphContext> s_CopyFunc = ExecuteCopy;
        static readonly BaseRenderFunc<FocusData, RasterGraphContext> s_FocusFunc = ExecuteFocus;

        static void ExecutePass(PassData data, RasterGraphContext ctx)
        {
            var sink = new PrismRasterSink { cmd = ctx.cmd };
            PrismHighlightFeature.DrawAll(ref sink, data.camera);
        }

        static void ExecuteCopy(CopyData data, RasterGraphContext ctx)
        {
            Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0f, false);
        }

        static void ExecuteFocus(FocusData data, RasterGraphContext ctx)
        {
            RTHandle source = data.source;
            SetFocusSource(source);
            var sink = new PrismRasterSink { cmd = ctx.cmd };
            PrismHighlightFeature.DrawFocus(ref sink, data.camera, data.material, s_FocusProps);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            bool focus = focusMaterial != null && !resources.isActiveTargetBackBuffer
                         && PrismHighlight.FindFocus(out _) != null;
            if (focus)
            {
                // same approach as URP's FullScreenPassRendererFeature: copy, then read the copy
                var desc = renderGraph.GetTextureDesc(resources.cameraColor);
                desc.name = "_PrismFocusCopy";
                desc.clearBuffer = false;
                var copy = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<CopyData>("PrismFX Focus Copy", out var copyData))
                {
                    copyData.source = resources.activeColorTexture;
                    builder.UseTexture(copyData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(copy, 0, AccessFlags.Write);
                    builder.SetRenderFunc(s_CopyFunc);
                }

                using (var builder = renderGraph.AddRasterRenderPass<FocusData>("PrismFX Focus", out var focusData))
                {
                    focusData.source = copy;
                    focusData.camera = cameraData.camera;
                    focusData.material = focusMaterial;
                    builder.UseTexture(copy, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(s_FocusFunc);
                }
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData))
            {
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                passData.camera = cameraData.camera;
                builder.SetRenderFunc(s_RenderFunc);
            }
        }
#endif

#if !UNITY_6000_3_OR_NEWER || URP_COMPATIBILITY_MODE
        // Compatibility Mode (Render Graph off) and 2022.3
#pragma warning disable CS0618, CS0672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get(PassName);
            var camera = renderingData.cameraData.camera;
            var sink = new PrismCmdSink { cmd = cmd };

#if UNITY_6000_0_OR_NEWER
            // Focus in compatibility mode (Unity 6). Not available on 2022.3.
            if (focusMaterial != null && PrismHighlight.FindFocus(out _) != null)
            {
                var renderer = renderingData.cameraData.renderer;
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthStencilFormat = GraphicsFormat.None;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _focusCopy, desc, name: "_PrismFocusCopy");

                CoreUtils.SetRenderTarget(cmd, _focusCopy);
                Blitter.BlitTexture(CommandBufferHelpers.GetRasterCommandBuffer(cmd), renderer.cameraColorTargetHandle,
                    new Vector4(1f, 1f, 0f, 0f), 0f, false);
                CoreUtils.SetRenderTarget(cmd, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);

                SetFocusSource(_focusCopy);
                PrismHighlightFeature.DrawFocus(ref sink, camera, focusMaterial, s_FocusProps);
            }
#endif

            PrismHighlightFeature.DrawAll(ref sink, camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
#endif
    }
}
