using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TGD.UI
{
    /// <summary>技能格子：图标、热键、冷却填充、禁用灰显</summary>
    public class SkillSlotView : MonoBehaviour
    {
        [Header("Refs")]
        public Button button;
        public Image icon;
        public TMP_Text hotkeyText;
        public TMP_Text cdText;
        public Image cdMask;         // Image type=Filled, FillMethod=Radial360
        public CanvasGroup cg;        // 控制禁用/透明（可选）

        [Header("Look")]
        public Sprite defaultIcon;
        public Color enabledColor = Color.white;
        public Color disabledColor = new(1f, 1f, 1f, 0.35f);

        // 数据
        public string skillId { get; private set; }

        void Reset()
        {
            button = GetComponent<Button>();
            cg = GetComponent<CanvasGroup>();
        }

        public void Bind(string id, Sprite sp, string hotkey)
        {
            skillId = id;
            if (icon) icon.sprite = sp ? sp : defaultIcon;
            if (hotkeyText) hotkeyText.text = hotkey ?? "";
            SetCooldown(0f, 0);
            SetInteractable(true);
        }

        /// <param name="norm">0~1 （1=刚进入冷却，0=冷却完）</param>
        public void SetCooldown(float norm, int uiTurnsLeft)
        {
            if (cdMask) cdMask.fillAmount = Mathf.Clamp01(norm);
            if (cdText) cdText.text = uiTurnsLeft > 0 ? uiTurnsLeft.ToString() : "";
        }

        public void SetInteractable(bool on)
        {
            if (button) button.interactable = on;
            if (icon) icon.color = on ? enabledColor : disabledColor;
            if (cg) cg.alpha = on ? 1f : 0.8f;
        }
    }
}
