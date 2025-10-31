using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

namespace TGD.UIV2.Battle
{
    /// <summary>
    /// ��չʾ�õĻغ���ʾ Banner��
    /// ���Լ������� TurnManagerV2����֪��˭�����˭�ǵ��ˡ�
    /// BattleUIService ��Ψһ�ܵ������ġ�ָ�ӹ١���
    /// </summary>
    public sealed class TurnBannerController : MonoBehaviour
    {
        [Header("UI Refs")]
        public TMP_Text messageText;     // �İ������� "��һغϿ�ʼ" / "ĳĳ �ĻغϿ�ʼ"
        public CanvasGroup canvasGroup;  // ����banner��CanvasGroup���������뵭��
        public Image glow;               // ��ѡ�����Ǹ���ɫ���/���ⱳ����û�о�����

        [Header("Colors")]
        public Color friendlyColor = new(0.2f, 0.85f, 0.2f); // �ѷ�/�����ɫ
        public Color enemyColor = new(0.85f, 0.2f, 0.2f); // �з���ɫ
        public Color neutralColor = new(1f, 1f, 1f);    // ���ף���������Ժ��벥������ʾ��

        [Header("Timing")]
        [Min(0.1f)]
        public float displaySeconds = 1.2f; // ��������Ļ�ϵ�ʱ�䣨����������
        [Min(0f)]
        public float fadeOutDuration = 0.25f; // �������
        public bool enableFadeOut = true;
        public AnimationCurve fadeOutCurve =
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        float _timer;
        bool _showing;
        Coroutine _fadeRoutine;

        void Awake()
        {
            ForceHideImmediate();
        }

        void OnDisable()
        {
            // rig �ص�ʱȷ��˲������ɾ���������ʬ UI
            ForceHideImmediate();
        }

        void Update()
        {
            if (!_showing)
                return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _showing = false;
                BeginFadeOut();
            }
        }

        /// <summary>
        /// BattleUIService ÿ����������Ҫ�������һ���¡���ʱ��ͻ�������
        /// isPlayerSide = true ���ѷ���ɫ��false �õз���ɫ��
        /// ����Դ����ġ�ԭ�ȵ� "Begin T1(1P)" �����㡣
        /// </summary>
        public void ShowBanner(string message, bool isPlayerSide)
        {
            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
                messageText.color = isPlayerSide ? friendlyColor : enemyColor;
            }

            if (glow != null)
            {
                glow.color = isPlayerSide ? friendlyColor : enemyColor;
            }

            // ����������
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _timer = Mathf.Max(0.1f, displaySeconds);
            _showing = true;
        }

        /// <summary>
        /// BattleUIService ������ OnEnable ��ʼ���� OnDisable �����ʱ��������
        /// �������أ��������֣�����alpha��
        /// </summary>
        public void ForceHideImmediate()
        {
            _showing = false;
            _timer = 0f;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (messageText != null)
                messageText.text = string.Empty;
        }

        void BeginFadeOut()
        {
            if (!enableFadeOut || canvasGroup == null || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                ForceHideImmediate();
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            _fadeRoutine = StartCoroutine(FadeOutRoutine());
        }

        IEnumerator FadeOutRoutine()
        {
            float dur = Mathf.Max(0f, fadeOutDuration);
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            float endAlpha = 0f;

            if (dur <= 0f)
            {
                if (canvasGroup != null)
                    canvasGroup.alpha = 0f;
            }
            else
            {
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float u = dur <= 0f ? 1f : Mathf.Clamp01(t / dur);
                    float k = fadeOutCurve != null ? fadeOutCurve.Evaluate(u) : u;
                    if (canvasGroup != null)
                        canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, k);
                    yield return null;
                }
                if (canvasGroup != null)
                    canvasGroup.alpha = endAlpha;
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
