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
                   new GUIContent("Offset (axial q,r)"));
            }
            else
            {
                var distanceExpressionProp = elem.FindPropertyRelative("moveDistanceExpression");
                var fallbackDistanceProp = elem.FindPropertyRelative("moveDistance");

                EditorGUILayout.PropertyField(distanceExpressionProp,
                    new GUIContent("Distance (tiles)"));
                EditorGUILayout.PropertyField(fallbackDistanceProp,
                    new GUIContent("Distance Fallback (tiles)"));
                EditorGUILayout.HelpBox("Expressions support variables such as 'targetdistance'. Leave empty to use the fallback value.", MessageType.None);

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
            FieldVisibilityUI.DrawConditionFields(elem, condProp);
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
                    return $"to axial offset ({offset.x}, {offset.y}).";
                case MoveDirection.TowardTarget:
                    int max = elem.FindPropertyRelative("moveMaxDistance").intValue;
                    string towardLabel = FormatDistanceLabel(elem);
                    if (max > 0)
                        return $"toward target up to {max} tile(s) (prefers {towardLabel}).";
                    return $"toward target by {towardLabel} tile(s).";
                case MoveDirection.AwayFromTarget:
                    return $"away from target by {FormatDistanceLabel(elem)} tile(s).";
                default:
                    return $"{direction.ToString().ToLower()} by {FormatDistanceLabel(elem)} tile(s).";
            }
        }
        private string FormatDistanceLabel(SerializedProperty elem)
        {
            var expressionProp = elem.FindPropertyRelative("moveDistanceExpression");
            string expression = expressionProp != null ? expressionProp.stringValue : string.Empty;
            int fallback = elem.FindPropertyRelative("moveDistance").intValue;
            if (string.IsNullOrWhiteSpace(expression))
                return fallback.ToString();

            return $"{expression.Trim()} (~{fallback})";
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