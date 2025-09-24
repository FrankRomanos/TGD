#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TGD.Level;

[CustomEditor(typeof(TGD.Level.HexTileSpawner))]
public class HexTileSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var sp = (TGD.Level.HexTileSpawner)target;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate (Edit Mode)", GUILayout.Height(26)))
        {
            sp.GenerateNow();
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(sp.gameObject.scene);
        }
        if (GUILayout.Button("Clear", GUILayout.Height(26)))
        {
            sp.ClearNow();
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(sp.gameObject.scene);
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
