using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Inspector drawer for the bespoke mastery posture engine.
    /// </summary>
    public class MasteryPostureDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Mastery ¨C Flowing Stance", EditorStyles.boldLabel);

            var settingsProp = elem.FindPropertyRelative("masteryPosture");
            if (settingsProp == null)
            {
                EditorGUILayout.HelpBox("'masteryPosture' property not found on EffectDefinition.", MessageType.Error);
                return;
            }

            DrawArmorConversion(settingsProp);
            EditorGUILayout.Space();
            DrawPostureResource(elem, settingsProp);
            EditorGUILayout.Space();
            DrawPostureBreak(settingsProp);
        }

        private void DrawArmorConversion(SerializedProperty settingsProp)
        {
            EditorGUILayout.LabelField("Armor Conversion", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("lockArmorToZero"),
                new GUIContent("Lock Armor To Zero"));

            var hpRatioProp = settingsProp.FindPropertyRelative("armorToHpRatio");
            var energyRatioProp = settingsProp.FindPropertyRelative("armorToEnergyRatio");

            if (hpRatioProp != null)
                EditorGUILayout.Slider(hpRatioProp, 0f, 1f, new GUIContent("Armor ¡ú HP Ratio"));
            if (energyRatioProp != null)
                EditorGUILayout.Slider(energyRatioProp, 0f, 1f, new GUIContent("Armor ¡ú Max Energy Ratio"));

            float hpShare = hpRatioProp != null ? hpRatioProp.floatValue : 0f;
            float energyShare = energyRatioProp != null ? energyRatioProp.floatValue : 0f;
            float total = hpShare + energyShare;
            EditorGUILayout.HelpBox(
                $"Armor gains are split {hpShare:P0} to HP / {energyShare:P0} to Max Energy (total {total:P0}).",
                MessageType.None);
        }

        private void DrawPostureResource(SerializedProperty elem, SerializedProperty settingsProp)
        {
            EditorGUILayout.LabelField("Posture Resource", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureResource"),
                new GUIContent("Resource Type"));
            EditorGUILayout.Slider(settingsProp.FindPropertyRelative("postureMaxHealthRatio"), 0f, 5f,
                new GUIContent("Max Posture vs HP"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureMaxExpression"),
                new GUIContent("Override Max Expression"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("masteryScalingExpression"),
                new GUIContent("Mastery Scaling Expression"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Damage ¡ú Posture", EditorStyles.boldLabel);

            bool drewValue = false;
            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Per-Level Conversion"))
            {
                bool collapsed;
                if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
                {
                    if (!collapsed)
                    {
                        PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"),
                            "Damage Conversion % by Level");
                    }

                    int currentLevel = LevelContext.GetSkillLevel(elem.serializedObject);
                    PerLevelUI.DrawPreviewForCurrentLevel(elem, currentLevel, showDuration: false, showProb: false);
                    drewValue = true;
                }
            }

            if (!drewValue)
            {
                var valueProp = elem.FindPropertyRelative("valueExpression");
                if (valueProp != null)
                {
                    EditorGUILayout.PropertyField(valueProp,
                        new GUIContent("Damage Conversion % (expression)"));
                }
                else
                {
                    EditorGUILayout.HelpBox("valueExpression property not found on effect.", MessageType.Warning);
                }
            }

            var recoveryProp = settingsProp.FindPropertyRelative("postureRecoveryPercentPerTurn");
            if (recoveryProp != null)
            {
                EditorGUILayout.Slider(recoveryProp, 0f, 1f, new GUIContent("Recovery per Turn (% of missing posture)"));
            }

            EditorGUILayout.HelpBox(
                "Configure how incoming damage is siphoned into the posture bar. Use 'p' or mastery-based expressions to scale conversion.",
                MessageType.Info);
        }

        private void DrawPostureBreak(SerializedProperty settingsProp)
        {
            EditorGUILayout.LabelField("Posture Break", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureBreakExtraDamageMultiplier"),
                new GUIContent("Extra Damage Multiplier"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureBreakStatusSkillID"),
                new GUIContent("Break Status Skill ID"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureBreakDurationTurns"),
                new GUIContent("Break Duration (turns)"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("postureBreakSkipsTurn"),
                new GUIContent("Skip Next Turn"));
        }
    }
}