// File: Assets/Scripts/TGD.Tools/Editor/TGDCleanMissing.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TGDCleanMissing
{
    // 顶部菜单：Tools/TGD/Clean Missing...
    [MenuItem("Tools/TGD/Clean Missing Scripts In Selection")]
    // 备用菜单：TGD Tools/Clean Missing...
    [MenuItem("TGD Tools/Clean Missing Scripts In Selection", false, 0)]
    public static void CleanSelection()
    {
        var gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.Log("No GameObject selected.");
            return;
        }

        int total = 0;
        foreach (var go in gos)
            total += CleanOne(go);

        Debug.Log($"Removed {total} missing script component(s) under {gos.Length} object(s).");
    }

    // 层级视图右键：GameObject/TGD/Clean Missing...
    [MenuItem("GameObject/TGD/Clean Missing Scripts In Children", false, 49)]
    private static void CleanFromHierarchy(MenuCommand cmd)
    {
        var go = cmd.context as GameObject;
        if (!go) return;
        int cnt = CleanOne(go);
        Debug.Log($"[{go.name}] removed {cnt} missing script component(s).");
    }

    // 在任意 Transform 组件的右键上下文菜单
    [MenuItem("CONTEXT/Transform/Clean Missing Scripts In Children")]
    private static void CleanFromContext(MenuCommand cmd)
    {
        var t = cmd.context as Transform;
        if (!t) return;
        int cnt = CleanOne(t.gameObject);
        Debug.Log($"[{t.gameObject.name}] removed {cnt} missing script component(s).");
    }

    // 实际清理：包含所有子物体
    private static int CleanOne(GameObject root)
    {
        int removed = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        return removed;
    }
}
#endif
