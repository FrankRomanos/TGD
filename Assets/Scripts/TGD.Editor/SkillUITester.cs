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
        // 新增：本地化字典（key → 实际描述文本）
        private Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        // 新增：本地化文件路径（必须改对！）
        private string localizationFilePath = "Assets/Localization/Localization_Skills.csv";
        private List<SkillDefinition> allSkillDatas = new List<SkillDefinition>();
        private SkillDefinition selectedSkill;
        private Vector2 scrollPos;
        private Vector2 effectScrollPos;
        private GUIStyle selectedMiniButton;

        [MenuItem("Tools/Skill/简化版UI测试窗口")]
        public static void OpenTestWindow()
        {
            GetWindow<SkillUITester_Simple>("技能UI测试（简化版）").minSize = new Vector2(500, 350);
        }

        private void OnEnable()
        {
            // 初始化自定义选中样式（选中时背景变浅灰，和未选中区分）
            selectedMiniButton = new GUIStyle(EditorStyles.miniButton);
            selectedMiniButton.normal.background = EditorStyles.toolbarButton.active.background;

            LoadLocalizationData();

            // 加载SkillData（替换成你的实际路径，如"SkillDatas"）
            string skillDataPath = "SkillData";
            SkillDefinition[] loadedSkills = Resources.LoadAll<SkillDefinition>(skillDataPath);
            allSkillDatas.Clear();
            allSkillDatas.AddRange(loadedSkills);
            allSkillDatas.Sort((a, b) =>
            {
                // 尝试将 skillID 转成数字比较
                if (int.TryParse(a.skillID, out int idA) && int.TryParse(b.skillID, out int idB))
                {
                    return idA.CompareTo(idB); // 数字升序（1→2→3）
                }
                // 情况2：skillID 带前缀（如 SK1、SK2、SK10），先提取数字再比较
                else
                {
                    int numA = ExtractNumberFromSkillID(a.skillID);
                    int numB = ExtractNumberFromSkillID(b.skillID);
                    return numA.CompareTo(numB); // 按提取的数字升序
                }
            });
            if (allSkillDatas.Count > 0) selectedSkill = allSkillDatas[0];
        }
        // 新增：加载localization_skill.csv到字典
        private void LoadLocalizationData()
        {
            localizationDict.Clear(); // 清空旧数据

            // 检查文件是否存在
            if (!File.Exists(localizationFilePath))
            {
                Debug.LogError("本地化文件找不到！路径：" + localizationFilePath);
                return;
            }

            // 读取CSV所有行
            string[] allLines = File.ReadAllLines(localizationFilePath);
            if (allLines.Length < 2) // 至少需要1行表头+1行数据
            {
                Debug.LogError("本地化文件内容为空或格式错误！");
                return;
            }

            // 解析每行数据（跳过表头行，从第2行开始）
            for (int i = 1; i < allLines.Length; i++)
            {
                string line = allLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 分割CSV行（简单处理：按逗号分割，适合无逗号的文本）
                string[] parts = line.Split(',');
                if (parts.Length >= 2) // 确保至少有key和描述两列
                {
                    string key = parts[0].Trim(); // 第1列是key
                    string desc = parts[1].Trim(); // 第2列是中文描述（根据你的CSV调整列索引）
                    if (!localizationDict.ContainsKey(key))
                    {
                        localizationDict.Add(key, desc); // 存入字典
                    }
                }
            }

            Debug.Log("本地化数据加载完成，共" + localizationDict.Count + "条");
        }

        private void OnGUI()
        {
            GUILayout.Label("技能UI测试（图标+基础信息）", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 左右分栏：左侧选技能，右侧看详情
            GUILayout.BeginHorizontal();

            // 左侧：技能列表（修复样式报错）
            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label("技能列表", EditorStyles.miniBoldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(400));

            foreach (var skill in allSkillDatas)
            {
                // 关键修复：用自定义的selectedMiniButton替代miniButtonSelected
                if (GUILayout.Button(
                    $"ID:{skill.skillID} | {skill.skillName}",
                    selectedSkill == skill ? selectedMiniButton : EditorStyles.miniButton))
                {
                    selectedSkill = skill;
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(40);

            // 右侧：技能详情（无修改，保持原样）
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Label("技能详情", EditorStyles.miniBoldLabel);
            GUILayout.Space(5);

            if (selectedSkill != null)
            {
                // 显示图标
                GUILayout.Label("技能图标：", EditorStyles.miniLabel);
                if (selectedSkill.icon != null)
                {
                    // 将Sprite转成Texture2D，用于Label显示
                    Texture2D iconTexture = selectedSkill.icon.texture;
                    // 用Label绘制图标，固定宽高，无多余按钮
                    GUILayout.Label(
                        new GUIContent(iconTexture),
                        GUILayout.Width(80),  // 图标宽度（和之前一致）
                        GUILayout.Height(80)  // 图标高度（和之前一致）
                    );
                }
                else
                {
                    // 图标为空时，显示“无图标”提示（避免空区域）
                    GUILayout.Label(
                        "无图标",
                        EditorStyles.helpBox,
                        GUILayout.Width(100),
                        GUILayout.Height(100)
                    );
                }

                // 显示名称、描述、属性
                GUILayout.Label("技能名称：");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.namekey),
                GUILayout.Height(40));



                GUILayout.Label("技能描述：");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.descriptionKey),
                GUILayout.Height(50));

                GUILayout.Label("基础属性：", EditorStyles.miniLabel);
                GUILayout.Label($"职业：{selectedSkill.classID}");
                GUILayout.Label($"动作类型：{selectedSkill.actionType}");
                GUILayout.Label($"冷却时间：{selectedSkill.cooldownSeconds}秒");
                GUILayout.Label($"冷却时间：{selectedSkill.cooldownRounds}回合");
                GUILayout.Space(10);
                GUILayout.Label("Effect Preview", EditorStyles.miniBoldLabel);

                using (var serializedSkill = new SerializedObject(selectedSkill))
                {
                    serializedSkill.Update();
                    SerializedProperty effectsProp = serializedSkill.FindProperty("effects");
                    if (effectsProp != null && effectsProp.isArray && effectsProp.arraySize > 0)
                    {
                        effectScrollPos = EditorGUILayout.BeginScrollView(effectScrollPos, GUILayout.Height(160));
                        for (int i = 0; i < effectsProp.arraySize; i++)
                        {
                            SerializedProperty effectProp = effectsProp.GetArrayElementAtIndex(i);
                            string summary = EffectSummaryUtility.BuildSummary(effectProp, selectedSkill);
                            if (string.IsNullOrWhiteSpace(summary))
                                continue;

                            EditorGUILayout.LabelField($"Effect {i + 1}", EditorStyles.boldLabel);
                            EditorGUILayout.HelpBox(summary, MessageType.None);
                            GUILayout.Space(4);
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("No effects configured for this skill.", MessageType.Info);
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
        private string GetLocalizedDesc(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "【描述Key为空】";

            // 从字典中查找，找到返回描述，找不到返回提示
            if (localizationDict.TryGetValue(key, out string desc))
                return desc;
            else
                return "【未找到描述：key=" + key + "】";
        }
        // 辅助方法：从 skillID 中提取数字（支持 SK1、Skill2、ID3 等格式）
        private int ExtractNumberFromSkillID(string skillID)
        {
            if (string.IsNullOrEmpty(skillID)) return 0;
            // 用正则表达式提取所有数字字符，再转成int
            string numberStr = System.Text.RegularExpressions.Regex.Replace(skillID, @"[^0-9]", "");
            return int.TryParse(numberStr, out int num) ? num : 0;
        }
    }
}