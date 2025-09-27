using UnityEngine;
using UnityEngine.UI;

namespace TGD.HexBoard
{
    /// <summary>
    /// ���������ڣ��� OnGUI����
    /// - �ȼ���V = ��ʾ/���ؿ��ƶ���
    /// - UI���� Button �� OnClick �� ShowRange()/HideRange()/ToggleRange()
    /// - ����ѡ���� Inspector ���� steps ����ѡ overrideSteps ���� HexClickMover.config.fallbackSteps
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
            // �����Զ���ͬ�����ϵ� mover
            if (mover == null) mover = GetComponent<HexClickMover>();
        }

        void Awake()
        {
            // �󶨰�ť��������������ã����Զ���û�Ͼͺ��ԣ�
            if (showButton) showButton.onClick.AddListener(ShowRange);
            if (hideButton) hideButton.onClick.AddListener(HideRange);
            if (toggleButton) toggleButton.onClick.AddListener(ToggleRange);
        }

        void Update()
        {
            // �ȼ���V ��ʾ/����
            if (Input.GetKeyDown(KeyCode.V)) ToggleRange();
        }

        // ===== �ṩ�� UGUI ֱ�Ӱ� =====
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

        // Slider/Dropdown ��ֱ�Ӱ���������� int��
        public void SetSteps(int s)
        {
            steps = Mathf.Max(0, s);
            if (overrideSteps && mover && mover.config)
            {
                mover.config.fallbackSteps = steps;
                if (shown) mover.ShowRange(); // ˢ����ʾ
            }
        }

        void ApplyStepOverride()
        {
            if (overrideSteps && mover && mover.config)
                mover.config.fallbackSteps = Mathf.Max(0, steps);
        }
    }
}
