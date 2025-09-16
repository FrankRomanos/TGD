using UnityEngine;
using UnityEngine.Localization.Settings;

public class LocaleSwitcher : MonoBehaviour
{
    [SerializeField] string chooseLanguage = "cn";
    void Start()
    {
        // 设置你希望的语言（示例：切到英文 "en"）
        var locale = LocalizationSettings.AvailableLocales.GetLocale(chooseLanguage);
        if (locale != null)
        {
            LocalizationSettings.SelectedLocale = locale;
            Debug.Log($"已切换到语言：{locale.Identifier.Code}");
        }
        else
        {
            Debug.LogWarning("未找到语言：en");
        }
    }
}
