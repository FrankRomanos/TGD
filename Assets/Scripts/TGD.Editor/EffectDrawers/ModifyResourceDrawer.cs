using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    public class ModifyResourceDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Modify Resource", EditorStyles.boldLabel);

            var resourceProp = elem.FindPropertyRelative("resourceType");
            if (resourceProp != null)
                EditorGUILayout.PropertyField(resourceProp, new GUIContent("Resource Type"));

            var effectTypeProp = elem.FindPropertyRelative("effectType");
            bool forceGainMode = effectTypeProp != null && (EffectType)effectTypeProp.enumValueIndex == EffectType.GainResource;

            var modifyTypeProp = elem.FindPropertyRelative("resourceModifyType");
            if (modifyTypeProp != null)
            {
                using (new EditorGUI.DisabledScope(forceGainMode))
                {
                    EditorGUILayout.PropertyField(modifyTypeProp, new GUIContent("Modify Type"));
                }
            }

            ResourceModifyType modifyType = ResourceModifyType.Gain;
            if (!forceGainMode && modifyTypeProp != null)
                modifyType = (ResourceModifyType)modifyTypeProp.enumValueIndex;

            bool showValueControls = modifyType == ResourceModifyType.Gain || modifyType == ResourceModifyType.ConvertMax;
            bool probabilityHandled = false;
            if (showValueControls)
                probabilityHandled = DrawValueControls(elem, modifyType);

            if (modifyType == ResourceModifyType.Lock ||
                modifyType == ResourceModifyType.Overdraft ||
                modifyType == ResourceModifyType.PayLate)
            {
                var stateProp = elem.FindPropertyRelative("resourceStateEnabled");
                if (stateProp != null)
                    EditorGUILayout.PropertyField(stateProp, new GUIContent("Enabled"));
            }

            if (!probabilityHandled)
            {
                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Target"))
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));

            var conditionProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(conditionProp, new GUIContent("Trigger Condition"));
            FieldVisibilityUI.DrawConditionFields(elem, conditionProp);
        }

        private static bool DrawValueControls(SerializedProperty elem, ResourceModifyType modifyType)
        {
            if (!FieldVisibilityUI.Toggle(elem, EffectFieldMask.PerLevel, "Values"))
                return false;

            bool collapsed;
            if (PerLevelUI.BeginPerLevelBlock(elem, out collapsed))
            {
                if (!collapsed)
                {
                    string header = modifyType == ResourceModifyType.ConvertMax
                        ? "Max Delta by Level"
                        : "Value by Level (formula)";
                    PerLevelUI.DrawStringLevels(elem.FindPropertyRelative("valueExprLevels"), header);
                }

                int curLv = LevelContext.GetSkillLevel(elem.serializedObject);
                bool showProb = FieldVisibilityUI.Has(elem, EffectFieldMask.Probability);
                PerLevelUI.DrawPreviewForCurrentLevel(elem, curLv, showDuration: false, showProb: showProb);
                return false;
            }
            else
            {
                var valueProp = FieldVisibilityUI.GetProp(elem, "valueExpression", "value");
                if (valueProp != null)
                {
                    string label = modifyType == ResourceModifyType.ConvertMax
                        ? "Max Delta / Expression"
                        : "Value / Expression";
                    if (modifyType == ResourceModifyType.Gain)
                        EditorGUILayout.HelpBox("Enter 'max' to restore the resource to its maximum.", MessageType.Info);
                    EditorGUILayout.PropertyField(valueProp, new GUIContent(label));
                }
                else
                {
                    EditorGUILayout.HelpBox("Missing 'valueExpression' (or legacy 'value') field.", MessageType.Warning);
                }

                if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Probability, "Probability"))
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("probability"), new GUIContent("Probability (%)"));
                return true;
            }
        }
    }
}
