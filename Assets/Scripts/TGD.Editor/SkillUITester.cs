using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TGD.Data;
using TGD.Editor;

namespace TGD.UI
{
    public class SkillUITester_Simple : EditorWindow
    {
        // 原有：本地化与技能数据变量
        // Cached localization entries loaded from CSV (reused between refreshes).
        private readonly Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        private string localizationFilePath = "Assets/Localization/Localization_Skills.csv";
        private readonly List<SkillDefinition> allSkillDatas = new List<SkillDefinition>();
        private SkillDefinition selectedSkill;
        private Vector2 scrollPos;
        private Vector2 effectScrollPos;
        private GUIStyle selectedMiniButton;

        // Lazily created styles used to render effect previews in a consistent manner.
        private GUIStyle effectTitleStyle;
        private GUIStyle effectItemContainerStyle;
        private GUIStyle effectContentStyle;
        private readonly Dictionary<SkillColor, GUIStyle> skillColorTextStyles = new();
        [MenuItem("Tools/Skill/简化版UI测试窗口")]
        public static void OpenTestWindow()
        {
            GetWindow<SkillUITester_Simple>("技能UI测试（简化版）").minSize = new Vector2(900, 600);
        }

        private void OnEnable()
        {
            // 原有：技能列表选中按钮样式初始化
            selectedMiniButton = new GUIStyle(EditorStyles.miniButton);
            selectedMiniButton.normal.background = EditorStyles.toolbarButton.active.background;

            // 原有：加载本地化与技能数据
            LoadLocalizationData();
            LoadSkillDefinitions();
        }


        private void LoadSkillDefinitions()
        {
            string skillDataPath = "SkillData";
            allSkillDatas.Clear();
            SkillDefinition[] loadedSkills = Resources.LoadAll<SkillDefinition>(skillDataPath);
            if (loadedSkills != null)
            {
                foreach (var skill in loadedSkills)
                {
                    if (skill == null)
                    {
                        Debug.LogWarning("SkillUITester_Simple: Encountered null SkillDefinition while loading resources.");
                        continue;
                    }

                    if (skill.skillDuration == null)
                        skill.skillDuration = new SkillDurationSettings();

                    allSkillDatas.Add(skill);
                }
            }

            allSkillDatas.Sort(CompareSkills);

            if (allSkillDatas.Count > 0)
                selectedSkill = allSkillDatas[0];
            else
                selectedSkill = null;
        }

        private int CompareSkills(SkillDefinition a, SkillDefinition b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            bool parsedA = int.TryParse(a.skillID, out int idA);
            bool parsedB = int.TryParse(b.skillID, out int idB);
            if (parsedA && parsedB)
                return idA.CompareTo(idB);

            int numA = ExtractNumberFromSkillID(a.skillID);
            int numB = ExtractNumberFromSkillID(b.skillID);
            int compare = numA.CompareTo(numB);
            if (compare != 0)
                return compare;

            return string.Compare(a.skillID, b.skillID, StringComparison.OrdinalIgnoreCase);
        }

        // 原有：加载本地化数据
        private void LoadLocalizationData()
        {
            localizationDict.Clear();
            if (!File.Exists(localizationFilePath))
            {
                Debug.LogError("本地化文件找不到！路径：" + localizationFilePath);
                return;
            }
            string[] allLines = File.ReadAllLines(localizationFilePath);
            if (allLines.Length < 2)
            {
                Debug.LogError("本地化文件内容为空或格式错误！");
                return;
            }
            for (int i = 1; i < allLines.Length; i++)
            {
                string line = allLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                string[] parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    string key = parts[0].Trim();
                    string desc = parts[1].Trim();
                    if (!localizationDict.ContainsKey(key))
                        localizationDict.Add(key, desc);
                }
            }
            Debug.Log("本地化数据加载完成，共" + localizationDict.Count + "条");
        }

