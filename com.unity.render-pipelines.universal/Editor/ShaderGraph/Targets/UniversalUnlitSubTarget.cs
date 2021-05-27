using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.ShaderGraph;
using UnityEngine.Rendering;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor.ShaderGraph.Legacy;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    sealed class UniversalUnlitSubTarget : SubTarget<UniversalTarget>, ILegacyTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("97c3f7dcb477ec842aa878573640313a"); // UniversalUnlitSubTarget.cs

        [SerializeField]
        List<PassOverride> m_PassOverrides;

        public UniversalUnlitSubTarget()
        {
            displayName = "Unlit";

            // Initialize the override map
            if(m_PassOverrides == null)
            {
                m_PassOverrides = new List<PassOverride>();
                foreach(var pass in SubShaders.Unlit.supportedPasses)
                {
                    var descriptor = pass.descriptor;
                    var passOverride = new PassOverride(descriptor.referenceName, SubShaders.Unlit.defaultPasses.Any(s => s.descriptor.Equals(descriptor)));
                    m_PassOverrides.Add(passOverride);
                }
            }
        }

        public override bool IsActive() => true;

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);

            // Added from LitSubTarget to support GI
            // Just adds to the globalIlluminationFlags of the material
            context.SetDefaultShaderGUI("ShaderGraph.PBRMasterGUI"); // TODO: This should be owned by URP

            // Process SubShaders
            UniversalSubShaderDescriptor[] subShaders = { SubShaders.ProcessDotsSubShader(SubShaders.Unlit), SubShaders.Unlit };
            for(int i = 0; i < subShaders.Length; i++)
            {
                // Apply Pass overrides
                var subShader = ConvertSubShaderForPassOverrides(subShaders[i]);

                // Update Render State
                subShader.renderType = target.renderType;
                subShader.renderQueue = target.renderQueue;

                // Add
                context.AddSubShader(subShader);
            }
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            // Surface Type & Blend Mode
            // These must be set per SubTarget as Sprite SubTargets override them
            context.AddField(UniversalFields.SurfaceOpaque,       target.surfaceType == SurfaceType.Opaque);
            context.AddField(UniversalFields.SurfaceTransparent,  target.surfaceType != SurfaceType.Opaque);
            context.AddField(UniversalFields.BlendAdd,            target.surfaceType != SurfaceType.Opaque && target.alphaMode == AlphaMode.Additive);
            context.AddField(Fields.BlendAlpha,                   target.surfaceType != SurfaceType.Opaque && target.alphaMode == AlphaMode.Alpha);
            context.AddField(UniversalFields.BlendMultiply,       target.surfaceType != SurfaceType.Opaque && target.alphaMode == AlphaMode.Multiply);
            context.AddField(UniversalFields.BlendPremultiply,    target.surfaceType != SurfaceType.Opaque && target.alphaMode == AlphaMode.Premultiply);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(BlockFields.SurfaceDescription.Alpha,              target.surfaceType == SurfaceType.Transparent || target.alphaClip);
            context.AddBlock(BlockFields.SurfaceDescription.AlphaClipThreshold, target.alphaClip);
            context.AddBlock(BlockFields.SurfaceDescription.Emission);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            context.AddProperty("Surface", new EnumField(SurfaceType.Opaque) { value = target.surfaceType }, (evt) =>
            {
                if (Equals(target.surfaceType, evt.newValue))
                    return;

                registerUndo("Change Surface");
                target.surfaceType = (SurfaceType)evt.newValue;
                onChange();
            });

            context.AddProperty("Blend", new EnumField(AlphaMode.Alpha) { value = target.alphaMode }, target.surfaceType == SurfaceType.Transparent, (evt) =>
            {
                if (Equals(target.alphaMode, evt.newValue))
                    return;

                registerUndo("Change Blend");
                target.alphaMode = (AlphaMode)evt.newValue;
                onChange();
            });

            context.AddProperty("Alpha Clip", new Toggle() { value = target.alphaClip }, (evt) =>
            {
                if (Equals(target.alphaClip, evt.newValue))
                    return;

                registerUndo("Change Alpha Clip");
                target.alphaClip = evt.newValue;
                onChange();
            });

            context.AddProperty("Two Sided", new Toggle() { value = target.twoSided }, (evt) =>
            {
                if (Equals(target.twoSided, evt.newValue))
                    return;

                registerUndo("Change Two Sided");
                target.twoSided = evt.newValue;
                onChange();
            });

            foreach(var pass in SubShaders.Unlit.supportedPasses)
            {
                var descriptor = pass.descriptor;
                var index = 0;
                for(int i = 0; i < m_PassOverrides.Count; i++)
                {
                    if(m_PassOverrides[i].referenceName == descriptor.referenceName)
                        index = i;
                }

                var passOverride = m_PassOverrides[index];
                context.AddProperty(descriptor.displayName, new Toggle() { value = passOverride.value }, (evt) =>
                {
                    if (Equals(passOverride.value, evt.newValue))
                        return;

                    registerUndo("Change Pass Override");
                    passOverride.value = evt.newValue;
                    m_PassOverrides[index] = passOverride;
                    onChange();
                });
            }
        }

        public bool TryUpgradeFromMasterNode(IMasterNode1 masterNode, out Dictionary<BlockFieldDescriptor, int> blockMap)
        {
            blockMap = null;
            if(!(masterNode is UnlitMasterNode1 unlitMasterNode))
                return false;

            // Set blockmap
            blockMap = new Dictionary<BlockFieldDescriptor, int>()
            {
                { BlockFields.VertexDescription.Position, 9 },
                { BlockFields.VertexDescription.Normal, 10 },
                { BlockFields.VertexDescription.Tangent, 11 },
                { BlockFields.SurfaceDescription.BaseColor, 0 },
                { BlockFields.SurfaceDescription.Alpha, 7 },
                { BlockFields.SurfaceDescription.AlphaClipThreshold, 8 },
            };

            return true;
        }

        [Serializable]
        struct PassOverride
        {
            [SerializeField] public string referenceName;
            [SerializeField] public bool value;

            public PassOverride(string referenceName, bool value)
            {
                this.referenceName = referenceName;
                this.value = value;
            }
        }

        internal struct UniversalSubShaderDescriptor
        {
            public string pipelineTag;
            public string customTags;
            public string renderType;
            public string renderQueue;
            public bool generatesPreview;
            public PassCollection supportedPasses;
            public PassCollection defaultPasses;
        }

        public SubShaderDescriptor ConvertSubShaderForPassOverrides(UniversalSubShaderDescriptor input)
        {
            var passes = new PassCollection();
            foreach(var pass in input.supportedPasses)
            {
                var descriptor = pass.descriptor;
                var value = false;
                foreach(var passOverride in m_PassOverrides)
                {
                    if(passOverride.referenceName == descriptor.referenceName)
                    {
                        value = passOverride.value;
                        break;
                    }
                }

                if(value == false)
                    continue;
                
                passes.Add(descriptor, pass.fieldConditions);
            }

            return new SubShaderDescriptor()
            {
                pipelineTag = input.pipelineTag,
                customTags = input.customTags,
                renderType = input.renderType,
                renderQueue = input.renderQueue,
                generatesPreview = input.generatesPreview,
                passes = passes,
            };
        }

