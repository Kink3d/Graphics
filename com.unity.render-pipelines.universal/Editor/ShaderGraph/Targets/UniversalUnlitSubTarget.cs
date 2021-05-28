using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing;
using UnityEngine.Rendering;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditor.ShaderGraph.Legacy;
using BlendOp = UnityEditor.ShaderGraph.BlendOp;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    sealed class UniversalUnlitSubTarget : SubTarget<UniversalTarget>, ILegacyTarget
    {
        const int kOverrideCount = 8;

        [Serializable]
        class PassOverride
        {
            [SerializeField] public bool foldout;
            [SerializeField] public int index;
            [SerializeField] public bool[] overrides = new bool[kOverrideCount];

            [SerializeField] public Cull cullValue; // 0
            [SerializeField] public Blend blendSrc; // 1
            [SerializeField] public Blend blendDst; // 1
            [SerializeField] public BlendOp blendOp; // 2
            [SerializeField] public BlendOp blendOpAlpha; // 2
            [SerializeField] public ZTest zTest; // 3
            [SerializeField] public ZWrite zWrite; // 4
            [SerializeField] public bool zClip; // 5
            [SerializeField] public string colorMask; // 6
            [SerializeField] public bool alphaToMask; // 7
        }

        static readonly GUID kSourceCodeGuid = new GUID("97c3f7dcb477ec842aa878573640313a"); // UniversalUnlitSubTarget.cs

        [SerializeField]
        List<PassOverride> m_PassOverrides;

        public UniversalUnlitSubTarget()
        {
            displayName = "Unlit";

            // Initialize the pass list
            if(m_PassOverrides == null)
            {
                m_PassOverrides = new List<PassOverride>();
                var supportedPasses = SubShaders.Unlit.supportedPasses.ToList();
                for(int i = 0; i < supportedPasses.Count(); i++)
                {
                    var pass = supportedPasses[i];
                    var descriptor = pass.descriptor;
                    if(SubShaders.Unlit.defaultPasses.Any(s => s.descriptor.Equals(descriptor)))
                    {
                        var passOverride = new PassOverride() { index = i };
                        m_PassOverrides.Add(passOverride);
                    }  
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

            var supportedPasses = SubShaders.Unlit.supportedPasses.ToList();
            var passList = new ReorderableListView<PassOverride>(
                m_PassOverrides,
                "Active Passes",
                true,
                passOverride => supportedPasses[passOverride.index].descriptor.displayName);

            void PassOverrideElementGUI(Rect rect, int index, GUIContent style, PassOverride passOverride, Action<Rect> drawAction)
            {
                var indent = 15;
                var toggleWidth = 16;
                var labelWidth = 0.7f;
                var elementRect = new Rect(rect.x, rect.y + (EditorGUIUtility.singleLineHeight * (index + 1)), rect.width, EditorGUIUtility.singleLineHeight);
                var toggleRect = new Rect(elementRect.x + indent, elementRect.y, toggleWidth, elementRect.height);
                var labelRect = new Rect(elementRect.x + indent + toggleWidth, elementRect.y, (EditorGUIUtility.labelWidth * labelWidth) - indent, elementRect.height);
                var fieldRect = new Rect(elementRect.x + indent + toggleWidth + labelRect.width, elementRect.y, elementRect.width - indent - toggleWidth - labelRect.width, elementRect.height);

                var overrideValue = EditorGUI.Toggle(toggleRect, passOverride.overrides[index]);
                if(overrideValue != passOverride.overrides[index])
                {
                    registerUndo("Change override");
                    passOverride.overrides[index] = overrideValue;
                    onChange();
                }

                using (var disabledScope = new EditorGUI.DisabledGroupScope(!passOverride.overrides[index]))
                {
                    EditorGUI.LabelField(labelRect, style);
                    drawAction(fieldRect);
                }
            }

            passList.OnDrawElementCallback +=
                (rect, index, isActive, isFocused) =>
                {
                    var passOverride = m_PassOverrides[index];
                    var foldoutRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
                    passOverride.foldout = EditorGUI.Foldout(foldoutRect, passOverride.foldout, supportedPasses[passOverride.index].descriptor.displayName);
                    if (passOverride.foldout)
                    {
                        PassOverrideElementGUI(rect, 0, new GUIContent("Cull"), passOverride, (r) =>
                        {
                            var cullValue = (Cull)EditorGUI.EnumPopup(r, passOverride.cullValue);
                            if(cullValue != passOverride.cullValue)
                            {
                                registerUndo("Change Cull");
                                passOverride.cullValue = cullValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 1, new GUIContent("Blend"), passOverride, (r) =>
                        {
                            var blendSrcValue = (Blend)EditorGUI.EnumPopup(new Rect(r.x, r.y, r.width / 2, r.height), passOverride.blendSrc);
                            if(blendSrcValue != passOverride.blendSrc)
                            {
                                registerUndo("Change Blend");
                                passOverride.blendSrc = blendSrcValue;
                                onChange();
                            }
                            var blendDstValue = (Blend)EditorGUI.EnumPopup(new Rect(r.x + r.width / 2, r.y, r.width / 2, r.height), passOverride.blendDst);
                            if(blendDstValue != passOverride.blendDst)
                            {
                                registerUndo("Change Blend");
                                passOverride.blendDst = blendDstValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 2, new GUIContent("BlendOp"), passOverride, (r) =>
                        {
                            var blendOpValue = (BlendOp)EditorGUI.EnumPopup(new Rect(r.x, r.y, r.width / 2, r.height), passOverride.blendOp);
                            if(blendOpValue != passOverride.blendOp)
                            {
                                registerUndo("Change BlendOp");
                                passOverride.blendOp = blendOpValue;
                                onChange();
                            }
                            var blendOpAlphaValue = (BlendOp)EditorGUI.EnumPopup(new Rect(r.x + r.width / 2, r.y, r.width / 2, r.height), passOverride.blendOpAlpha);
                            if(blendOpAlphaValue != passOverride.blendOpAlpha)
                            {
                                registerUndo("Change BlendOp");
                                passOverride.blendOpAlpha = blendOpAlphaValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 3, new GUIContent("ZTest"), passOverride, (r) =>
                        {
                            var zTestValue = (ZTest)EditorGUI.EnumPopup(r, passOverride.zTest);
                            if(zTestValue != passOverride.zTest)
                            {
                                registerUndo("Change ZTest");
                                passOverride.zTest = zTestValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 4, new GUIContent("ZWrite"), passOverride, (r) =>
                        {
                            var zWriteValue = (ZWrite)EditorGUI.EnumPopup(r, passOverride.zWrite);
                            if(zWriteValue != passOverride.zWrite)
                            {
                                registerUndo("Change ZWrite");
                                passOverride.zWrite = zWriteValue;
                                onChange();
                            }
                        });

                        PassOverrideElementGUI(rect, 5, new GUIContent("ZClip"), passOverride, (r) =>
                        {
                            var zClipValue = EditorGUI.Toggle(r, passOverride.zClip);
                            if(zClipValue != passOverride.zClip)
                            {
                                registerUndo("Change ZClip");
                                passOverride.zClip = zClipValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 6, new GUIContent("ColorMask"), passOverride, (r) =>
                        {
                            var colorMaskValue = EditorGUI.DelayedTextField(r, passOverride.colorMask);
                            if(colorMaskValue != passOverride.colorMask)
                            {
                                registerUndo("Change ColorMask");
                                passOverride.colorMask = colorMaskValue;
                                onChange();
                            }
                        });
                        PassOverrideElementGUI(rect, 7, new GUIContent("AlphaToMask"), passOverride, (r) =>
                        {
                            var alphaToMaskValue = EditorGUI.Toggle(r, passOverride.alphaToMask);
                            if(alphaToMaskValue != passOverride.alphaToMask)
                            {
                                registerUndo("Change AlphaToMask");
                                passOverride.alphaToMask = alphaToMaskValue;
                                onChange();
                            }
                        });
                    }
                };

            passList.OnElementHeightCallback += (index) =>
            {
                var foldout = m_PassOverrides[index].foldout;
                if(foldout)
                    return (1 + kOverrideCount) * EditorGUIUtility.singleLineHeight;
                else
                    return 1 * EditorGUIUtility.singleLineHeight;
            };

            passList.GetAddMenuOptions = () =>
            {
                return supportedPasses.Select(s => s.descriptor.displayName).ToList();
            };

            passList.OnAddMenuItemCallback +=
                (list, addMenuOptionIndex, addMenuOption) =>
                {
                    registerUndo("Add pass"); 
                    var passOverride = new PassOverride() { index = addMenuOptionIndex };
                    m_PassOverrides.Add(passOverride);
                    onChange();
                };

            passList.RemoveItemCallback +=
                (list, itemIndex) =>
                {
                    registerUndo("Remove pass");
                    m_PassOverrides.Remove(list[itemIndex]);
                    onChange();
                };

            passList.OnListReorderedCallback +=
                (list) =>
                {
                    onChange();
                };
            
            context.AddGUIElement(passList);
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
            var supportedPasses = input.supportedPasses.ToList();
            foreach(var passOverride in m_PassOverrides)
            {
                var pass = supportedPasses[passOverride.index];
                var descriptor = pass.descriptor;
                var previousRenderStates = descriptor.renderStates.ToList();
                var renderStates = new RenderStateCollection();

                // Cull
                if(passOverride.overrides[0])
                {
                    renderStates.Add(RenderState.Cull(passOverride.cullValue));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.Cull);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // Blend
                if(passOverride.overrides[1])
                {
                    renderStates.Add(RenderState.Blend(passOverride.blendSrc, passOverride.blendDst));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.Blend);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // BlendOp
                if(passOverride.overrides[2])
                {
                    renderStates.Add(RenderState.BlendOp(passOverride.blendOp, passOverride.blendOpAlpha));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.BlendOp);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // ZTest
                if(passOverride.overrides[3])
                {
                    renderStates.Add(RenderState.ZTest(passOverride.zTest));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.ZTest);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // ZWrite
                if(passOverride.overrides[4])
                {
                    renderStates.Add(RenderState.ZWrite(passOverride.zWrite));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.ZWrite);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // ZClip
                if(passOverride.overrides[5])
                {
                    renderStates.Add(RenderState.ZClip(passOverride.zClip ? "True" : "False"));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.ZClip);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // ColorMask
                if(passOverride.overrides[6])
                {
                    renderStates.Add(RenderState.ColorMask(passOverride.colorMask));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.ColorMask);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                // AlphaToMask
                if(passOverride.overrides[7])
                {
                    renderStates.Add(RenderState.AlphaToMask(passOverride.alphaToMask ? "True" : "False"));
                }
                else
                {
                    var item = previousRenderStates.FirstOrDefault(s => s.descriptor.type == RenderStateType.AlphaToMask);
                    if(item != null) renderStates.Add(item.descriptor);
                }

                descriptor.renderStates = renderStates;
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