        private void OnGUI()
        {
            // 初始化Effect相关样式（修复边框颜色错误）
            InitEffectStyles();

            GUILayout.Label("技能UI测试（图标+基础信息）", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 左右分栏：左侧选技能，右侧看详情
            GUILayout.BeginHorizontal();

            // 左侧：技能列表（可修改：宽度280像素）
            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label("技能列表", EditorStyles.miniBoldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(400));
            foreach (var skill in allSkillDatas)
            {
                if (skill == null)
                    continue;

                string idDisplay = string.IsNullOrWhiteSpace(skill.skillID) ? "--" : skill.skillID;
                string nameDisplay = string.IsNullOrWhiteSpace(skill.skillName) ? "(Unnamed)" : skill.skillName;
                if (GUILayout.Button(
                    $"ID:{idDisplay} | {nameDisplay}",
                    selectedSkill == skill ? selectedMiniButton : EditorStyles.miniButton))
                {
                    selectedSkill = skill;
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(20);

            // 右侧：技能详情（可修改：宽度600像素）
            GUILayout.BeginVertical(GUILayout.Width(600));
            GUILayout.Label("技能详情", EditorStyles.miniBoldLabel);
            GUILayout.Space(5);

            if (selectedSkill != null)
            {
                // 原有：技能图标
                GUILayout.Label("技能图标：", EditorStyles.miniLabel);
                if (selectedSkill.icon != null && selectedSkill.icon.texture != null)
                {
                    GUILayout.Label(new GUIContent(selectedSkill.icon.texture), GUILayout.Width(80), GUILayout.Height(80));
                }
                else
                {
                    GUILayout.Label("无图标", EditorStyles.helpBox, GUILayout.Width(100), GUILayout.Height(100));
                }

                // 原有：技能名称/描述/属性
                GUILayout.Label("技能名称：");
                EditorGUILayout.TextArea(
     GetLocalizedDesc(selectedSkill.namekey),
     GetSkillTextStyle(selectedSkill.skillColor),
     GUILayout.Height(40));
                GUILayout.Label("技能描述：");
                EditorGUILayout.TextArea(
            GetLocalizedDesc(selectedSkill.descriptionKey),
            GetSkillTextStyle(selectedSkill.skillColor),
            GUILayout.Height(50));
                GUILayout.Label("基础属性：", EditorStyles.miniBoldLabel);
                GUILayout.Label($"职业：{selectedSkill.classID}");
                GUILayout.Label($"技能类型：{selectedSkill.skillType}");
                GUILayout.Label($"动作类型：{selectedSkill.actionType}");
                GUILayout.Label($"冷却时间：{selectedSkill.cooldownSeconds}秒 / {selectedSkill.cooldownTurns}回合");
                GUILayout.Label($"技能持续：{FormatSkillDurationLabel(selectedSkill.ResolveDuration())}");
                GUILayout.Space(10);

                // Effect标题
                GUILayout.Label("Effect Preview", effectTitleStyle);
                GUILayout.Space(5);  // 标题与下方边框的间距
                // Effect列表区域
                using (var serializedSkill = new SerializedObject(selectedSkill))
                {
                    serializedSkill.Update();
                    SerializedProperty effectsProp = serializedSkill.FindProperty("effects");
                    if (effectsProp != null && effectsProp.isArray && effectsProp.arraySize > 0)
                    {
                        effectScrollPos = EditorGUILayout.BeginScrollView(effectScrollPos, GUILayout.Height(600), GUILayout.ExpandWidth(true));
                        for (int i = 0; i < effectsProp.arraySize; i++)
                        {
                            SerializedProperty effectProp = effectsProp.GetArrayElementAtIndex(i);
                            string summary;
                            try
                            {
                                summary = EffectSummaryUtility.BuildSummary(effectProp, selectedSkill);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"SkillUITester_Simple: Failed to build effect summary for {selectedSkill?.skillID}: {ex.Message}");
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(summary)) continue;

                            // 单个Effect条目（最小高度120像素）
                            GUILayout.BeginVertical(effectItemContainerStyle, GUILayout.MinHeight(120));
                            GUILayout.Label($"Effect {i + 1}", effectTitleStyle);
                            GUILayout.Space(5);
                            GUILayout.Label(summary, effectContentStyle);
                            GUILayout.EndVertical();
                            GUILayout.Space(8);
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    else
                    {
                        GUILayout.BeginVertical(effectItemContainerStyle, GUILayout.MinHeight(120));
                        GUILayout.Label("No effects configured for this skill.", effectContentStyle);
                        GUILayout.EndVertical();
                    }
                }
            }
            else
            {
                GUILayout.Label("请从左侧选择一个技能", EditorStyles.helpBox);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // 关键修复：初始化Effect样式（移除错误的border.color，用背景纹理实现边框效果）
        private void InitEffectStyles()
        {
            // 1. Effect标题样式（蓝色字体，醒目）
            if (effectTitleStyle == null)
            {
                effectTitleStyle = new GUIStyle(EditorStyles.boldLabel);
                effectTitleStyle.fontSize = 14; // 可修改：标题字体大小
                effectTitleStyle.margin = new RectOffset(0, 0, 2, 0);
                effectTitleStyle.normal.textColor = new Color(0.2f, 0.6f, 1f); // 标题色：蓝色
            }

            // 2. 单个Effect容器样式（修复边框颜色错误）
            if (effectItemContainerStyle == null)
            {
                effectItemContainerStyle = new GUIStyle();
                // 边框宽度：上下左右各1像素（控制边框粗细，0=无边框）
                effectItemContainerStyle.border = new RectOffset(1, 1, 1, 1);
                // 容器背景色：匹配Unity编辑器面板灰（0.196f灰，和其他面板一致）
                effectItemContainerStyle.normal.background = CreateSolidTexture(new Color(0.196f, 0.196f, 0.196f));
                // 内边距：内容与边框的间距（避免文字贴边）
                effectItemContainerStyle.padding = new RectOffset(10, 10, 10, 10);
            }

            // 3. Effect内容样式（绿色字体，醒目不刺眼）
            if (effectContentStyle == null)
            {
                effectContentStyle = new GUIStyle(EditorStyles.label);
                effectContentStyle.fontSize = 13; // 可修改：内容字体大小
                effectContentStyle.wordWrap = true; // 自动换行
                effectContentStyle.padding = new RectOffset(0, 0, 3, 0); // 优化行间距
                effectContentStyle.normal.textColor = new Color(0.8f, 0.7f, 0.3f);
            }
        }
        private GUIStyle GetSkillTextStyle(SkillColor color)
        {
            if (color == SkillColor.None)
            {
                return EditorStyles.textArea;
            }

            if (!skillColorTextStyles.TryGetValue(color, out GUIStyle style))
            {
                style = new GUIStyle(EditorStyles.textArea);
                Color resolvedColor = ResolveSkillColor(color);
                style.normal.textColor = resolvedColor;
                style.focused.textColor = resolvedColor;
                style.hover.textColor = resolvedColor;
                style.active.textColor = resolvedColor;
                skillColorTextStyles[color] = style;
            }

            return style;
        }

        private Color ResolveSkillColor(SkillColor color)
        {
            switch (color)
            {
                case SkillColor.DeepBlue:
                    return new Color(0.2f, 0.55f, 0.95f);
                case SkillColor.DarkYellow:
                    return new Color(0.86f, 0.67f, 0.2f);
                case SkillColor.Green:
                    return new Color(0.35f, 0.78f, 0.38f);
                case SkillColor.Purple:
                    return new Color(0.7f, 0.45f, 0.9f);
                case SkillColor.LightBlue:
                    return new Color(0.45f, 0.85f, 1f);
                case SkillColor.Red:
                    return new Color(0.94f, 0.37f, 0.37f);
                default:
                    return EditorStyles.textArea.normal.textColor;
            }
        }

        // 辅助：生成纯色纹理（用于容器背景）
        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        private static string FormatSkillDurationLabel(int duration)
        {
            if (duration > 0)
                return $"{duration} 回合";
            if (duration == -1)
                return "瞬间";
            if (duration == -2)
                return "永久";
            if (duration == 0)
                return "无持续";
            return duration.ToString();
        }

        // 原有：获取本地化描述
        private string GetLocalizedDesc(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "【描述Key为空】";
            if (localizationDict.TryGetValue(key, out string desc))
                return desc;
            return "【未找到描述：key=" + key + "】";
        }

        // 原有：提取技能ID中的数字
        private int ExtractNumberFromSkillID(string skillID)
        {
            if (string.IsNullOrEmpty(skillID)) return 0;
            string numberStr = System.Text.RegularExpressions.Regex.Replace(skillID, @"[^0-9]", "");
            return int.TryParse(numberStr, out int num) ? num : 0;
        }
    }
}