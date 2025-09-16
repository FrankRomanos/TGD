using UnityEditor;

namespace TGD.Editor
{
    public interface IEffectDrawer
    {
        /// <summary>绘制一个 EffectDefinition 元素</summary>
        void Draw(SerializedProperty effectElement);
    }
}
