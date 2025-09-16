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
            // 1) 等待本地化系统初始化
            yield return LocalizationSettings.InitializationOperation;

            // 2) 异步取表（更稳，不会拿到 null）
            AsyncOperationHandle<StringTable> tableHandle =
                LocalizationSettings.StringDatabase.GetTableAsync(tableReference);
            yield return tableHandle;

            var table = tableHandle.Result;
            if (table == null)
            {
                Debug.LogError($"找不到 Localization Table：{tableReference}");
                yield break;
            }

            // 3) 取 Entry 并显示
            var entry = table.GetEntry(entryReference);
            if (entry != null)
            {
                // 建议直接用 LocalizedValue（已按当前 Locale 处理）
                string content = entry.LocalizedValue;
                Debug.Log($"【内容】：{content}");
                if (displayText) displayText.text = content;
            }
            else
            {
                Debug.LogError($"未找到 Entry：{entryReference}（表：{tableReference}）");
            }
        }
    }
}
