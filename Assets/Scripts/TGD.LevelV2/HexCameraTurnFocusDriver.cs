using UnityEngine;
using TGD.CombatV2;
using TGD.HexBoard;

namespace TGD.LevelV2
{
    /// <summary>
    /// 自动将 HexCameraControllerHB 对准当前行动单位。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HexCameraTurnFocusDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] HexCameraControllerHB cameraController;
        [SerializeField] TurnManagerV2 turnManager;
        [SerializeField] CombatActionManagerV2 combatManager;
        [SerializeField] bool focusOnNullChainFallbackToActive = true;

        static T AutoFind<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        void Awake()
        {
            if (!cameraController)
                cameraController = GetComponent<HexCameraControllerHB>();
            if (!turnManager)
                turnManager = AutoFind<TurnManagerV2>();
            if (!combatManager)
                combatManager = AutoFind<CombatActionManagerV2>();
        }

        void OnEnable()
        {
            Subscribe();
            TryFocus(turnManager != null ? turnManager.ActiveUnit : null);
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Subscribe()
        {
            if (turnManager != null)
                turnManager.TurnStarted += OnTurnStarted;
            if (combatManager != null)
                combatManager.ChainFocusChanged += OnChainFocusChanged;
        }

        void Unsubscribe()
        {
            if (turnManager != null)
                turnManager.TurnStarted -= OnTurnStarted;
            if (combatManager != null)
                combatManager.ChainFocusChanged -= OnChainFocusChanged;
        }

        void OnTurnStarted(Unit unit)
        {
            TryFocus(unit);
        }

        void OnChainFocusChanged(Unit unit)
        {
            if (unit == null && focusOnNullChainFallbackToActive && turnManager != null)
            {
                TryFocus(turnManager.ActiveUnit);
                return;
            }

            TryFocus(unit);
        }

        void TryFocus(Unit unit)
        {
            if (cameraController == null || unit == null)
                return;

            cameraController.AutoFocus(unit.Position);
        }
    }
}

