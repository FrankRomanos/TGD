// Assets/Scripts/TGD.Editor/SkillJsonBackupUtility.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{
    /// <summary>
    /// 导出/导入 SkillDefinition 为 JSON（备份/回滚）
    /// </summary>
    public static class SkillJsonBackupUtility
    {
        // ========== 菜单 ==========
        [MenuItem("Tools/TGD/Skills/Export Selected Skill(s) to JSON...")]
        public static void ExportSelectedSkills()
        {
            var skills = GetSelectedSkills();
            if (skills.Count == 0)
            {
                EditorUtility.DisplayDialog("Export Skills", "请在 Project 里选中一个或多个 SkillDefinition 资产。", "OK");
                return;
            }

            string folder = EditorUtility.OpenFolderPanel("选择导出目录", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder)) return;

            int ok = 0, fail = 0;
            foreach (var s in skills)
            {
                try
                {
                    var dto = SkillBackup.FromAsset(s);
                    string filename = $"Skill_{SanitizeFileName(dto.skillID)}.json";
                    string path = Path.Combine(folder, filename);
                    File.WriteAllText(path, JsonUtility.ToJson(dto, true));
                    ok++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"导出失败 {s.name}: {ex}");
                    fail++;
                }
            }

            EditorUtility.RevealInFinder(folder);
            EditorUtility.DisplayDialog("Export Skills",
                $"导出完成：成功 {ok} 个，失败 {fail} 个。\n保存到：{folder}", "OK");
        }

        [MenuItem("Tools/TGD/Skills/Export All Skills in Folder...")]
        public static void ExportAllInFolder()
        {
            string srcFolder = EditorUtility.OpenFolderPanel("选择技能所在目录", Application.dataPath, "");
            if (string.IsNullOrEmpty(srcFolder)) return;

            string projectRel = ToProjectRelativePath(srcFolder);
            if (string.IsNullOrEmpty(projectRel))
            {
                EditorUtility.DisplayDialog("Export Skills", "请选择 Assets/ 下的目录。", "OK");
                return;
            }

            string outFolder = EditorUtility.OpenFolderPanel("选择导出目录", srcFolder, "");
            if (string.IsNullOrEmpty(outFolder)) return;

            var guids = AssetDatabase.FindAssets("t:SkillDefinition", new[] { projectRel });
            int ok = 0, fail = 0;
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;

                try
                {
                    var dto = SkillBackup.FromAsset(asset);
                    string filename = $"Skill_{SanitizeFileName(dto.skillID)}.json";
                    string path = Path.Combine(outFolder, filename);
                    File.WriteAllText(path, JsonUtility.ToJson(dto, true));
                    ok++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"导出失败 {asset.name}: {ex}");
                    fail++;
                }
            }

            EditorUtility.RevealInFinder(outFolder);
            EditorUtility.DisplayDialog("Export All Skills",
                $"导出完成：成功 {ok} 个，失败 {fail} 个。\n保存到：{outFolder}", "OK");
        }

        [MenuItem("Tools/TGD/Skills/Import Skill JSON (Create or Overwrite)...")]
        public static void ImportSkillJson()
        {
            string jsonPath = EditorUtility.OpenFilePanel("选择 Skill JSON", Application.dataPath, "json");
            if (string.IsNullOrEmpty(jsonPath)) return;

            try
            {
                string json = File.ReadAllText(jsonPath);
                var dto = JsonUtility.FromJson<SkillBackup>(json);
                if (dto == null || string.IsNullOrEmpty(dto.skillID))
                {
                    EditorUtility.DisplayDialog("Import Skill", "JSON 无效或缺少 skillID。", "OK");
                    return;
                }

                // 目标 .asset 路径：优先选择 JSON 所在目录对应的 Assets/ 子路径；找不到则用默认
                string defaultDir = "Assets/GameData/Skills";
                string jsonDirProj = ToProjectRelativePath(Path.GetDirectoryName(jsonPath) ?? "");
                string targetDir = string.IsNullOrEmpty(jsonDirProj) ? defaultDir : jsonDirProj;
                if (!AssetDatabase.IsValidFolder(targetDir))
                {
                    Directory.CreateDirectory(targetDir.Replace("Assets/", Application.dataPath + "/"));
                    AssetDatabase.Refresh();
                }

                string assetPath = Path.Combine(targetDir, $"Skill_{SanitizeFileName(dto.skillID)}.asset").Replace("\\", "/");
                var asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(assetPath);
                bool isNew = false;
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SkillDefinition>();
                    AssetDatabase.CreateAsset(asset, assetPath);
                    isNew = true;
                }

                // 应用数据
                dto.ApplyToAsset(asset);

                // 重新计算派生字段
                asset.RecalculateCooldownSecondToTurn(6);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorGUIUtility.PingObject(asset);

                EditorUtility.DisplayDialog("Import Skill",
                    $"{(isNew ? "已创建" : "已覆盖")}技能：{asset.skillID}\n路径：{assetPath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("导入失败：" + ex);
                EditorUtility.DisplayDialog("Import Skill", "导入失败，详见 Console。", "OK");
            }
        }

        // ========== 辅助 ==========
        private static List<SkillDefinition> GetSelectedSkills()
        {
            var list = new List<SkillDefinition>();
            foreach (var o in Selection.objects)
            {
                if (o is SkillDefinition s) list.Add(s);
            }
            return list;
        }

        private static string ToProjectRelativePath(string absolute)
        {
            absolute = absolute.Replace("\\", "/");
            string data = Application.dataPath.Replace("\\", "/");
            if (!absolute.StartsWith(data)) return null;
            return "Assets" + absolute.Substring(data.Length);
        }

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "_");
            return s;
        }
    }

    /// <summary>
    /// 备份 DTO：与 SkillDefinition 对应，但把 Sprite 等 Unity 对象改成 GUID/path 记录，便于 JSON 存储。
    /// 注意：EffectDefinition/SkillCost/枚举等直接沿用（它们已 [Serializable]）。
    /// </summary>
    [Serializable]
    public class SkillBackup
    {
        public string skillID;
        public string skillName;
        public string iconGuid;      // 记录图标 GUID（可选回填）
        public string iconPath;      // 记录图标路径（辅助定位）

        public string classID;
        public string moduleID;
        public string variantKey;
        public string chainNextID;
        public bool resetOnTurnEnd;

        public SkillType skillType;
        public ActionType actionType;
        public SkillTargetType targetType;

        public List<SkillCost> costs = new();

        public int timeCostSeconds;
        public int cooldownSeconds;
        public int cooldownRounds;
        public int range;
        public float threat;
        public float shredMultiplier;
        public string namekey;
        public string descriptionKey;

        public SkillColor skillColor;
        public int skillLevel;
        public SkillDurationSettings skillDuration = new SkillDurationSettings();

        public List<EffectDefinition> effects = new();

        // 元信息（可选）
        public string backupTime;    // 备份时间
        public string unityVersion;  // Unity 版本
        public string notes;         // 备注（可手改）

        public static SkillBackup FromAsset(SkillDefinition s)
        {
            var dto = new SkillBackup
            {
                skillID = s.skillID,
                skillName = s.skillName,
                classID = s.classID,
                moduleID = s.moduleID,
                variantKey = s.variantKey,
                chainNextID = s.chainNextID,
                resetOnTurnEnd = s.resetOnTurnEnd,

                skillType = s.skillType,
                actionType = s.actionType,
                targetType = s.targetType,

                timeCostSeconds = s.timeCostSeconds,
                cooldownSeconds = s.cooldownSeconds,
                cooldownRounds = s.cooldownRounds,
                range = s.range,
                threat = s.threat,
                shredMultiplier = s.shredMultiplier,
                namekey = s.namekey,
                descriptionKey = s.descriptionKey,

                skillColor = s.skillColor,
                skillLevel = s.skillLevel,
                skillDuration = s.skillDuration != null ? new SkillDurationSettings(s.skillDuration) : new SkillDurationSettings(),

                backupTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                notes = ""
            };

            // costs
            if (s.costs != null) dto.costs = new List<SkillCost>(s.costs);

            // effects
            if (s.effects != null) dto.effects = new List<EffectDefinition>(s.effects);

            // icon
            if (s.icon != null)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s.icon, out string guid, out long _))
                {
                    dto.iconGuid = guid;
                    dto.iconPath = AssetDatabase.GUIDToAssetPath(guid);
                }
                else
                {
                    dto.iconGuid = "";
                    dto.iconPath = AssetDatabase.GetAssetPath(s.icon);
                }
            }
            else
            {
                dto.iconGuid = "";
                dto.iconPath = "";
            }

            return dto;
        }

        public void ApplyToAsset(SkillDefinition s)
        {
            s.skillID = skillID;
            s.skillName = skillName;

            // icon 还原（尽量用 GUID）
            Sprite icon = null;
            if (!string.IsNullOrEmpty(iconGuid))
            {
                string p = AssetDatabase.GUIDToAssetPath(iconGuid);
                if (!string.IsNullOrEmpty(p)) icon = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            if (icon == null && !string.IsNullOrEmpty(iconPath))
                icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            s.icon = icon;

            s.classID = classID;
            s.moduleID = moduleID;
            s.variantKey = variantKey;
            s.chainNextID = chainNextID;
            s.resetOnTurnEnd = resetOnTurnEnd;

            s.skillType = skillType;
            s.actionType = actionType;
            s.targetType = targetType;

            s.costs = costs != null ? new List<SkillCost>(costs) : new List<SkillCost>();

            s.timeCostSeconds = timeCostSeconds;
            s.cooldownSeconds = cooldownSeconds;
            s.cooldownRounds = cooldownRounds; // 会在 RecalculateDerived 覆盖
            s.range = range;
            s.threat = threat;
            s.shredMultiplier = shredMultiplier;
            s.namekey = namekey;
            s.descriptionKey = descriptionKey;

            s.skillColor = skillColor;
            s.skillLevel = skillLevel;
            s.skillDuration = skillDuration != null ? new SkillDurationSettings(skillDuration) : new SkillDurationSettings();

            s.effects = effects != null ? new List<EffectDefinition>(effects) : new List<EffectDefinition>();
        }
    }
}
