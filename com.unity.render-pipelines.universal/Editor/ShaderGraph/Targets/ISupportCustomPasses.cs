using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing;
using BlendOp = UnityEditor.ShaderGraph.BlendOp;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    internal interface ISupportCustomPasses
    {
        List<PassOverride> passOverrides { get; }
        PassCollection supportedPasses { get; }
        PassCollection defaultPasses { get; }
    }

    [Serializable]
    class PassOverride
    {
        [SerializeField] public bool foldout;
        [SerializeField] public int index;
        [SerializeField] public bool[] overrides = new bool[CustomPassExtensions.kOverrideCount];

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

    internal struct PassOverrideSubShaderDescriptor
    {
        public string pipelineTag;
        public string customTags;
        public string renderType;
        public string renderQueue;
        public bool generatesPreview;
        public PassCollection supportedPasses;
        public PassCollection defaultPasses;
    }

    internal static class CustomPassExtensions
    {
        public static readonly int kOverrideCount = 8;

        internal static void InitPassOverrides(this ISupportCustomPasses target)
        {
            target.passOverrides.Clear();
            var supportedPasses = target.supportedPasses.ToList();
            for(int i = 0; i < supportedPasses.Count(); i++)
            {
                var pass = supportedPasses[i];
                var descriptor = pass.descriptor;
                if(target.defaultPasses.Any(s => s.descriptor.Equals(descriptor)))
                {
                    var passOverride = new PassOverride() { index = i };
                    target.passOverrides.Add(passOverride);
                }  
            }
        }

        internal static void GetPassListGUI(this ISupportCustomPasses target, ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            var supportedPasses = target.supportedPasses.ToList();
            var passList = new ReorderableListView<PassOverride>(
                target.passOverrides,
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
                    var passOverride = target.passOverrides[index];
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
                var foldout = target.passOverrides[index].foldout;
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
                    target.passOverrides.Add(passOverride);
                    onChange();
                };

            passList.RemoveItemCallback +=
                (list, itemIndex) =>
                {
                    registerUndo("Remove pass");
                    target.passOverrides.Remove(list[itemIndex]);
                    onChange();
                };

            passList.OnListReorderedCallback +=
                (list) =>
                {
                    onChange();
                };
            
            context.AddGUIElement(passList);
        }

        internal static SubShaderDescriptor ConvertSubShaderForPassOverrides(this ISupportCustomPasses target, PassOverrideSubShaderDescriptor input)
        {
            var passes = new PassCollection();
            var supportedPasses = target.supportedPasses.ToList();
            foreach(var passOverride in target.passOverrides)
            {
                var pass = supportedPasses[passOverride.index];
                if(!input.supportedPasses.ToList().Any(s => s.descriptor.referenceName == pass.descriptor.referenceName))
                    continue;

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
                    renderStates.Add(RenderState.ColorMask($"ColorMask {passOverride.colorMask}"));
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
    }
}
