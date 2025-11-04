// Unity 6+ 仅编辑器：把层名写回指定索引（不动物理矩阵）
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Text;

public static class TGD_LayerRescue
{
    // ★把右侧名字改成你项目真实的名字（索引必须对应你原来用的）
    static readonly (int index, string name)[] MAP = new (int, string)[]
    {
        (6,  "Layer"),    // ← 如果你原来叫 "HexBoard" 或 "Board"，把 "Layer" 改掉
        (7,  "Unit"),
        (10, "Obstacle"),
    };

    [MenuItem("TGD/Tools/Restore Layers (Frank Map)")]
    public static void Restore()
    {
        var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (objs == null || objs.Length == 0)
        {
            EditorUtility.DisplayDialog("TGD Layer Rescue", "找不到 ProjectSettings/TagManager.asset", "OK");
            return;
        }

        var so = new SerializedObject(objs[0]);
        var layersProp = so.FindProperty("layers");
        if (layersProp == null || !layersProp.isArray)
        {
            EditorUtility.DisplayDialog("TGD Layer Rescue", "TagManager.layers 未找到", "OK");
            return;
        }

        int changes = 0;
        foreach (var (i, name) in MAP)
        {
            if (i < 0 || i > 31) continue;
            var sp = layersProp.GetArrayElementAtIndex(i);
            if (sp == null) continue;

            string before = sp.stringValue;
            if (before != name)
            {
                sp.stringValue = name;
                changes++;
                Debug.Log($"Layer[{i}] \"{before}\" → \"{name}\"");
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("TGD Layer Rescue", $"完成：已写回 {changes} 个层名。", "OK");
    }

    [MenuItem("TGD/Tools/Audit Layers (Print)")]
    public static void Audit()
    {
        var sb = new StringBuilder("当前 Layer 索引 → 名称：\n");
        for (int i = 0; i < 32; i++)
            sb.AppendLine($"[{i}] {LayerMask.LayerToName(i)}");
        Debug.Log(sb.ToString());
    }

    // 如需最小化地校准几个碰撞关系，可用此函数（按需改名后再解开注释）:
    /*
    [MenuItem("TGD/Tools/Calibrate Minimal Collision Matrix")]
    public static void CalibrateCollisions()
    {
        // 例：Unit 与 Obstacle 发生碰撞
        int Unit = LayerMask.NameToLayer("Unit");
        int Obstacle = LayerMask.NameToLayer("Obstacle");
        if (Unit >= 0 && Obstacle >= 0) Physics.IgnoreLayerCollision(Unit, Obstacle, false);

        // 例：Unit 与 HexBoard（或 Layer）不发生物理碰撞（仅射线用）
        int Board = LayerMask.NameToLayer("Layer"); // 若你叫 "HexBoard"，把名字改掉
        if (Unit >= 0 && Board >= 0) Physics.IgnoreLayerCollision(Unit, Board, true);

        Debug.Log("已按最小集校准 Layer 碰撞矩阵（可按需继续补充对）。");
    }
    */
}
#endif
