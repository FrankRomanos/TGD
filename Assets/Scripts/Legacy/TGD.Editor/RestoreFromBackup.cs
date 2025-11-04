using UnityEditor;
using UnityEngine;
using System.IO;

public class RestoreSkillIconBackup
{
    [MenuItem("Tools/Skill/Restore SkillIcon From Backup")]
    static void Restore()
    {
        string backup = "Assets/Sprite/SkillIcon_Backup";
        string target = "Assets/Sprite/SkillIcon";
        if (!Directory.Exists(backup))
        {
            Debug.LogError("Backup folder not found: " + backup);
            return;
        }
        var files = Directory.GetFiles(backup);
        int c = 0;
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var dest = Path.Combine(target, name);
            File.Copy(f, dest, true);
            c++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"Restored {c} files from backup to {target}");
    }
}

