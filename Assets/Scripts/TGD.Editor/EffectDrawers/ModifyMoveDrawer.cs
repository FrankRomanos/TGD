using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// Drawer for forced movement / reposition effects.
    /// </summary>
    public class MoveDrawer : IEffectDrawer
    {
        public void Draw(SerializedProperty elem)
        {
            EditorGUILayout.LabelField("Move / Forced Movement", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveSubject"), new GUIContent("Subject"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveExecution"), new GUIContent("Execution"));

            var directionProp = elem.FindPropertyRelative("moveDirection");
            EditorGUILayout.PropertyField(directionProp, new GUIContent("Direction"));
            var direction = (MoveDirection)directionProp.enumValueIndex;

            if (direction == MoveDirection.AbsoluteOffset)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveOffset"),
                    new GUIContent("Offset (tiles)"));
            }
            else
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveDistance"),
                    new GUIContent("Distance (tiles)"));

                if (direction == MoveDirection.TowardTarget)
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveMaxDistance"),
                        new GUIContent("Max Distance"));
                }
            }

            if (direction == MoveDirection.TowardTarget || direction == MoveDirection.AwayFromTarget)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveStopAdjacentToTarget"),
                    new GUIContent("Stop Adjacent To Target"));
            }

            EditorGUILayout.PropertyField(elem.FindPropertyRelative("forceMovement"),
                new GUIContent("Force Movement"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("allowPartialMove"),
                new GUIContent("Allow Partial Move"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("moveIgnoreObstacles"),
                new GUIContent("Ignore Obstacles"));

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Target, "Effect Target"))
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("target"), new GUIContent("Target"));
            }

            if (FieldVisibilityUI.Toggle(elem, EffectFieldMask.Condition, "Trigger Condition"))
            {
                DrawCondition(elem);
            }

            DrawSummary(elem, direction);
        }

        private void DrawCondition(SerializedProperty elem)
        {
            var condProp = elem.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condProp, new GUIContent("Trigger Condition"));

            if ((EffectCondition)condProp.enumValueIndex == EffectCondition.OnNextSkillSpendResource)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionResourceType"),
                    new GUIContent("Cond. Resource"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("conditionMinAmount"),
                    new GUIContent("Min Spend"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("consumeStatusOnTrigger"),
                    new GUIContent("Consume Status On Trigger"));
            }
        }

        private void DrawSummary(SerializedProperty elem, MoveDirection direction)
        {
            var subject = (MoveSubject)elem.FindPropertyRelative("moveSubject").enumValueIndex;
            var execution = (MoveExecution)elem.FindPropertyRelative("moveExecution").enumValueIndex;

            string detail = BuildDetail(elem, direction);
            string text = $"{SubjectToString((MoveSubject)subject)} performs a {execution.ToString().ToLower()} move {detail}";
            EditorGUILayout.HelpBox(text, MessageType.None);
        }

        private string BuildDetail(SerializedProperty elem, MoveDirection direction)
        {
            switch (direction)
            {
                case MoveDirection.AbsoluteOffset:
                    var offset = elem.FindPropertyRelative("moveOffset").vector2IntValue;
                    return $"to offset ({offset.x}, {offset.y}).";
                case MoveDirection.TowardTarget:
                    int max = elem.FindPropertyRelative("moveMaxDistance").intValue;
                    int dist = elem.FindPropertyRelative("moveDistance").intValue;
                    if (max > 0)
                        return $"toward target up to {max} tile(s) (prefers {dist}).";
                    return $"toward target by {dist} tile(s).";
                case MoveDirection.AwayFromTarget:
                    return $"away from target by {elem.FindPropertyRelative("moveDistance").intValue} tile(s).";
                default:
                    return $"{direction.ToString().ToLower()} by {elem.FindPropertyRelative("moveDistance").intValue} tile(s).";
            }
        }

        private string SubjectToString(MoveSubject subject)
        {
            switch (subject)
            {
                case MoveSubject.Caster:
                    return "Caster";
                case MoveSubject.PrimaryTarget:
                    return "Primary target";
                case MoveSubject.SecondaryTarget:
                    return "Secondary target";
                default:
                    return subject.ToString();
            }
        }
    }
}