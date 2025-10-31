using System;
namespace TGD.Data
{
    /// <summary>
    /// ���ڱ�� EffectDefinition ���ֶ�����Щ EffectType ����ʾ
    /// ���û�б�ע����Ĭ�϶����� EffectType ��ʾ
    /// ����б�ע������ڱ�ע�� EffectType ����ʾ
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class EffectFieldAttribute : Attribute
    {
        public EffectType[] EffectTypes { get; private set; }

        public EffectFieldAttribute(params EffectType[] effectTypes)
        {
            EffectTypes = effectTypes;
        }
    }
}

