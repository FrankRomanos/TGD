using UnityEngine;
using TMPro; // ������TextMeshPro�����ռ�
using System;
using UnityEngine.UI;

public class UnitWorldUI : MonoBehaviour
{
    [Header("Ѫ����������")]
    [SerializeField] private Image healthBarFill; // ԭ��Ѫ�����㣨��fillAmount���£�
    [SerializeField] private HealthSystem healthSystem; // ԭ��Ѫ��ϵͳ����

    [Header("������Ѫ���ı�����")]
    [SerializeField] private TextMeshProUGUI healthText; // Ѫ��������ı����

    private void Start()
    {
        // ����Ѫ���仯�¼���ԭ���߼���
        healthSystem.OnDamaged += HealthSystem_OnDamaged;
        // ��ʼ����Ѫ�����ı�
        UpdateHealthBarAndText();
    }

    // Ѫ���仯ʱ������ԭ���߼��������ı����£�
    private void HealthSystem_OnDamaged(object sender, EventArgs e)
    {
        UpdateHealthBarAndText();
    }

    // ���ģ�ͬʱ����Ѫ��fillAmount���ı�
    private void UpdateHealthBarAndText()
    {
        // 1. ԭ���߼�������Ѫ�������
        healthBarFill.fillAmount = healthSystem.GetHealthNormalized();

        // 2. �����߼������㲢��ʾ����������Ѫ�� + �ٷֱȡ�
        int currentHealth = healthSystem.GetCurrentHealth();
        int maxHealth = healthSystem.GetMaxHealth();
        // ����������ʽ����1k=1000��1m=1000000��
        string formattedHealth = FormatHealthWithWesternNotation(currentHealth);
        // �ٷֱȣ���λС����
        float healthPercent = (float)currentHealth / maxHealth * 100f;
        // �����ı���ʾ����"1.2k (50.00%)"��
        healthText.text = $"{formattedHealth} ({healthPercent:F2}%)";
    }

    // ����������������ʽ������
    private string FormatHealthWithWesternNotation(int health)
    {
        if (health >= 1000000) // ���򼶣���100��
        {
            return $"{health / 1000000f:F1}m"; // ����1λС������ 1.5m
        }
        else if (health >= 1000) // ǧ������1000��
        {
            return $"{health / 1000f:F1}k"; // ����1λС������ 2.3k
        }
        else // ǧ���£�<1000��
        {
            return health.ToString(); // ֱ����ʾ���֣��� 850
        }
    }

    // ȡ�����ģ�ԭ���߼�����ֹ�ڴ�й©��
    private void OnDestroy()
    {
        healthSystem.OnDamaged -= HealthSystem_OnDamaged;
    }
}
