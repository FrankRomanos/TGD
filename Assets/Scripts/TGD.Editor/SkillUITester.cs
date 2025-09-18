using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TGD.Data;
using TGD.Editor;

// ====== 新增 using（只为读取 Localization 表，不改变 UI 逻辑）======
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
#if UNITY_EDITOR
using UnityEditor.Localization;

#endif
// ================================================================

namespace TGD.UI
{
    public class SkillUITester_Simple : EditorWindow
    {
        // 原有：本地化与技能数据变量（CSV -> Localization）
        // ✅ 移除 CSV 字典与路径；改为 Localization 缓存
        // private readonly Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        // private string localizationFilePath = "Assets/Localization/Localization_Skills.csv";

#if UNITY_EDITOR
        // ✅ 你的 Localization 字符串表集合名（已按你要求设置为 SkillDiscription）
        private const string StringTableCollectionName = "SkillDiscription";

        // 缓存：集合、当前语言表、项目语言
        private StringTableCollection stringTableCollection;
        private StringTable currentStringTable;
        private Locale projectLocale;
#endif
#if UNITY_EDITOR
        // —— 调试工具（默认收起）——
        private bool _showLocDebugFoldout = false;
        private List<Locale> _debugLocales = new List<Locale>();
        private int _debugLocaleIndex = -1;  // 调试选择的 Locale 索引（仅影响本窗口）
        private string _debugKey = "";       // Key 快速定位输入
        private string _debugKeyPreview = ""; // 读取到的值预览
#endif
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

            // ✅ Localization 初始化（只读，不改变 UI）
#if UNITY_EDITOR
            InitializeLocalizationReadonly();
#if UNITY_EDITOR
            // ……你原有的 InitializeLocalizationReadonly() 里最后面加上：
            var all = LocalizationEditorSettings.GetLocales();
            _debugLocales.Clear();
            if (all != null) _debugLocales.AddRange(all);

            // 调试用默认选中：跟随 projectLocale
            _debugLocaleIndex = (_debugLocales.Count > 0 && projectLocale != null)
                ? _debugLocales.IndexOf(projectLocale)
                : (_debugLocales.Count > 0 ? 0 : -1);
#endif

#endif

            // 原有：加载技能数据
            LoadSkillDefinitions();
        }

#if UNITY_EDITOR
        // 只读初始化：拿到集合、项目 Locale，并解析当前语言表
        private void InitializeLocalizationReadonly()
        {
            try
            {
                // 取集合
                stringTableCollection = LocalizationEditorSettings.GetStringTableCollection(StringTableCollectionName);
                if (stringTableCollection == null)
                {
                    Debug.LogError($"[Localization] 未找到 StringTableCollection：{StringTableCollectionName}。请在 Window > Asset Management > Localization Tables 中确认集合存在。");
                    currentStringTable = null;
                    return;
                }

                // 取项目 Locale（编辑器环境）
                projectLocale = LocalizationSettings.ProjectLocale;
                if (projectLocale == null)
                {
                    // 如果未设置 Project Locale，也不报错，改为提示并尝试用第一个 Locale
                    var locales = LocalizationEditorSettings.GetLocales();
                    if (locales != null && locales.Count > 0)
                        projectLocale = locales[0];
                    if (projectLocale == null)
                        Debug.LogWarning("[Localization] Project Locale 未设置，且项目中没有可用 Locale。将无法从表中读取文本。");
                }

                ResolveCurrentStringTable();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Localization] 初始化失败：" + ex.Message);
                currentStringTable = null;
            }
        }

        private void ResolveCurrentStringTable()
        {
            currentStringTable = null;
            if (stringTableCollection == null || projectLocale == null)
                return;

            // 根据集合与项目语言，取该语言的 StringTable
            currentStringTable = stringTableCollection.GetTable(projectLocale.Identifier) as StringTable;

            if (currentStringTable == null)
            {
                // 不创建，不改资源，只提示
                Debug.LogWarning($"[Localization] 集合 \"{StringTableCollectionName}\" 下未找到 Locale \"{projectLocale.Identifier.Code}\" 对应的 StringTable。读取将返回占位提示。");
            }
        }
