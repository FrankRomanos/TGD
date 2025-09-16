using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    [CustomEditor(typeof(SkillDefinition))]
    public class SkillDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty effectsProp;
        private SerializedProperty costsProp;

        private SerializedProperty skillColorProp;
        private SerializedProperty skillLevelProp;
        private SerializedProperty skillDurationProp;


        private static readonly SkillColor[] kLeveledColors = new[]
        {
            SkillColor.DeepBlue,
            SkillColor.DarkYellow,
            SkillColor.Green,
            SkillColor.Purple,
            SkillColor.LightBlue
        };

        private void OnEnable()
        {
            effectsProp = serializedObject.FindProperty("effects");
            costsProp = serializedObject.FindProperty("costs");
            skillColorProp = serializedObject.FindProperty("skillColor");
            skillLevelProp = serializedObject.FindProperty("skillLevel");
            skillDurationProp = serializedObject.FindProperty("skillDuration");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 基础信息
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("classID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("moduleID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("variantKey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("chainNextID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resetOnTurnEnd"));

            // 颜色 + 等级
            EditorGUILayout.PropertyField(skillColorProp, new GUIContent("Skill Color"));

            bool leveled = IsLeveledColor((SkillColor)skillColorProp.enumValueIndex);
            if (leveled)
            {
                EditorGUILayout.IntSlider(skillLevelProp, 1, 4, new GUIContent("Skill Level"));
            }
            else
            {
                skillLevelProp.intValue = 1; // 非五色不分级
                EditorGUILayout.HelpBox("此技能颜色不参与 1~4 等级系统。每级数值请直接在 Effects 里按需配置（或不配置）。", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skill Duration", EditorStyles.boldLabel);

            if (skillDurationProp != null)
            {
                var durationValueProp = skillDurationProp.FindPropertyRelative("duration");
                var levelsProp = skillDurationProp.FindPropertyRelative("durationLevels");

                if (durationValueProp == null)
                {
                    EditorGUILayout.HelpBox("'duration' property not found on skillDuration.", MessageType.Error);
                }
                else
                {
                    bool collapsed;
                    if (PerLevelUI.BeginPerLevelBlock(skillDurationProp, out collapsed, "Use Per-Level Duration"))
                    {
                        if (!collapsed)
                        {
                            if (levelsProp != null)
                            {
                                PerLevelUI.DrawIntLevels(levelsProp, "Duration by Level (turns)");
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("'durationLevels' property not found on skillDuration.", MessageType.Error);
                            }
                        }

                        if (levelsProp != null)
                        {
                            PerLevelUI.EnsureSize(levelsProp, 4);
                            int currentLevel = LevelContext.GetSkillLevel(serializedObject);
                            int idx = Mathf.Clamp(currentLevel - 1, 0, 3);
                            int value = levelsProp.GetArrayElementAtIndex(idx).intValue;
                            if (value == 0)
                                value = durationValueProp.intValue;
                            EditorGUILayout.HelpBox($"Duration @L{currentLevel}: {value} turn(s)", MessageType.Info);
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(durationValueProp, new GUIContent("Duration (turns)"));
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("'skillDuration' property not found on SkillDefinition.", MessageType.Error);
            }

            // 类型与通用数值（注意 Mastery 的 N/A）
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillType"), new GUIContent("Skill Type"));
            var skillTypeProp = serializedObject.FindProperty("skillType");

            EditorGUILayout.PropertyField(serializedObject.FindProperty("actionType"), new GUIContent("Action Type"));
            SkillType skillType = (SkillType)skillTypeProp.enumValueIndex;

            if (skillType == SkillType.Mastery)
            {
                serializedObject.FindProperty("targetType").enumValueIndex = (int)SkillTargetType.None;
                serializedObject.FindProperty("timeCostSeconds").intValue = 0;
                serializedObject.FindProperty("cooldownSeconds").intValue = 0;
                serializedObject.FindProperty("cooldownRounds").intValue = 0;
                serializedObject.FindProperty("threat").floatValue = 0f;
                serializedObject.FindProperty("shredMultiplier").floatValue = 0f;

                EditorGUILayout.HelpBox("Mastery/Passive 不需要 Target/TimeCost/Cooldown/Threat/Shred，已自动设为 N/A。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetType"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("timeCostSeconds"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cooldownSeconds"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cooldownRounds"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("range"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("threat"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shredMultiplier"));
            }
            var actionTypeProp = serializedObject.FindProperty("actionType");
            var actionType = (ActionType)actionTypeProp.enumValueIndex;

            if (actionType == ActionType.FullRound)
            {
                // 强制写哨兵值 -1，并隐藏 TimeCost 编辑
                var timeProp = serializedObject.FindProperty("timeCostSeconds");
                timeProp.intValue = -1;

                EditorGUILayout.HelpBox(
                    "Full Round: Consumes all remaining time this turn and ends the turn immediately. (Time Cost set to -1)",
                    MessageType.Info);

                // 其他依然可见：冷却、替换、范围等
            }
            else
            {
                // 非 FullRound 照常显示 TimeCost
                EditorGUILayout.PropertyField(serializedObject.FindProperty("timeCostSeconds"));
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("namekey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("descriptionKey"));

            // Costs
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skill Costs", EditorStyles.boldLabel);
            if (costsProp != null)
            {
                for (int i = 0; i < costsProp.arraySize; i++)
                {
                    var costElem = costsProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(costElem.FindPropertyRelative("resourceType"), GUIContent.none);
                    EditorGUILayout.PropertyField(costElem.FindPropertyRelative("amount"), GUIContent.none);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                        costsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add Cost"))
                    costsProp.InsertArrayElementAtIndex(costsProp.arraySize);
            }

            // Effects（交由 Drawer 渲染）
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);

            if (effectsProp == null)
            {
                EditorGUILayout.HelpBox("'effects' property not found on SkillDefinition.", MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            for (int i = 0; i < effectsProp.arraySize; i++)
            {
                var element = effectsProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical("box");

                var effectTypeProp = element.FindPropertyRelative("effectType");
                EditorGUILayout.PropertyField(effectTypeProp, new GUIContent("Effect Type"));

                var type = (EffectType)effectTypeProp.enumValueIndex;
                var drawer = EffectDrawerRegistry.Get(type);
                drawer.Draw(element);
                // Display duration field based on Skill level (if relevant for effect)
                if (FieldVisibilityUI.Toggle(element, EffectFieldMask.Duration, "Duration"))
                {
                    var durationProp = element.FindPropertyRelative("duration");
                    var perLevelProp = element.FindPropertyRelative("perLevel");
                    var durationLevelsProp = element.FindPropertyRelative("durationLevels");

                    bool hasDurationProp = durationProp != null;
                    bool hasLevelArray = durationLevelsProp != null;
                    bool hasPerLevelProp = perLevelProp != null;
                    bool perLevelUIVisible = hasPerLevelProp && hasLevelArray &&
                        FieldVisibilityUI.Has(element, EffectFieldMask.PerLevel);

                    bool perLevelEnabled = false;
                    bool collapsed = false;

                    if (perLevelUIVisible)
                    {
                        perLevelEnabled = PerLevelUI.BeginPerLevelBlock(element, out collapsed, collapseKeySuffix: "_duration");
                    }
                    else if (hasPerLevelProp && hasLevelArray)
                    {
                        perLevelEnabled = perLevelProp.boolValue;
                    }

                    if (hasDurationProp)
                    {
                        string label = perLevelEnabled ? "Default Duration (turns)" : "Duration (turns)";
                        EditorGUILayout.PropertyField(durationProp, new GUIContent(label));
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("'duration' property not found on effect.", MessageType.Warning);
                    }

                    int currentLevel = LevelContext.GetSkillLevel(element.serializedObject);
                    int resolvedDuration = hasDurationProp ? (int)durationProp.floatValue : 0;

                    if (perLevelEnabled && hasLevelArray)
                    {  
                            if (perLevelUIVisible && !collapsed)
                            {
                                PerLevelUI.DrawIntLevels(durationLevelsProp, "Duration by Level (turns)");
                            }

                        PerLevelUI.EnsureSize(durationLevelsProp, 4);
                        int idx = Mathf.Clamp(currentLevel - 1, 0, 3);
                        int levelValue = durationLevelsProp.GetArrayElementAtIndex(idx).intValue;
                        if (levelValue != 0)
                        {
                            resolvedDuration = levelValue;
                        }
                    }

                    EditorGUILayout.HelpBox($"Duration @L{currentLevel}: {resolvedDuration} turn(s)", MessageType.Info);
                }

                if (GUILayout.Button("Remove Effect"))
                    effectsProp.DeleteArrayElementAtIndex(i);

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Effect"))
                effectsProp.InsertArrayElementAtIndex(effectsProp.arraySize);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool IsLeveledColor(SkillColor color)
        {
            for (int i = 0; i < kLeveledColors.Length; i++)
                if (kLeveledColors[i] == color) return true;
            return false;
        }
    }
}

