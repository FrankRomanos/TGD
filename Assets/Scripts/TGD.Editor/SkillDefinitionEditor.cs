using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TGD.Data;

// ==== Odin（可选，若未连接 DLL 也不会崩；找不到类时请把 Sirenix 的 using 注释掉即可）====
using Sirenix.Utilities.Editor;   // SirenixEditorGUI / GUIHelper
// using Sirenix.OdinInspector;
// using Sirenix.OdinInspector.Editor;

namespace TGD.Editor
{
    [CustomEditor(typeof(SkillDefinition))]
    public class SkillDefinitionEditor : UnityEditor.Editor
    {
        // === 缓存的序列化属性 ===
        private SerializedProperty effectsProp;
        private SerializedProperty costsProp;
        private SerializedProperty skillColorProp;
        private SerializedProperty skillLevelProp;
        private SerializedProperty skillDurationProp;
        private SerializedProperty statusMetadataProp;
        private bool _effectsFoldAll = false;

        // 折叠状态
        private readonly Dictionary<int, bool> _effectFoldouts = new();

        private bool showStatusMetadataFoldout = true;
        private bool showStatusAccumulatorFoldout = true;

        private static readonly SkillColor[] kLeveledColors = new[]
        {
            SkillColor.DeepBlue, SkillColor.DarkYellow, SkillColor.Green, SkillColor.Purple, SkillColor.LightBlue
        };

        // ====== EffectType → 徽标（短标签 + 颜色）======
        private struct EffectStyle { public string Tag; public Color Color; }
        private static readonly Dictionary<EffectType, EffectStyle> kEffectStyles = new()
        {
            { EffectType.Damage,            new EffectStyle{ Tag="DMG",   Color=new Color(0.86f,0.23f,0.23f)} },
            { EffectType.Heal,              new EffectStyle{ Tag="HEAL",  Color=new Color(0.18f,0.70f,0.40f)} },
            { EffectType.GainResource,      new EffectStyle{ Tag="RES+",  Color=new Color(0.24f,0.56f,0.90f)} },
            { EffectType.ModifyResource,    new EffectStyle{ Tag="RES",   Color=new Color(0.20f,0.46f,0.80f)} },
            { EffectType.ScalingBuff,       new EffectStyle{ Tag="SCAL",  Color=new Color(1.00f,0.64f,0.00f)} },
            { EffectType.ModifyStatus,      new EffectStyle{ Tag="STATUS",Color=new Color(0.68f,0.48f,0.96f)} },
            { EffectType.NegativeStatus,    new EffectStyle{ Tag="NEG",   Color=new Color(0.80f,0.40f,0.40f)} },
            { EffectType.AttributeModifier, new EffectStyle{ Tag="ATTR",  Color=new Color(0.40f,0.74f,0.62f)} },
            { EffectType.ModifySkill,       new EffectStyle{ Tag="SKMOD", Color=new Color(0.20f,0.72f,0.72f)} },
            { EffectType.ReplaceSkill,      new EffectStyle{ Tag="REPL",  Color=new Color(0.55f,0.55f,0.62f)} },
            { EffectType.Move,              new EffectStyle{ Tag="MOVE",  Color=new Color(0.20f,0.80f,0.90f)} },
            { EffectType.ModifyAction,      new EffectStyle{ Tag="ACT",   Color=new Color(0.96f,0.40f,0.90f)} },
            { EffectType.CooldownModifier,  new EffectStyle{ Tag="CD",    Color=new Color(0.90f,0.80f,0.30f)} },
            { EffectType.ModifyDamageSchool,new EffectStyle{ Tag="SCHOOL",Color=new Color(0.96f,0.60f,0.40f)} },
            { EffectType.MasteryPosture,    new EffectStyle{ Tag="POST",  Color=new Color(0.88f,0.52f,0.66f)} },
            { EffectType.RandomOutcome,     new EffectStyle{ Tag="RND",   Color=new Color(0.96f,0.80f,0.30f)} },
            { EffectType.Repeat,            new EffectStyle{ Tag="REP",   Color=new Color(0.40f,0.66f,0.95f)} },
            { EffectType.ProbabilityModifier,new EffectStyle{Tag="PR",    Color=new Color(0.55f,0.70f,0.25f)} },
            { EffectType.DotHotModifier,    new EffectStyle{ Tag="DOT/H", Color=new Color(0.94f,0.49f,0.24f)} },
            { EffectType.Aura,              new EffectStyle{ Tag="AURA",  Color=new Color(0.58f,0.44f,0.88f)} },
            { EffectType.ModifyDefence,     new EffectStyle{ Tag="DEF",   Color=new Color(0.45f,0.55f,0.76f)} },
        };