#endif

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

        // ====== 保留原签名：LoadLocalizationData（现在不再读 CSV，仅为兼容留空）======
        private void LoadLocalizationData()
        {
            // 已改为通过 Unity Localization 读取，不再需要预读 CSV。
            // 保留空实现以兼容原调用点（不改变 UI 逻辑）。
        }
        // ====================================================================


        private void OnGUI()
        {
            // 初始化Effect相关样式（修复边框颜色错误）
            InitEffectStyles();
#if UNITY_EDITOR
            DrawLocalizationDebugToolbar();
#endif
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

                // 原有：技能名称/描述/属性 —— 仅“取值来源”改为 Localization
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

        // ✅ 改为读取 Localization 表（仅读，不创建、不写入）
        private string GetLocalizedDesc(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "【描述Key为空】";

#if UNITY_EDITOR
            if (stringTableCollection == null)
                return $"【未找到集合：{StringTableCollectionName}】";

            if (currentStringTable == null)
                return $"【未找到当前语言表：{(LocalizationSettings.ProjectLocale != null ? LocalizationSettings.ProjectLocale.Identifier.Code : "未设置")}】";

            try
            {
                var entry = currentStringTable.GetEntry(key);
                if (entry != null && entry.Value != null)
                    return entry.Value;

                return $"【未找到描述：key={key}】";
            }
            catch (Exception ex)
            {
                return $"【读取本地化失败：{ex.Message}】";
            }
#else
            // 运行时一般不会开这个 EditorWindow，这里返回 key 以免报错
            return key;
#endif
        }
#if UNITY_EDITOR
        private void DrawLocalizationDebugToolbar()
        {
            // 折叠栏标题（完全独立，不影响你现有 UI）
            _showLocDebugFoldout = EditorGUILayout.Foldout(_showLocDebugFoldout, "调试工具（Localization）", true);
            if (!_showLocDebugFoldout) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // —— 语言快速切换（只刷新本窗口读取的表，不改工程 Project Settings）——
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("阅读语言（本窗口）", GUILayout.Width(140));
                    string[] options = _debugLocales.ConvertAll(l => l != null ? l.Identifier.Code : "(null)").ToArray();
                    int newIndex = EditorGUILayout.Popup(_debugLocaleIndex, options);
                    if (newIndex != _debugLocaleIndex && newIndex >= 0 && newIndex < _debugLocales.Count)
                    {
                        _debugLocaleIndex = newIndex;
                        var picked = _debugLocales[_debugLocaleIndex];
                        if (picked != null)
                        {
                            // 仅更新本窗口使用的 projectLocale 引用，并重新解析表
                            projectLocale = picked;
                            ResolveCurrentStringTable();
                            // 清理一次预览缓存
                            _debugKeyPreview = string.Empty;
                            Repaint();
                        }
                    }

                    if (GUILayout.Button("刷新表", GUILayout.Width(90)))
                    {
                        ResolveCurrentStringTable();
                        _debugKeyPreview = string.Empty;
                    }
                }

                EditorGUILayout.Space(6);

                // —— Key 快速定位 —— 
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Key", GUILayout.Width(140));
                    _debugKey = EditorGUILayout.TextField(_debugKey);

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_debugKey)))
                    {
                        if (GUILayout.Button("读取值", GUILayout.Width(90)))
                        {
                            _debugKeyPreview = GetLocalizedDesc(_debugKey);
                        }

                        if (GUILayout.Button("定位技能", GUILayout.Width(90)))
                        {
                            // 在当前技能数据里查找 namekey/descriptionKey 命中该 key 的技能并选中第一个
                            SkillDefinition found = null;
                            foreach (var s in allSkillDatas)
                            {
                                if (s == null) continue;
                                if (string.Equals(s.namekey, _debugKey, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(s.descriptionKey, _debugKey, StringComparison.OrdinalIgnoreCase))
                                {
                                    found = s; break;
                                }
                            }
                            if (found != null)
                            {
                                selectedSkill = found;
                                // 让左侧列表滚动条有机会回到顶部（不强求）
                                scrollPos = Vector2.zero;
                            }
                            else
                            {
                                Debug.Log($"[定位技能] 未找到使用 Key \"{_debugKey}\" 的技能（会在 namekey/descriptionKey 中匹配）。");
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_debugKeyPreview))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("当前语言读取结果：", EditorStyles.miniBoldLabel);
                    EditorGUILayout.HelpBox(_debugKeyPreview, MessageType.None);
                }
            }
        }
#endif


        // 原有：提取技能ID中的数字
        private int ExtractNumberFromSkillID(string skillID)
        {
            if (string.IsNullOrEmpty(skillID)) return 0;
            string numberStr = System.Text.RegularExpressions.Regex.Replace(skillID, @"[^0-9]", "");
            return int.TryParse(numberStr, out int num) ? num : 0;
        }
    }
}
