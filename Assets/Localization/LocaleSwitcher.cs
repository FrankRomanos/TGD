using UnityEngine;
using UnityEngine.Localization.Settings;

public class LocaleSwitcher : MonoBehaviour
{
    [SerializeField] string chooseLanguage = "cn";
    void Start()
    {
        // ������ϣ�������ԣ�ʾ�����е�Ӣ�� "en"��
        var locale = LocalizationSettings.AvailableLocales.GetLocale(chooseLanguage);
        if (locale != null)
        {
            LocalizationSettings.SelectedLocale = locale;
            Debug.Log($"���л������ԣ�{locale.Identifier.Code}");
        }
        else
        {
            Debug.LogWarning("δ�ҵ����ԣ�en");
        }
    }
}
