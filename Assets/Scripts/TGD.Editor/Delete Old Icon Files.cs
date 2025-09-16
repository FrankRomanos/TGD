// Assets/Editor/DeleteOldIconFiles.cs
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class DeleteOldIconFiles
{
    // ���ã�
    private static string spriteFolder = "Assets/Sprite/SkillIcon";
    // ʶ��Ϊ�������������������� Skill_<Class>_SK123 �� Skill_SK123��
    private static Regex newNameRx = new Regex(@"^(Skill_[A-Za-z0-9_]+_SK\d+|Skill_SK\d+)\.(png|jpg|jpeg|psd|tga)$", RegexOptions.IgnoreCase);

    [MenuItem("Tools/Skill/Delete Old Icon Files")]
    static void DeleteOldIcons()
    {
        if (!AssetDatabase.IsValidFolder(spriteFolder))
        {
            EditorUtility.DisplayDialog("Folder not found", $"Sprite folder not found: {spriteFolder}", "OK");
            return;
        }

        var files = Directory.GetFiles(spriteFolder)
            .Where(p => {
                var ext = Path.GetExtension(p).ToLower();
                if (!(ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".psd" || ext == ".tga")) return false;
                var name = Path.GetFileName(p);
                return !newNameRx.IsMatch(name); // ����������Ϊ���ļ�
            }).ToArray();

        if (files.Length == 0)
        {
            EditorUtility.DisplayDialog("Nothing to delete", "No old icon files found to delete.", "OK");
            return;
        }

        // �г�������ȷ��
        string sample = string.Join("\n", files.Take(50).Select(Path.GetFileName));
        bool ok = EditorUtility.DisplayDialog("Confirm Delete",
            $"Found {files.Length} old icon files (non-new-naming). Sample:\n{sample}\n\nAre you sure you want to DELETE them? (This is permanent unless you have backup.)",
            "Delete", "Cancel");
        if (!ok) return;

        int deleted = 0;
        foreach (var f in files)
        {
            try
            {
                File.Delete(f);
                // ɾ�� meta �ļ����������ࣩ
                var meta = f + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                deleted++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to delete {f}: {ex.Message}");
            }
        }
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", $"Deleted {deleted} files from {spriteFolder}", "OK");
        Debug.Log($"DeleteOldIconFiles: deleted {deleted} files from {spriteFolder}");
    }
}
