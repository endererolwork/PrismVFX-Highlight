using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
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
    }

    public struct PrismCmdSink : IPrismDrawSink
    {
        public CommandBuffer cmd;
        public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass) => cmd.DrawRenderer(renderer, material, submesh, pass);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int pass) => cmd.DrawMesh(mesh, matrix, material, 0, pass);
    }

#if UNITY_6000_0_OR_NEWER
    public struct PrismRasterSink : IPrismDrawSink
    {
        public RasterCommandBuffer cmd;
        public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass) => cmd.DrawRenderer(renderer, material, submesh, pass);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int pass) => cmd.DrawMesh(mesh, matrix, material, 0, pass);
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

        PrismHighlightPass _pass;

        // only used for the inspector warning
        public static bool IsPresent { get; private set; }

        public override void Create()
        {
            _pass = new PrismHighlightPass();
            IsPresent = true;
            if (PrismHighlight.StencilBit != (int)stencilBit)
            {
                PrismHighlight.StencilBit = (int)stencilBit;
                PrismHighlight.RefreshAllMaterials();
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (PrismHighlight.Active.Count == 0) return;
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (type == CameraType.SceneView && !renderInSceneView) return;
            _pass.renderPassEvent = renderPassEvent;
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
    }

    class PrismHighlightPass : ScriptableRenderPass
    {
        const string PassName = "PrismFX Highlight";

#if UNITY_6000_0_OR_NEWER
        class PassData
        {
            public Camera camera;
        }

        static readonly BaseRenderFunc<PassData, RasterGraphContext> s_RenderFunc = ExecutePass;

        static void ExecutePass(PassData data, RasterGraphContext ctx)
        {
            var sink = new PrismRasterSink { cmd = ctx.cmd };
            PrismHighlightFeature.DrawAll(ref sink, data.camera);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

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
            var sink = new PrismCmdSink { cmd = cmd };
            PrismHighlightFeature.DrawAll(ref sink, renderingData.cameraData.camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
#endif
    }
}
