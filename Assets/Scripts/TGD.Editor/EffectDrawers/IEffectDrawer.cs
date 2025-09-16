using UnityEditor;

namespace TGD.Editor
{
    public interface IEffectDrawer
    {
        /// <summary>����һ�� EffectDefinition Ԫ��</summary>
        void Draw(SerializedProperty effectElement);
    }
}