        // SkillColor → Tint
        private static readonly Dictionary<SkillColor, Color> kSkillTints = new()
        {
            { SkillColor.DeepBlue,  new Color(0.13f,0.22f,0.46f, 0.20f) },
            { SkillColor.DarkYellow,new Color(0.50f,0.40f,0.10f, 0.20f) },
            { SkillColor.Green,     new Color(0.10f,0.45f,0.25f, 0.20f) },
            { SkillColor.Purple,    new Color(0.35f,0.20f,0.55f, 0.20f) },
            { SkillColor.LightBlue, new Color(0.20f,0.55f,0.75f, 0.20f) },
            { SkillColor.Red,       new Color(0.65f,0.20f,0.20f, 0.20f) },
            { SkillColor.None,      new Color(0,0,0,0) }
        };

        private static Color GetSkillTint(SkillColor c) => kSkillTints.TryGetValue(c, out var col) ? col : new Color(0, 0, 0, 0);

        private static bool IsLeveledColor(SkillColor color)
        {
            for (int i = 0; i < kLeveledColors.Length; i++)
                if (kLeveledColors[i] == color) return true;
            return false;
        }

        private static void DrawTintBar(string title, Color tint, float height = 26f)
        {
            var r = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && tint.a > 0f)
                EditorGUI.DrawRect(r, tint);
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = tint.a > 0 ? Color.white : EditorStyles.boldLabel.normal.textColor;
            r.x += 6; r.width -= 12;
            GUI.Label(r, title, style);
        }

