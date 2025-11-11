using System;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Marks a string field that stores a skill identifier and should surface editor helpers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SkillIdReferenceAttribute : PropertyAttribute
    {
        public bool allowEmpty { get; }

        public SkillIdReferenceAttribute(bool allowEmpty = true)
        {
            this.allowEmpty = allowEmpty;
        }
    }
}
