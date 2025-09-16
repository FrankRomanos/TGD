using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class RemoveJobPrefixFromSkillFileName
{
    [MenuItem("Tools/Skill/Remove Job Prefix From Skill Files")]
    public static void Run()
    {
        string folderPath = "Assets/Sprite/SkillIcon";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"文件夹不存在：{folderPath}");
            return;
        }
        var files = Directory.GetFiles(folderPath)
                     .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".psd") || f.EndsWith(".tga"))
                     .ToArray();

        int changedCount = 0;
        foreach (var fullPath in files)
        {
            string filename = Path.GetFileNameWithoutExtension(fullPath);
            // 匹配形如 Skill_*_SKxxx | Skill_*_SKxxx
            var match = Regex.Match(filename, @"^(Skill)_.*_SK\d+$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string newName = match.Groups[1].Value + "_SK" + filename.Split('_').Last().Substring(2);
                string newPath = Path.Combine(Path.GetDirectoryName(fullPath), newName + Path.GetExtension(fullPath));
                if (File.Exists(newPath))
                {
                    Debug.LogWarning($"目标文件已存在：{newName}");
                    continue;
                }
                // 备份
                string backupDir = "Assets/Sprite/SkillIcon_Backup";
                if (!AssetDatabase.IsValidFolder(backupDir))
                    Directory.CreateDirectory(backupDir);
                string backupPath = Path.Combine(backupDir, Path.GetFileName(fullPath));
                if (!File.Exists(backupPath))
                    File.Copy(fullPath, backupPath);
                // 重命名
                File.Move(fullPath, newPath);
                Debug.Log($"重命名：{fullPath} -> {newPath}");
                changedCount++;
            }
        }
        AssetDatabase.Refresh();
        Debug.Log($"完成：共修改 {changedCount} 个文件名");
    }
}
