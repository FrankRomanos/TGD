// Unity 6+ ���༭�����Ѳ���д��ָ�������������������
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Text;

public static class TGD_LayerRescue
{
    // ����Ҳ����ָĳ�����Ŀ��ʵ�����֣����������Ӧ��ԭ���õģ�
    static readonly (int index, string name)[] MAP = new (int, string)[]
    {
        (6,  "Layer"),    // �� �����ԭ���� "HexBoard" �� "Board"���� "Layer" �ĵ�
        (7,  "Unit"),
        (10, "Obstacle"),
    };

    [MenuItem("TGD/Tools/Restore Layers (Frank Map)")]
    public static void Restore()
    {
        var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (objs == null || objs.Length == 0)
        {
            EditorUtility.DisplayDialog("TGD Layer Rescue", "�Ҳ��� ProjectSettings/TagManager.asset", "OK");
            return;
        }

        var so = new SerializedObject(objs[0]);
        var layersProp = so.FindProperty("layers");
        if (layersProp == null || !layersProp.isArray)
        {
            EditorUtility.DisplayDialog("TGD Layer Rescue", "TagManager.layers δ�ҵ�", "OK");
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
                Debug.Log($"Layer[{i}] \"{before}\" �� \"{name}\"");
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("TGD Layer Rescue", $"��ɣ���д�� {changes} ��������", "OK");
    }

    [MenuItem("TGD/Tools/Audit Layers (Print)")]
    public static void Audit()
    {
        var sb = new StringBuilder("��ǰ Layer ���� �� ���ƣ�\n");
        for (int i = 0; i < 32; i++)
            sb.AppendLine($"[{i}] {LayerMask.LayerToName(i)}");
        Debug.Log(sb.ToString());
    }

    // ������С����У׼������ײ��ϵ�����ô˺���������������ٽ⿪ע�ͣ�:
    /*
    [MenuItem("TGD/Tools/Calibrate Minimal Collision Matrix")]
    public static void CalibrateCollisions()
    {
        // ����Unit �� Obstacle ������ײ
        int Unit = LayerMask.NameToLayer("Unit");
        int Obstacle = LayerMask.NameToLayer("Obstacle");
        if (Unit >= 0 && Obstacle >= 0) Physics.IgnoreLayerCollision(Unit, Obstacle, false);

        // ����Unit �� HexBoard���� Layer��������������ײ���������ã�
        int Board = LayerMask.NameToLayer("Layer"); // ����� "HexBoard"�������ָĵ�
        if (Unit >= 0 && Board >= 0) Physics.IgnoreLayerCollision(Unit, Board, true);

        Debug.Log("�Ѱ���С��У׼ Layer ��ײ���󣨿ɰ����������ԣ���");
    }
    */
}
#endif
