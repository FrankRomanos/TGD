// File: ChainPopupSmokeTest.cs  (Unity 6)
using UnityEngine;
using TGD.UIV2;       // ChainPopupPresenter
using TGD.CombatV2;  // ChainPopupWindowData

public class ChainPopupSmokeTest : MonoBehaviour
{
    [SerializeField] ChainPopupPresenter presenter;

    [Header("Test Payload")]
    [SerializeField] string header = "Attack (Test)";
    [SerializeField] string prompt = "Pick one";
    [SerializeField] bool isEnemyPhase = false;
    [SerializeField] string context = ""; // 允许留空

    void Reset()
    {
        // 方便一键找到场景中的 Presenter（包含未激活）
        presenter = FindAnyObjectByType<ChainPopupPresenter>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (presenter == null)
                presenter = FindAnyObjectByType<ChainPopupPresenter>(FindObjectsInactive.Include);

            if (presenter == null)
            {
                Debug.LogError("[SmokeTest] No ChainPopupPresenter in scene.");
                return;
            }

            // ✅ readonly struct 用“位置参数”构造
            var w = new ChainPopupWindowData(
                header,
                prompt,
                isEnemyPhase,
                string.IsNullOrEmpty(context) ? null : context
            );

            Debug.Log("[SmokeTest] presenter.OpenWindow()");
            presenter.OpenWindow(w);
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            if (presenter != null)
            {
                presenter.CloseWindow();
                Debug.Log("[SmokeTest] presenter.CloseWindow()");
            }
        }
    }
}
