using System;
namespace TGD.Data
{
    /// <summary>
    /// 用于标记 EffectDefinition 的字段在哪些 EffectType 下显示
    /// 如果没有标注，则默认对所有 EffectType 显示
    /// 如果有标注，则仅在标注的 EffectType 下显示
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

