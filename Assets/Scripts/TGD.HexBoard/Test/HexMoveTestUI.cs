using UnityEngine;
using UnityEngine.UI;

namespace TGD.HexBoard
{
    /// <summary>
    /// 极简测试入口（无 OnGUI）：
    /// - 热键：V = 显示/隐藏可移动格
    /// - UI：把 Button 的 OnClick 绑到 ShowRange()/HideRange()/ToggleRange()
    /// - （可选）在 Inspector 调整 steps 并勾选 overrideSteps 覆盖 HexClickMover.config.fallbackSteps
    /// </summary>
    public class HexMoveTestUI : MonoBehaviour
    {
        [Header("Refs")]
        public HexClickMover mover;

        [Header("Optional UI Hooks")]
        public Button showButton;
        public Button hideButton;
        public Button toggleButton;

        [Header("Debug Steps Override")]
        public bool overrideSteps = false;
        [Min(0)] public int steps = 3;

        bool shown = false;

        void Reset()
        {
            // 尝试自动找同物体上的 mover
            if (mover == null) mover = GetComponent<HexClickMover>();
        }

        void Awake()
        {
            // 绑定按钮（如果你拖了引用，就自动绑；没拖就忽略）
            if (showButton) showButton.onClick.AddListener(ShowRange);
            if (hideButton) hideButton.onClick.AddListener(HideRange);
            if (toggleButton) toggleButton.onClick.AddListener(ToggleRange);
        }

        void Update()
        {
            // 热键：V 显示/隐藏
            if (Input.GetKeyDown(KeyCode.V)) ToggleRange();
        }

        // ===== 提供给 UGUI 直接绑定 =====
        public void ShowRange()
        {
            if (!mover) return;
            ApplyStepOverride();
            mover.ShowRange();
            shown = true;
        }

        public void HideRange()
        {
            if (!mover) return;
            mover.HideRange();
            shown = false;
        }

        public void ToggleRange()
        {
            if (!mover) return;
            if (shown) HideRange();
            else ShowRange();
        }

        // Slider/Dropdown 可直接绑这个（传入 int）
        public void SetSteps(int s)
        {
            steps = Mathf.Max(0, s);
            if (overrideSteps && mover && mover.config)
            {
                mover.config.fallbackSteps = steps;
                if (shown) mover.ShowRange(); // 刷新显示
            }
        }

        void ApplyStepOverride()
        {
            if (overrideSteps && mover && mover.config)
                mover.config.fallbackSteps = Mathf.Max(0, steps);
        }
    }
}
