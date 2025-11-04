using UnityEngine;
using UnityEditor;
using System.IO;

public class BatchConvertToSpriteToSingle : EditorWindow
{
    private string folder = "Assets/Sprite/SkillIcon";
    private bool recursive = true;

    [MenuItem("Tools/Batch/Convert Folder Textures To Sprite (Single)")]
    static void Open()
    {
        GetWindow<BatchConvertToSpriteToSingle>("Batch Convert To Sprite (Single)");
    }

    void OnGUI()
    {
        GUILayout.Label("Batch Convert Textures To Sprite (Single)", EditorStyles.boldLabel);
        folder = EditorGUILayout.TextField("Folder", folder);
        recursive = EditorGUILayout.Toggle("Recursive", recursive);
        if (GUILayout.Button("Convert Now"))
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Error", "Folder not found: " + folder, "OK");
                return;
            }
            ConvertFolder(folder, recursive);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Done", "Conversion completed. Check Console for details.", "OK");
        }
    }

    static void ConvertFolder(string folderPath, bool recursive)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new string[] { folderPath });
        int count = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!recursive && Path.GetDirectoryName(path) != folderPath) continue;
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            bool changed = false;

            // Set to Sprite (2D and UI)
            if (ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            // Force Single sprite mode (not Multiple)
            if (ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            // common defaults for UI icons
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            // 默认像素单位，可按项目规范修改
            if (ti.spritePixelsPerUnit != 100) { ti.spritePixelsPerUnit = 100; changed = true; }

            // 可根据需要设置 Packing Tag（如使用 Sprite Atlas）
            // ti.spritePackingTag = "SkillIcons";

            if (changed)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"Converted/Updated to Sprite(Single): {path}");
                count++;
            }
        }
        Debug.Log($"Batch conversion finished. Converted/updated {count} assets in {folderPath}.");
    }
}
