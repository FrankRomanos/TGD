using UnityEngine;
using TMPro; // 需引入TextMeshPro命名空间
using System;
using UnityEngine.UI;

public class UnitWorldUI : MonoBehaviour
{
    [Header("血条核心引用")]
    [SerializeField] private Image healthBarFill; // 原有血条填充层（用fillAmount更新）
    [SerializeField] private HealthSystem healthSystem; // 原有血量系统引用

    [Header("新增：血量文本引用")]
    [SerializeField] private TextMeshProUGUI healthText; // 血条中央的文本组件

    private void Start()
    {
        // 订阅血量变化事件（原有逻辑）
        healthSystem.OnDamaged += HealthSystem_OnDamaged;
        // 初始更新血条和文本
        UpdateHealthBarAndText();
    }

    // 血量变化时触发（原有逻辑，新增文本更新）
    private void HealthSystem_OnDamaged(object sender, EventArgs e)
    {
        UpdateHealthBarAndText();
    }

    // 核心：同时更新血条fillAmount和文本
    private void UpdateHealthBarAndText()
    {
        // 1. 原有逻辑：更新血条填充量
        healthBarFill.fillAmount = healthSystem.GetHealthNormalized();

        // 2. 新增逻辑：计算并显示“西方计数血量 + 百分比”
        int currentHealth = healthSystem.GetCurrentHealth();
        int maxHealth = healthSystem.GetMaxHealth();
        // 西方计数格式化（1k=1000，1m=1000000）
        string formattedHealth = FormatHealthWithWesternNotation(currentHealth);
        // 百分比（两位小数）
        float healthPercent = (float)currentHealth / maxHealth * 100f;
        // 设置文本（示例："1.2k (50.00%)"）
        healthText.text = $"{formattedHealth} ({healthPercent:F2}%)";
    }

    // 新增：西方计数格式化方法
    private string FormatHealthWithWesternNotation(int health)
    {
        if (health >= 1000000) // 百万级（≥100万）
        {
            return $"{health / 1000000f:F1}m"; // 保留1位小数，如 1.5m
        }
        else if (health >= 1000) // 千级（≥1000）
        {
            return $"{health / 1000f:F1}k"; // 保留1位小数，如 2.3k
        }
        else // 千以下（<1000）
        {
            return health.ToString(); // 直接显示数字，如 850
        }
    }

    // 取消订阅（原有逻辑，防止内存泄漏）
    private void OnDestroy()
    {
        healthSystem.OnDamaged -= HealthSystem_OnDamaged;
    }
}