#region SubShader
        static class SubShaders
        {
            public static UniversalSubShaderDescriptor Unlit = new UniversalSubShaderDescriptor()
            {
                pipelineTag = UniversalTarget.kPipelineTag,
                customTags = UniversalTarget.kUnlitMaterialTypeTag,
                generatesPreview = true,
                supportedPasses = new PassCollection
                {
                    { UnlitPasses.Unlit },
                    { CorePasses.ShadowCaster },
                    { CorePasses.DepthOnly },
                    { UniversalLitSubTarget.LitPasses.DepthNormalOnly },
                    { UniversalLitSubTarget.LitPasses.Meta },
                },
                defaultPasses = new PassCollection
                {
                    { UnlitPasses.Unlit },
                    { CorePasses.ShadowCaster },
                    { CorePasses.DepthOnly },
                },
            };

            public static UniversalSubShaderDescriptor ProcessDotsSubShader(UniversalSubShaderDescriptor input)
            {
                var modifiedPasses = new PassCollection();
                foreach(var pass in input.supportedPasses)
                {
                    var descriptor = pass.descriptor;

                    if(descriptor.Equals(UnlitPasses.Unlit))
                        descriptor.pragmas = CorePragmas.DOTSForward;
                    else if(descriptor.Equals(CorePasses.ShadowCaster))
                        descriptor.pragmas = CorePragmas.DOTSInstanced;
                    else if(descriptor.Equals(CorePasses.DepthOnly))
                        descriptor.pragmas = CorePragmas.DOTSInstanced;
                    else if(descriptor.Equals(UniversalLitSubTarget.LitPasses.DepthNormalOnly))
                        descriptor.pragmas = CorePragmas.DOTSInstanced;
                    else if(descriptor.Equals(UniversalLitSubTarget.LitPasses.Meta))
                        descriptor.pragmas = CorePragmas.Default;

                    modifiedPasses.Add(descriptor, pass.fieldConditions);
                }

                input.supportedPasses = modifiedPasses;
                return input;
            }
        }
#endregion

#region Pass
        static class UnlitPasses
        {
            public static PassDescriptor Unlit = new PassDescriptor
            {
                // Definition
                displayName = "Unlit",
                referenceName = "SHADERPASS_UNLIT",
                useInPreview = true,

                // Template
                passTemplatePath = GenerationUtils.GetDefaultTemplatePath("PassMesh.template"),
                sharedTemplateDirectories = GenerationUtils.GetDefaultSharedTemplateDirectories(),

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.FragmentColorEmissionAlpha,

                // Fields
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.Default,
                pragmas = CorePragmas.Forward,
                keywords = UnlitKeywords.Unlit,
                includes = UnlitIncludes.Unlit,
            };
        }
#endregion

#region Keywords
        static class UnlitKeywords
        {
            public static KeywordCollection Unlit = new KeywordCollection
            {
                { CoreKeywordDescriptors.Lightmap },
                { CoreKeywordDescriptors.DirectionalLightmapCombined },
                { CoreKeywordDescriptors.SampleGI },
            };
        }
#endregion

#region Includes
        static class UnlitIncludes
        {
            const string kUnlitPass = "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/UnlitPass.hlsl";

            public static IncludeCollection Unlit = new IncludeCollection
            {
                // Pre-graph
                { CoreIncludes.CorePregraph },
                { CoreIncludes.ShaderGraphPregraph },

                // Post-graph
                { CoreIncludes.CorePostgraph },
                { kUnlitPass, IncludeLocation.Postgraph },
            };
        }
#endregion
    }
}
