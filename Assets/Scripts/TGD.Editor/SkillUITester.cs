using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using TGD.Data;
using TGD.Editor;

namespace TGD.UI
{
    public class SkillUITester_Simple : EditorWindow
    {
        // 原有：本地化与技能数据变量
        private Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        private string localizationFilePath = "Assets/Localization/Localization_Skills.csv";
        private List<SkillDefinition> allSkillDatas = new List<SkillDefinition>();
        private SkillDefinition selectedSkill;
        private Vector2 scrollPos;
        private Vector2 effectScrollPos;
        private GUIStyle selectedMiniButton;

        // 新增：Effect样式变量
        private GUIStyle effectTitleStyle;       // Effect标题样式（如"Effect 1"）
        private GUIStyle effectItemContainerStyle;// 单个Effect容器样式（背景、边框宽度）
        private GUIStyle effectContentStyle;      // Effect内容文本样式（字体大小、颜色）

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
            string skillDataPath = "SkillData";
            SkillDefinition[] loadedSkills = Resources.LoadAll<SkillDefinition>(skillDataPath);
            allSkillDatas.Clear();
            allSkillDatas.AddRange(loadedSkills);
            allSkillDatas.Sort((a, b) =>
            {
                if (int.TryParse(a.skillID, out int idA) && int.TryParse(b.skillID, out int idB))
                    return idA.CompareTo(idB);
                int numA = ExtractNumberFromSkillID(a.skillID);
                int numB = ExtractNumberFromSkillID(b.skillID);
                return numA.CompareTo(numB);
            });
            if (allSkillDatas.Count > 0) selectedSkill = allSkillDatas[0];
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
                if (GUILayout.Button(
                    $"ID:{skill.skillID} | {skill.skillName}",
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
                if (selectedSkill.icon != null)
                {
                    GUILayout.Label(new GUIContent(selectedSkill.icon.texture), GUILayout.Width(80), GUILayout.Height(80));
                }
                else
                {
                    GUILayout.Label("无图标", EditorStyles.helpBox, GUILayout.Width(100), GUILayout.Height(100));
                }

                // 原有：技能名称/描述/属性
                GUILayout.Label("技能名称：");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.namekey), GUILayout.Height(40));
                GUILayout.Label("技能描述：");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.descriptionKey), GUILayout.Height(50));
                GUILayout.Label("基础属性：", EditorStyles.miniBoldLabel);
                GUILayout.Label($"职业：{selectedSkill.classID}");
                GUILayout.Label($"动作类型：{selectedSkill.actionType}");
                GUILayout.Label($"冷却时间：{selectedSkill.cooldownSeconds}秒 / {selectedSkill.cooldownRounds}回合");
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
                            string summary = EffectSummaryUtility.BuildSummary(effectProp, selectedSkill);
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

        // 辅助：生成纯色纹理（用于容器背景）
        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
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