        private static void DrawBadge(string tag, Color color)
        {
            var bg = new Color(color.r, color.g, color.b, 0.22f);
            var h = 18f;
            var w = Mathf.Max(36f, 10f + tag.Length * 8f);
            var r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, bg);
                var border = new Rect(r.x, r.y, r.width, 1f);
                EditorGUI.DrawRect(border, color);
            }
            var s = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            s.normal.textColor = color;
            GUI.Label(r, tag, s);
        }

        private static bool ButtonMini(string text, float width = 70f)
        {
            return GUILayout.Button(text, GUILayout.Width(width), GUILayout.Height(18));
        }

        private static string EnumNamesOrFallback(SerializedProperty enumProp)
        {
            try { return string.Join("/", enumProp.enumDisplayNames ?? Array.Empty<string>()); }
            catch { return "<Enum>"; }
        }

        private void OnEnable()
        {
            effectsProp = serializedObject.FindProperty("effects");
            costsProp = serializedObject.FindProperty("costs");
            skillColorProp = serializedObject.FindProperty("skillColor");
            skillLevelProp = serializedObject.FindProperty("skillLevel");
            skillDurationProp = serializedObject.FindProperty("skillDuration");
            statusMetadataProp = serializedObject.FindProperty("statusMetadata");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 顶部彩条（按 SkillColor 着色）
            var colorEnum = skillColorProp != null ? (SkillColor)skillColorProp.enumValueIndex : SkillColor.None;
            var tint = GetSkillTint(colorEnum);
            var skillIdProp = serializedObject.FindProperty("skillID");
            var skillNameProp = serializedObject.FindProperty("skillName");
            DrawTintBar($"Skill: {skillNameProp?.stringValue ?? "<unnamed>"}   [{skillIdProp?.stringValue ?? "-"}]", tint);

            // ===== 基础信息 =====
            EditorGUILayout.PropertyField(skillIdProp);
            EditorGUILayout.PropertyField(skillNameProp);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("classID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("moduleID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("variantKey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("chainNextID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resetOnTurnEnd"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillTag"));

            // 颜色 + 等级
            EditorGUILayout.PropertyField(skillColorProp, new GUIContent("Skill Color"));
            bool leveled = skillColorProp != null && IsLeveledColor((SkillColor)skillColorProp.enumValueIndex);
            if (leveled)
            {
                EditorGUILayout.IntSlider(skillLevelProp, 1, 4, new GUIContent("Skill Level"));
            }
            else
            {
                skillLevelProp.intValue = 1; // 非五色不分级
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skill Duration", EditorStyles.boldLabel);

            if (skillDurationProp != null)
            {
                var durationValueProp = skillDurationProp.FindPropertyRelative("duration");
                var levelsProp = skillDurationProp.FindPropertyRelative("durationLevels");

                if (durationValueProp != null)
                {
                    bool collapsed;
                    if (PerLevelUI.BeginPerLevelBlock(skillDurationProp, out collapsed, "Use Per-Level Duration"))
                    {
                        if (!collapsed)
                        {
                            if (levelsProp != null)
                                PerLevelUI.DrawIntLevels(levelsProp, "Duration by Level (turns)");
                        }

                        if (levelsProp != null)
                        {
                            PerLevelUI.EnsureSize(levelsProp, 4);
                            int currentLevel = LevelContext.GetSkillLevel(serializedObject);
                            int idx = Mathf.Clamp(currentLevel - 1, 0, 3);
                            int value = levelsProp.GetArrayElementAtIndex(idx).intValue;
                            if (value == 0) value = durationValueProp.intValue;
                            EditorGUILayout.HelpBox($"Duration @L{currentLevel}: {value} turn(s)", MessageType.Info);
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(durationValueProp, new GUIContent("Duration (turns)"));
                    }
                }
            }

            // ===== 类型与通用数值 =====
            var skillTypeProp = serializedObject.FindProperty("skillType");
            DrawPropertyField(skillTypeProp, nameof(SkillDefinition.skillType), new GUIContent("Skill Type"));

            var timeCostSecondsProp = serializedObject.FindProperty("timeCostSeconds");
            var targetTypeProp = serializedObject.FindProperty("targetType");
            var cooldownSecondsProp = serializedObject.FindProperty("cooldownSeconds");
            var cooldownTurnsProp = serializedObject.FindProperty("cooldownTurns");
            var rangeProp = serializedObject.FindProperty("range");
            var threatProp = serializedObject.FindProperty("threat");
            var shredMultiplierProp = serializedObject.FindProperty("shredMultiplier");

            SkillType skillType = skillTypeProp != null ? (SkillType)skillTypeProp.enumValueIndex : SkillType.None;

            if (skillType == SkillType.Mastery)
            {
                var masteryRatioProp = serializedObject.FindProperty("masteryStatConversionRatio");
                if (masteryRatioProp != null)
                {
                    EditorGUILayout.PropertyField(
                        masteryRatioProp,
                        new GUIContent("Mastery → Stat Ratio",
                        "Fraction of this class mastery converted into the shared mastery stat (used by 'p')."));
                }
                if (targetTypeProp != null) targetTypeProp.enumValueIndex = (int)SkillTargetType.None;
                if (timeCostSecondsProp != null) timeCostSecondsProp.intValue = 0;
                if (cooldownSecondsProp != null) cooldownSecondsProp.intValue = 0;
                if (cooldownTurnsProp != null) cooldownTurnsProp.intValue = 0;
                if (threatProp != null) threatProp.floatValue = 0f;
                if (shredMultiplierProp != null) shredMultiplierProp.floatValue = 0f;
            }
            else
            {
                DrawPropertyField(timeCostSecondsProp, nameof(SkillDefinition.timeCostSeconds));
                DrawPropertyField(targetTypeProp, nameof(SkillDefinition.targetType));
                DrawPropertyField(cooldownSecondsProp, nameof(SkillDefinition.cooldownSeconds));
                DrawPropertyField(cooldownTurnsProp, nameof(SkillDefinition.cooldownTurns));
                DrawPropertyField(rangeProp, nameof(SkillDefinition.range));
                DrawPropertyField(threatProp, nameof(SkillDefinition.threat));
                DrawPropertyField(shredMultiplierProp, nameof(SkillDefinition.shredMultiplier));
            }

            var actionTypeProp = serializedObject.FindProperty("actionType");
            DrawPropertyField(actionTypeProp, nameof(SkillDefinition.actionType), new GUIContent("Action Type"));
            var actionType = actionTypeProp != null ? (ActionType)actionTypeProp.enumValueIndex : ActionType.None;

            if (actionType == ActionType.FullRound)
            {
                if (timeCostSecondsProp != null)
                    timeCostSecondsProp.intValue = -1;
                EditorGUILayout.HelpBox(
                    "Full Round: Consumes all remaining time this turn and ends the turn immediately. (Time Cost = -1)",
                    MessageType.Info);
            }

            // 类型/动作未选时警告
            if (skillType == SkillType.None)
                EditorGUILayout.HelpBox("Skill Type 未选择。", MessageType.Warning);
            if (actionType == ActionType.None)
                EditorGUILayout.HelpBox("Action Type 未选择。", MessageType.Warning);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("namekey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("descriptionKey"));
            if (skillType == SkillType.State)
            {
                DrawStatusMetadataSection();
            }

            // ===== COSTS（只高亮，不报错）=====
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skill Costs", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");
            if (costsProp != null)
            {
                // 轻微高亮背景（仅内容区域）
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 1f, 0.85f, 1f);

                for (int i = 0; i < costsProp.arraySize; i++)
                {
                    var costElem = costsProp.GetArrayElementAtIndex(i);
                    var resourceProp = costElem.FindPropertyRelative("resourceType");
                    var amountProp = costElem.FindPropertyRelative("amount");
                    var amountExpressionProp = costElem.FindPropertyRelative("amountExpression");

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(resourceProp, GUIContent.none);

                    string currentDisplay = (amountExpressionProp != null && !string.IsNullOrWhiteSpace(amountExpressionProp.stringValue))
                        ? amountExpressionProp.stringValue
                        : (amountProp != null ? amountProp.intValue.ToString(CultureInfo.InvariantCulture) : string.Empty);

                    EditorGUI.BeginChangeCheck();
                    string newValue = EditorGUILayout.DelayedTextField(
                        new GUIContent(string.Empty, "Enter a number or expression (e.g., maxhp*0.5)."),
                        currentDisplay);
                    if (EditorGUI.EndChangeCheck())
                    {
                        string trimmed = string.IsNullOrWhiteSpace(newValue) ? string.Empty : newValue.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                        {
                            if (amountProp != null) amountProp.intValue = 0;
                            if (amountExpressionProp != null) amountExpressionProp.stringValue = string.Empty;
                        }
                        else if (amountProp != null && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        {
                            amountProp.intValue = parsed;
                            if (amountExpressionProp != null) amountExpressionProp.stringValue = string.Empty;
                        }
                        else if (amountExpressionProp != null)
                        {
                            amountExpressionProp.stringValue = trimmed;
                            if (amountProp != null) amountProp.intValue = 0;
                        }
                    }

                    if (ButtonMini("Remove"))
                    {
                        costsProp.DeleteArrayElementAtIndex(i);
                        serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("＋ Add Cost", GUILayout.Height(24)))
                {
                    int idx = costsProp.arraySize;
                    costsProp.InsertArrayElementAtIndex(idx);
                    serializedObject.ApplyModifiedProperties();

                    var newElem = costsProp.GetArrayElementAtIndex(idx);
                    var resProp = newElem.FindPropertyRelative("resourceType");
                    var amtProp = newElem.FindPropertyRelative("amount");
                    var exprProp = newElem.FindPropertyRelative("amountExpression");
                    if (resProp != null) resProp.enumValueIndex = (int)CostResourceType.Energy;
                    if (amtProp != null) amtProp.intValue = 1;
                    if (exprProp != null) exprProp.stringValue = string.Empty;

                    serializedObject.ApplyModifiedProperties();
                    Repaint();
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = prevBg;
            }
            else
            {
                EditorGUILayout.HelpBox("'costs' property not found on SkillDefinition.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            // ===== Use Conditions =====
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Use Conditions", EditorStyles.boldLabel);
            var useConditionsProp = serializedObject.FindProperty("useConditions");
            if (useConditionsProp != null)
            {
                for (int i = 0; i < useConditionsProp.arraySize; i++)
                {
                    var condElem = useConditionsProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical("box");
                    if (!DrawUseConditionClause(condElem, false, useConditionsProp, i))
                    {
                        EditorGUILayout.EndVertical();
                        break;
                    }

                    var useSecondProp = condElem.FindPropertyRelative("useSecondCondition");
                    EditorGUILayout.PropertyField(useSecondProp, new GUIContent("Enable Secondary Condition"));
                    if (useSecondProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(condElem.FindPropertyRelative("secondConditionLogic"), new GUIContent("Logic Operator"));
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("Secondary Condition", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        DrawUseConditionClause(condElem, true);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("Add Condition"))
                    useConditionsProp.InsertArrayElementAtIndex(useConditionsProp.arraySize);
            }
            else
            {
                EditorGUILayout.HelpBox("'useConditions' property not found on SkillDefinition.", MessageType.Warning);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_effectsFoldAll ? "Collapse All" : "Expand All", GUILayout.Height(20)))
            {
                _effectsFoldAll = !_effectsFoldAll;
                for (int i = 0; i < effectsProp.arraySize; i++) _effectFoldouts[i] = _effectsFoldAll;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();


            // ===== Effects（折叠 + 彩色徽标）=====
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            var row = new List<string>();
            foreach (var kv in kEffectStyles)
                row.Add(kv.Value.Tag);
            var legend = string.Join("   ", row.Distinct());
            var col = new Color(0f, 0f, 0f, 0.06f);
            var r = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, col);
            GUI.Label(r, "Legend: " + legend, EditorStyles.miniBoldLabel);


            if (effectsProp == null)
            {
                EditorGUILayout.HelpBox("'effects' property not found on SkillDefinition.", MessageType.Error);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            for (int i = 0; i < effectsProp.arraySize; i++)
            {
                var element = effectsProp.GetArrayElementAtIndex(i);
                var effectTypeProp = element.FindPropertyRelative("effectType");

                // 兼容旧 ApplyStatus
                if (effectTypeProp.intValue == EffectTypeLegacy.ApplyStatus)
                {
                    effectTypeProp.intValue = (int)EffectType.ModifyStatus;
                    var modifyTypeProp = element.FindPropertyRelative("statusModifyType");
                    if (modifyTypeProp != null)
                        modifyTypeProp.intValue = (int)StatusModifyType.ApplyStatus;
                }

                var effectType = (EffectType)effectTypeProp.intValue;
                kEffectStyles.TryGetValue(effectType, out var style);

                // 折叠头（徽标 + 标题）
                EditorGUILayout.BeginHorizontal();
                _effectFoldouts.TryGetValue(i, out bool open);
                open = EditorGUILayout.Foldout(open, $"Effect #{i + 1} — {effectType}", true);
                _effectFoldouts[i] = open;
                GUILayout.FlexibleSpace();
                DrawBadge(style.Tag ?? effectType.ToString(), style.Color.a > 0 ? style.Color : new Color(0.6f, 0.6f, 0.6f));
                EditorGUILayout.EndHorizontal();

                if (open)
                {
                    EditorGUILayout.BeginVertical("box");

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(effectTypeProp, new GUIContent("Effect Type"));
                    GUILayout.FlexibleSpace();
                    if (ButtonMini("Remove"))
                    {
                        effectsProp.DeleteArrayElementAtIndex(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    // 交给 Drawer 渲染
                    var drawer = EffectDrawerRegistry.Get(effectType);
                    drawer.Draw(element);

                    // —— 可选：Duration per-level（保持你原逻辑）——
                    if (FieldVisibilityUI.Toggle(element, EffectFieldMask.Duration, "Duration"))
                    {
                        var durationProp = element.FindPropertyRelative("duration");
                        string perLevelPropertyName = "perLevelDuration";
                        var perLevelProp = element.FindPropertyRelative(perLevelPropertyName);
                        if (perLevelProp == null)
                        {
                            perLevelPropertyName = "perLevel";
                            perLevelProp = element.FindPropertyRelative(perLevelPropertyName);
                        }
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
                            perLevelEnabled = PerLevelUI.BeginPerLevelBlock(
                                element, out collapsed, "Use Per-Level Duration", "_duration", perLevelPropertyName);
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
                        int currentLevel = LevelContext.GetSkillLevel(element.serializedObject);
                        int resolvedDuration = hasDurationProp ? (int)durationProp.floatValue : 0;

                        if (perLevelEnabled && hasLevelArray)
                        {
                            if (perLevelUIVisible && !collapsed)
                                PerLevelUI.DrawIntLevels(durationLevelsProp, "Duration by Level (turns)");

                            PerLevelUI.EnsureSize(durationLevelsProp, 4);
                            int idx = Mathf.Clamp(currentLevel - 1, 0, 3);
                            int levelValue = durationLevelsProp.GetArrayElementAtIndex(idx).intValue;
                            if (levelValue != 0) resolvedDuration = levelValue;
                        }

                        EditorGUILayout.HelpBox($"Duration @L{currentLevel}: {resolvedDuration} turn(s)", MessageType.Info);
                    }

                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            }

            if (GUILayout.Button("＋ Add Effect", GUILayout.Height(26)))
                effectsProp.InsertArrayElementAtIndex(effectsProp.arraySize);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusMetadataSection()
        {
            EditorGUILayout.Space();
            if (statusMetadataProp == null)
            {
                EditorGUILayout.HelpBox("'statusMetadata' property not found on SkillDefinition.", MessageType.Warning);
                return;
            }

            showStatusMetadataFoldout = EditorGUILayout.Foldout(showStatusMetadataFoldout, "Status Metadata", true);
            if (!showStatusMetadataFoldout)
                return;

            EditorGUI.indentLevel++;
            var accumulatorProp = statusMetadataProp.FindPropertyRelative("accumulatorSettings");
            DrawStatusAccumulatorSettings(accumulatorProp);
            EditorGUI.indentLevel--;
        }

        private void DrawStatusAccumulatorSettings(SerializedProperty accumulatorProp)
        {
            if (accumulatorProp == null)
            {
                EditorGUILayout.HelpBox("'accumulatorSettings' property not found on status metadata.", MessageType.Warning);
                return;
            }

            showStatusAccumulatorFoldout = EditorGUILayout.Foldout(showStatusAccumulatorFoldout, "Status Accumulator Settings", true);
            if (!showStatusAccumulatorFoldout)
                return;

            EditorGUI.indentLevel++;
            var enabledProp = accumulatorProp.FindPropertyRelative("enabled");
            if (enabledProp == null)
            {
                EditorGUILayout.HelpBox("'enabled' property not found on status accumulator settings.", MessageType.Warning);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enabled"));
            if (enabledProp.boolValue)
            {
                EditorGUI.indentLevel++;
                var sourceProp = accumulatorProp.FindPropertyRelative("source");
                StatusAccumulatorSource sourceValue = StatusAccumulatorSource.DamageTaken;
                if (sourceProp != null)
                {
                    EditorGUILayout.PropertyField(sourceProp, new GUIContent("Source"));
                    sourceValue = (StatusAccumulatorSource)sourceProp.enumValueIndex;
                }

                var fromProp = accumulatorProp.FindPropertyRelative("from");
                if (fromProp != null)
                    EditorGUILayout.PropertyField(fromProp, new GUIContent("From"));

                var amountProp = accumulatorProp.FindPropertyRelative("amount");
                if (amountProp != null)
                    EditorGUILayout.PropertyField(amountProp, new GUIContent("Amount"));

                var includeProp = accumulatorProp.FindPropertyRelative("includeDotHot");
                if (includeProp != null)
                    EditorGUILayout.PropertyField(includeProp, new GUIContent("Include DoT/HoT"));

                if (sourceValue == StatusAccumulatorSource.DamageTaken)
                {
                    var schoolProp = accumulatorProp.FindPropertyRelative("damageSchool");
                    if (schoolProp != null)
                        EditorGUILayout.PropertyField(schoolProp, new GUIContent("Damage School"));
                }

                string variableKey = StatusAccumulatorSettings.GetVariableKey(sourceValue);
                EditorGUILayout.HelpBox($"Accumulated value is stored in custom variable '{variableKey}'.", MessageType.Info);

                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("Accumulator disabled. No values will be tracked for this status.", MessageType.None);
            }

            EditorGUI.indentLevel--;
        }

        private static bool DrawUseConditionClause(SerializedProperty condElem, bool secondary, SerializedProperty list = null, int index = -1)
        {
            string typePropName = secondary ? "secondConditionType" : "conditionType";
            string targetPropName = secondary ? "secondTarget" : "target";
            string resourcePropName = secondary ? "secondResourceType" : "resourceType";
            string compareOpPropName = secondary ? "secondCompareOp" : "compareOp";
            string compareValuePropName = secondary ? "secondCompareValue" : "compareValue";
            string compareExprPropName = secondary ? "secondCompareValueExpression" : "compareValueExpression";
            string minDistancePropName = secondary ? "secondMinDistance" : "minDistance";
            string maxDistancePropName = secondary ? "secondMaxDistance" : "maxDistance";
            string requireLinePropName = secondary ? "secondRequireLineOfSight" : "requireLineOfSight";
            string skillIdPropName = secondary ? "secondSkillID" : "skillID";

            var conditionTypeProp = condElem.FindPropertyRelative(typePropName);
            GUIContent typeLabel = secondary
                ? new GUIContent("Condition Type (Secondary)")
                : new GUIContent("Condition Type");

            if (!secondary)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(conditionTypeProp, typeLabel);
                if (list != null && index >= 0)
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        list.DeleteArrayElementAtIndex(index);
                        EditorGUILayout.EndHorizontal();
                        return false;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.PropertyField(conditionTypeProp, typeLabel);
            }

            var targetProp = condElem.FindPropertyRelative(targetPropName);
            EditorGUILayout.PropertyField(targetProp, secondary
                ? new GUIContent("Condition Target (Secondary)")
                : new GUIContent("Condition Target"));

            var conditionType = (SkillCostConditionType)conditionTypeProp.enumValueIndex;
            switch (conditionType)
            {
                case SkillCostConditionType.Resource:
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(resourcePropName), new GUIContent("Resource"));
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(compareOpPropName), new GUIContent("Compare"));
                    var exprProp = condElem.FindPropertyRelative(compareExprPropName);
                    EditorGUILayout.PropertyField(exprProp, new GUIContent("Value Expression"));
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(compareValuePropName), new GUIContent("Value (Fallback)"));
                    EditorGUILayout.HelpBox("Expressions can reference caster/target stats, e.g. 0.1*maxhp.", MessageType.None);
                    break;
                case SkillCostConditionType.Distance:
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(minDistancePropName), new GUIContent("Min Distance"));
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(maxDistancePropName), new GUIContent("Max Distance (0 = ignore)"));
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(requireLinePropName), new GUIContent("Require Clear Path"));
                    EditorGUILayout.HelpBox("Distance is evaluated between the caster and the chosen target.", MessageType.None);
                    break;
                case SkillCostConditionType.PerformHeal:
                    EditorGUILayout.HelpBox("Requires the skill to perform healing on the specified target.", MessageType.None);
                    break;
                case SkillCostConditionType.PerformAttack:
                    EditorGUILayout.HelpBox("Requires the skill to deal damage to the specified target.", MessageType.None);
                    break;
                case SkillCostConditionType.SkillStateActive:
                    EditorGUILayout.PropertyField(condElem.FindPropertyRelative(skillIdPropName), new GUIContent("Skill ID"));
                    EditorGUILayout.HelpBox("Requires the specified skill state to be active on the target.", MessageType.None);
                    break;
            }

            return true;
        }

        private static void DrawPropertyField(SerializedProperty property, string propertyName, GUIContent label = null)
        {
            if (property != null)
            {
                if (label != null) EditorGUILayout.PropertyField(property, label);
                else EditorGUILayout.PropertyField(property);
            }
            else
            {
                EditorGUILayout.HelpBox($"'{propertyName}' property not found on SkillDefinition.", MessageType.Error);
            }
        }
    }
}
