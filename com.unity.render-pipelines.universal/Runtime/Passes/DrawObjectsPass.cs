using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Profiling;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Draw  objects into the given color and depth target
    ///
    /// You can use this pass to render objects that have a material and/or shader
    /// with the pass names UniversalForward or SRPDefaultUnlit.
    /// </summary>
    public class DrawObjectsPass : ScriptableRenderPass
    {
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;
        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        string m_ProfilerTag;
        ProfilingSampler m_ProfilingSampler;
        bool m_IsOpaque;

        static readonly int s_DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");

        public DrawObjectsPass(string profilerTag, ShaderTagId[] shaderTagIds, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DrawObjectsPass));

            m_ProfilerTag = profilerTag;
            m_ProfilingSampler = new ProfilingSampler(profilerTag);
            foreach (ShaderTagId sid in shaderTagIds)
                m_ShaderTagIdList.Add(sid);
            renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_IsOpaque = opaque;

            if (stencilState.enabled)
            {
                m_RenderStateBlock.stencilReference = stencilReference;
                m_RenderStateBlock.mask = RenderStateMask.Stencil;
                m_RenderStateBlock.stencilState = stencilState;
            }
        }

        public DrawObjectsPass(string profilerTag, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(profilerTag,
                new ShaderTagId[] { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("LightweightForward")},
                opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference)
        {}

        internal DrawObjectsPass(URPProfileId profileId, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
            : this(profileId.GetType().Name, opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference)
        {
            m_ProfilingSampler = ProfilingSampler.Get(profileId);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // NOTE: Do NOT mix ProfilingScope with named CommandBuffers i.e. CommandBufferPool.Get("name").
            // Currently there's an issue which results in mismatched markers.

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var colorTargetDescriptor = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
                var depthTargetDescriptor = new AttachmentDescriptor(RenderTextureFormat.Depth);

                if (renderingData.cameraData.xr.enabled)
                {
                    colorTargetDescriptor.ConfigureTarget(renderingData.cameraData.xr.renderTarget, false, true);
                }
                else
                {
                    colorTargetDescriptor.ConfigureTarget(renderingData.cameraData.targetTexture, false, true);
                }

                depthTargetDescriptor.ConfigureClear(default);

                const int colorIndex = 0, depthIndex = 1;
                var attachments = new NativeArray<AttachmentDescriptor>(2, Allocator.Temp);
                attachments[colorIndex] = colorTargetDescriptor;
                attachments[depthIndex] = depthTargetDescriptor;

                context.BeginRenderPass(renderingData.cameraData.pixelWidth, renderingData.cameraData.pixelHeight, 1, attachments, depthIndex);


                NativeArray<int> targetAttachments = new NativeArray<int>(1,Allocator.Temp);
                targetAttachments[0] = colorIndex;
              //  targetAttachments[1] = depthIndex;

                context.BeginSubPass(targetAttachments);

                // Global render pass data containing various settings.
                // x,y,z are currently unused
                // w is used for knowing whether the object is opaque(1) or alpha blended(0)
                Vector4 drawObjectPassData = new Vector4(0.0f, 0.0f, 0.0f, (m_IsOpaque) ? 1.0f : 0.0f);
                cmd.SetGlobalVector(s_DrawObjectPassDataPropID, drawObjectPassData);

                // scaleBias.x = flipSign
                // scaleBias.y = scale
                // scaleBias.z = bias
                // scaleBias.w = unused
                float flipSign = (renderingData.cameraData.IsCameraProjectionMatrixFlipped()) ? -1.0f : 1.0f;
                Vector4 scaleBias = (flipSign < 0.0f)
                    ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                    : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
                cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                Camera camera = renderingData.cameraData.camera;
                var sortFlags = (m_IsOpaque) ? renderingData.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent;
                var drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortFlags);
                var filterSettings = m_FilteringSettings;

                #if UNITY_EDITOR
                // When rendering the preview camera, we want the layer mask to be forced to Everything
                if (renderingData.cameraData.isPreviewCamera)
                {
                    filterSettings.layerMask = -1;
                }
                #endif

                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings, ref m_RenderStateBlock);

                // Render objects that did not match any shader pass with error shader
                RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortingCriteria.None);

                context.EndSubPass();

                var transparentPassInputAttachments = new NativeArray<int>(1, Allocator.Temp);
                transparentPassInputAttachments[0] = depthIndex;

                var noDepthBufferTargetAttachments = new NativeArray<int>(1, Allocator.Temp);
                noDepthBufferTargetAttachments[0] = colorIndex;
                context.BeginSubPass(noDepthBufferTargetAttachments, transparentPassInputAttachments, true, true);

                FilteringSettings transparentFilterSettings = filterSettings;
                transparentFilterSettings.renderQueueRange=RenderQueueRange.transparent;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref transparentFilterSettings, ref m_RenderStateBlock);

                context.EndSubPass();

                context.EndRenderPass();

                attachments.Dispose();
                targetAttachments.Dispose();
                transparentPassInputAttachments.Dispose();
                noDepthBufferTargetAttachments.Dispose();
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
