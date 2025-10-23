using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;  // AsyncOperationHandle<T>

namespace TGD.UI
{
    public class LocalizationTestWithoutIsReady : MonoBehaviour
    {
        public string tableReference = "SkillDescription";
        public string entryReference = "skill_SK001_description";
        public Text displayText;

        private IEnumerator Start()
        {
            // 1) �ȴ����ػ�ϵͳ��ʼ��
            yield return LocalizationSettings.InitializationOperation;

            // 2) �첽ȡ�����ȣ������õ� null��
            AsyncOperationHandle<StringTable> tableHandle =
                LocalizationSettings.StringDatabase.GetTableAsync(tableReference);
            yield return tableHandle;

            var table = tableHandle.Result;
            if (table == null)
            {
                Debug.LogError($"�Ҳ��� Localization Table��{tableReference}");
                yield break;
            }

            // 3) ȡ Entry ����ʾ
            var entry = table.GetEntry(entryReference);
            if (entry != null)
            {
                // ����ֱ���� LocalizedValue���Ѱ���ǰ Locale ����
                string content = entry.LocalizedValue;
                Debug.Log($"�����ݡ���{content}");
                if (displayText) displayText.text = content;
            }
            else
            {
                Debug.LogError($"δ�ҵ� Entry��{entryReference}����{tableReference}��");
            }
        }
    }
}
