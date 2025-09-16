using System.IO;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public static class CreateLinkedStateUtility
    {
        [MenuItem("Assets/TGD/Create Linked State (_STANCE)...", true)]
        private static bool ValidateCreateState() => Selection.activeObject is SkillDefinition;

        [MenuItem("Assets/TGD/Create Linked State (_STANCE)...")]
        private static void CreateLinkedState()
        {
            var src = Selection.activeObject as SkillDefinition;
            if (src == null) return;

            string srcPath = AssetDatabase.GetAssetPath(src);
            string dir = Path.GetDirectoryName(srcPath);
            string id = string.IsNullOrEmpty(src.skillID) ? src.name : src.skillID;
            string newId = id.EndsWith("_STANCE") ? id : id + "_STANCE";
            string assetPath = Path.Combine(dir!, $"Skill_{newId}.asset").Replace("\\", "/");

            if (File.Exists(assetPath))
            {
                EditorUtility.DisplayDialog("Create Linked State", $"文件已存在：\n{assetPath}", "OK");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SkillDefinition>(assetPath);
                return;
            }

            // 创建状态资产
            var state = ScriptableObject.CreateInstance<SkillDefinition>();
            state.skillID = newId;
            state.skillName = $"{src.skillName} (State)";
            state.classID = src.classID;
            state.moduleID = src.moduleID;
            state.variantKey = src.variantKey;
            state.chainNextID = string.Empty;
            state.resetOnTurnEnd = false;

            state.skillType = SkillType.State;
            state.actionType = ActionType.None;
            state.targetType = SkillTargetType.Self;
            state.skillColor = SkillColor.None;

            state.costs.Clear();
            state.timeCostSeconds = 0;
            state.cooldownSeconds = 0;
            state.cooldownRounds = 0;
            state.range = 0;
            state.threat = 0;
            state.shredMultiplier = 0;

            // 默认留空本地化 key，按需自己填
            state.namekey = "";
            state.descriptionKey = "";

            state.effects = new System.Collections.Generic.List<EffectDefinition>(); // 空，等你手填

            AssetDatabase.CreateAsset(state, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(state);
            Selection.activeObject = state;

            EditorUtility.DisplayDialog("Create Linked State",
                $"已创建状态资产：\n{assetPath}\n\n接下来：在主技能(SK003)里加一条 Apply Status，指向 {newId}，并把该状态里的效果 Duration 都设置为 -1。",
                "OK");
        }
    }
}
