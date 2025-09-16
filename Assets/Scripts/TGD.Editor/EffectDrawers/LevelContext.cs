using UnityEngine;
using UnityEditor;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>��ȡ��ǰ Skill �ĵȼ���1~4����</summary>
    public static class LevelContext
    {
        public static int GetSkillLevel(SerializedObject so)
        {
            var sd = so?.targetObject as SkillDefinition;
            if (sd == null) return 1;
            return Mathf.Clamp(sd.skillLevel, 1, 4);
        }
    }
}
