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
                EditorUtility.DisplayDialog("Create Linked State", $"�ļ��Ѵ��ڣ�\n{assetPath}", "OK");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SkillDefinition>(assetPath);
                return;
            }

            // ����״̬�ʲ�
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

            // Ĭ�����ձ��ػ� key�������Լ���
            state.namekey = "";
            state.descriptionKey = "";

            state.effects = new System.Collections.Generic.List<EffectDefinition>(); // �գ���������

            AssetDatabase.CreateAsset(state, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(state);
            Selection.activeObject = state;

            EditorUtility.DisplayDialog("Create Linked State",
                $"�Ѵ���״̬�ʲ���\n{assetPath}\n\n����������������(SK003)���һ�� Apply Status��ָ�� {newId}�����Ѹ�״̬���Ч�� Duration ������Ϊ -1��",
                "OK");
        }
    }
}
