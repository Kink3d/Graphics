using UnityEditor.Graphing;

namespace UnityEditor.ShaderGraph
{
    interface IMayRequireColorPipelineIndex
    {
        bool RequiresColorPipelineIndex();
    }

    static class MayRequireColorPipelineIndexExtensions
    {
        public static bool RequiresColorPipelineIndex(this AbstractMaterialNode node)
        {
            return node is IMayRequireColorPipelineIndex mayRequireTime && mayRequireTime.RequiresColorPipelineIndex();
        }
    }
}
