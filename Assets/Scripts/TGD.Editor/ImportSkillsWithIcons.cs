using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.Editor
{

    public class ImportSkillsWithIcons : EditorWindow
    {
        private static bool bindIcons = true;
        private static bool verboseLog = true;
        private static string spriteFolder = "Assets/Icons"; // 你原来的图标路径
        private static Dictionary<string, Sprite> spritePaths = new Dictionary<string, Sprite>();
        private static Dictionary<string, string> normalizedToPaths = new Dictionary<string, string>();

        [MenuItem("Tools/Import Skills CSV")]
        public static void ImportSkills()
        {
            string path = EditorUtility.OpenFilePanel("Select Skills CSV", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(path)) return;

            string outputFolder = "Assets/GameData/Skills";
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            if (lines.Count < 2)
            {
                Debug.LogError("CSV 文件为空或缺少数据行！");
                return;
            }

            var headers = lines[0].Split(',');
            var indexOf = headers.Select((h, i) => new { h, i }).ToDictionary(x => x.h.Trim(), x => x.i);

            for (int r = 1; r < lines.Count; r++)
            {
                var cols = ParseCSVLine(lines[r]);
                if (cols.Count == 0) continue;
                string skillID = Get(cols, indexOf, "skillID");
                if (string.IsNullOrEmpty(skillID)) continue;

                string assetPath = $"{outputFolder}/Skill_{skillID}.asset";
                SkillDefinition asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SkillDefinition>();
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                // 基础字段
                asset.skillID = skillID;
                asset.skillName = Get(cols, indexOf, "skillName");
                asset.moduleID = Get(cols, indexOf, "moduleID");
                asset.variantKey = Get(cols, indexOf, "variantKey");
                asset.chainNextID = Get(cols, indexOf, "chainNextID");
                asset.resetOnTurnEnd = Get(cols, indexOf, "resetOnTurnEnd").ToLower() == "true";
                asset.classID = Get(cols, indexOf, "classID");

                TryParseEnumField(asset, "actionType", Get(cols, indexOf, "actionType"));
                TryParseEnumField(asset, "targetType", Get(cols, indexOf, "targetType"));
                asset.costs = new List<SkillCost>();

                int energy = ParseIntSafe(Get(cols, indexOf, "cost"));
                if (energy > 0)
                {
                    asset.costs.Add(new SkillCost { resourceType = CostResourceType.Energy, amount = energy });
                }

                asset.timeCostSeconds = ParseIntSafe(Get(cols, indexOf, "timeCostSeconds"));
                asset.cooldownSeconds = ParseIntSafe(Get(cols, indexOf, "cooldownSeconds"));
                asset.range = ParseIntSafe(Get(cols, indexOf, "range"));
                asset.threat = ParseIntSafe(Get(cols, indexOf, "threat"));
                asset.shredMultiplier = ParseFloatSafe(Get(cols, indexOf, "shredMultiplier"));
                asset.namekey = Get(cols, indexOf, "namekey");
                asset.descriptionKey = Get(cols, indexOf, "descriptionKey");

                // 不再使用 effectsRaw，保持 effects 为空（后续可用 Editor 手动配置）
                asset.effects = new List<EffectDefinition>();

                // 自动绑定图标
                if (bindIcons)
                {
                    Sprite found = FindIconByIDOnly(asset.skillID, spriteFolder, spritePaths, normalizedToPaths);
                    if (found != null)
                    {
                        SetIconField(asset, found);
                        if (verboseLog) Debug.Log($"Icon bound: {asset.skillID} -> {found.name}");
                    }
                    else
                    {
                        if (verboseLog) Debug.Log($"Icon NOT found for: {asset.skillID}");
                    }
                }

                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Import Complete", "Skills imported successfully!", "OK");
        }

        // ======================== 导出功能 ========================
        [MenuItem("Tools/Export Skills to CSV")]
        public static void ExportSkillsToCSV()
        {
            string outputPath = EditorUtility.SaveFilePanel("Export Skills CSV", Application.dataPath, "skills", "csv");
            if (string.IsNullOrEmpty(outputPath)) return;

            var skills = AssetDatabase.FindAssets("t:SkillDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .OrderBy(s => s.skillID)
                .ToList();
            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                // 不再包含 effectsRaw
                writer.WriteLine("skillID,skillName,moduleID,variantKey,chainNextID,resetOnTurnEnd,classID,actionType,targetType,energyCost,timeCostSeconds,cooldownSeconds,range,threat,shredMultiplier,namekey,descriptionKey");

                foreach (var s in skills)
                {
                    // 找 Energy 消耗（如果没有则 0）
                    int energy = s.costs.FirstOrDefault(c => c.resourceType == CostResourceType.Energy)?.amount ?? 0;

                    string line = string.Join(",",
                        s.skillID,
                        s.skillName,
                        s.moduleID,
                        s.variantKey,
                        s.chainNextID,
                        s.resetOnTurnEnd ? "true" : "false",
                        s.classID,
                        s.actionType.ToString(),
                        s.targetType.ToString(),
                        energy,
                        s.timeCostSeconds,
                        s.cooldownSeconds,
                        s.range,
                        s.threat,
                        s.shredMultiplier,
                        s.namekey,
                        s.descriptionKey
                    );

                    writer.WriteLine(line);
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Export Complete", $"Exported {skills.Count} skills to {outputPath}", "OK");
        }

        // ======================== 工具方法 ========================
        private static string Get(List<string> cols, Dictionary<string, int> indexOf, string key)
        {
            if (!indexOf.ContainsKey(key)) return "";
            int i = indexOf[key];
            return (i >= 0 && i < cols.Count) ? cols[i] : "";
        }

        private static List<string> ParseCSVLine(string line)
        {
            // 简单 CSV 行解析（按逗号分割）
            return line.Split(',').Select(s => s.Trim()).ToList();
        }

        private static void TryParseEnumField(SkillDefinition asset, string fieldName, string value)
        {
            var field = asset.GetType().GetField(fieldName);
            if (field != null && !string.IsNullOrEmpty(value))
            {
                try
                {
                    var enumValue = Enum.Parse(field.FieldType, value);
                    field.SetValue(asset, enumValue);
                }
                catch { }
            }
        }

        private static int ParseIntSafe(string s)
        {
            return int.TryParse(s, out int v) ? v : 0;
        }

        private static float ParseFloatSafe(string s)
        {
            return float.TryParse(s, out float v) ? v : 0f;
        }

        private static Sprite FindIconByIDOnly(string skillID, string folder, Dictionary<string, Sprite> cache, Dictionary<string, string> norm)
        {
            if (cache.ContainsKey(skillID)) return cache[skillID];

            var assets = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
            foreach (var guid in assets)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null && sprite.name.Equals(skillID, StringComparison.OrdinalIgnoreCase))
                {
                    cache[skillID] = sprite;
                    return sprite;
                }
            }
            return null;
        }

        private static void SetIconField(SkillDefinition asset, Sprite sprite)
        {
            var field = asset.GetType().GetField("icon");
            if (field != null)
            {
                field.SetValue(asset, sprite);
            }
        }
    }
}