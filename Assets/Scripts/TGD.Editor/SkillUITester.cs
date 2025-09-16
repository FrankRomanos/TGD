using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using TGD.Data;

namespace TGD.UI
{

    public class SkillUITester_Simple : EditorWindow
    {
        // ���������ػ��ֵ䣨key �� ʵ�������ı���
        private Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        // ���������ػ��ļ�·��������Ķԣ���
        private string localizationFilePath = "Assets/Localization/Localization_Skills.csv";
        private List<SkillDefinition> allSkillDatas = new List<SkillDefinition>();
        private SkillDefinition selectedSkill;
        private Vector2 scrollPos;
        // �Զ���ѡ��״̬��С�Ͱ�ť��ʽ����������ڵ�miniButtonSelected��
        private GUIStyle selectedMiniButton;

        [MenuItem("Tools/Skill/�򻯰�UI���Դ���")]
        public static void OpenTestWindow()
        {
            GetWindow<SkillUITester_Simple>("����UI���ԣ��򻯰棩").minSize = new Vector2(500, 350);
        }

        private void OnEnable()
        {
            // ��ʼ���Զ���ѡ����ʽ��ѡ��ʱ������ǳ�ң���δѡ�����֣�
            selectedMiniButton = new GUIStyle(EditorStyles.miniButton);
            selectedMiniButton.normal.background = EditorStyles.toolbarButton.active.background;

            LoadLocalizationData();

            // ����SkillData���滻�����ʵ��·������"SkillDatas"��
            string skillDataPath = "SkillData";
            SkillDefinition[] loadedSkills = Resources.LoadAll<SkillDefinition>(skillDataPath);
            allSkillDatas.Clear();
            allSkillDatas.AddRange(loadedSkills);
            allSkillDatas.Sort((a, b) =>
            {
                // ���Խ� skillID ת�����ֱȽ�
                if (int.TryParse(a.skillID, out int idA) && int.TryParse(b.skillID, out int idB))
                {
                    return idA.CompareTo(idB); // ��������1��2��3��
                }
                // ���2��skillID ��ǰ׺���� SK1��SK2��SK10��������ȡ�����ٱȽ�
                else
                {
                    int numA = ExtractNumberFromSkillID(a.skillID);
                    int numB = ExtractNumberFromSkillID(b.skillID);
                    return numA.CompareTo(numB); // ����ȡ����������
                }
            });
            if (allSkillDatas.Count > 0) selectedSkill = allSkillDatas[0];
        }
        // ����������localization_skill.csv���ֵ�
        private void LoadLocalizationData()
        {
            localizationDict.Clear(); // ��վ�����

            // ����ļ��Ƿ����
            if (!File.Exists(localizationFilePath))
            {
                Debug.LogError("���ػ��ļ��Ҳ�����·����" + localizationFilePath);
                return;
            }

            // ��ȡCSV������
            string[] allLines = File.ReadAllLines(localizationFilePath);
            if (allLines.Length < 2) // ������Ҫ1�б�ͷ+1������
            {
                Debug.LogError("���ػ��ļ�����Ϊ�ջ��ʽ����");
                return;
            }

            // ����ÿ�����ݣ�������ͷ�У��ӵ�2�п�ʼ��
            for (int i = 1; i < allLines.Length; i++)
            {
                string line = allLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // �ָ�CSV�У��򵥴��������ŷָ�ʺ��޶��ŵ��ı���
                string[] parts = line.Split(',');
                if (parts.Length >= 2) // ȷ��������key����������
                {
                    string key = parts[0].Trim(); // ��1����key
                    string desc = parts[1].Trim(); // ��2���������������������CSV������������
                    if (!localizationDict.ContainsKey(key))
                    {
                        localizationDict.Add(key, desc); // �����ֵ�
                    }
                }
            }

            Debug.Log("���ػ����ݼ�����ɣ���" + localizationDict.Count + "��");
        }

        private void OnGUI()
        {
            GUILayout.Label("����UI���ԣ�ͼ��+������Ϣ��", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // ���ҷ��������ѡ���ܣ��Ҳ࿴����
            GUILayout.BeginHorizontal();

            // ��ࣺ�����б��޸���ʽ����
            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label("�����б�", EditorStyles.miniBoldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(400));

            foreach (var skill in allSkillDatas)
            {
                // �ؼ��޸������Զ����selectedMiniButton���miniButtonSelected
                if (GUILayout.Button(
                    $"ID:{skill.skillID} | {skill.skillName}",
                    selectedSkill == skill ? selectedMiniButton : EditorStyles.miniButton))
                {
                    selectedSkill = skill;
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(40);

            // �Ҳࣺ�������飨���޸ģ�����ԭ����
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Label("��������", EditorStyles.miniBoldLabel);
            GUILayout.Space(5);

            if (selectedSkill != null)
            {
                // ��ʾͼ��
                GUILayout.Label("����ͼ�꣺", EditorStyles.miniLabel);
                if (selectedSkill.icon != null)
                {
                    // ��Spriteת��Texture2D������Label��ʾ
                    Texture2D iconTexture = selectedSkill.icon.texture;
                    // ��Label����ͼ�꣬�̶���ߣ��޶��ఴť
                    GUILayout.Label(
                        new GUIContent(iconTexture),
                        GUILayout.Width(80),  // ͼ���ȣ���֮ǰһ�£�
                        GUILayout.Height(80)  // ͼ��߶ȣ���֮ǰһ�£�
                    );
                }
                else
                {
                    // ͼ��Ϊ��ʱ����ʾ����ͼ�ꡱ��ʾ�����������
                    GUILayout.Label(
                        "��ͼ��",
                        EditorStyles.helpBox,
                        GUILayout.Width(100),
                        GUILayout.Height(100)
                    );
                }

                // ��ʾ���ơ�����������
                GUILayout.Label("�������ƣ�");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.namekey),
                GUILayout.Height(40));



                GUILayout.Label("����������");
                EditorGUILayout.TextArea(GetLocalizedDesc(selectedSkill.descriptionKey),
                GUILayout.Height(50));

                GUILayout.Label("�������ԣ�", EditorStyles.miniLabel);
                GUILayout.Label($"ְҵ��{selectedSkill.classID}");
                GUILayout.Label($"�������ͣ�{selectedSkill.actionType}");
                GUILayout.Label($"��ȴʱ�䣺{selectedSkill.cooldownSeconds}��");
                GUILayout.Label($"��ȴʱ�䣺{selectedSkill.cooldownRounds}�غ�");
            }
            else
            {
                GUILayout.Label("������ѡ��һ������", EditorStyles.helpBox);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        private string GetLocalizedDesc(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "������KeyΪ�ա�";

            // ���ֵ��в��ң��ҵ������������Ҳ���������ʾ
            if (localizationDict.TryGetValue(key, out string desc))
                return desc;
            else
                return "��δ�ҵ�������key=" + key + "��";
        }
        // ������������ skillID ����ȡ���֣�֧�� SK1��Skill2��ID3 �ȸ�ʽ��
        private int ExtractNumberFromSkillID(string skillID)
        {
            if (string.IsNullOrEmpty(skillID)) return 0;
            // ��������ʽ��ȡ���������ַ�����ת��int
            string numberStr = System.Text.RegularExpressions.Regex.Replace(skillID, @"[^0-9]", "");
            return int.TryParse(numberStr, out int num) ? num : 0;
        }
    }
}