using System.Collections.Generic;
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
        [SerializeField] bool autoDiscoverDrivers = true;
        [SerializeField] List<HexBoardTestDriver> driverHints = new();

        readonly List<HexBoardTestDriver> _driverCache = new();
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
            RefreshDriverCache();
        }

        void OnEnable()
        {
            Subscribe();
            RefreshDriverCache();
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

        void RefreshDriverCache()
        {
            _driverCache.Clear();
            foreach (var drv in driverHints)
                AddDriverToCache(drv);

            if (!autoDiscoverDrivers)
                return;

            foreach (var drv in FindAllDrivers())
                AddDriverToCache(drv);
        }

        void AddDriverToCache(HexBoardTestDriver driver)
        {
            if (driver == null)
                return;

            if (_driverCache.Contains(driver))
                return;

            driver.EnsureInit();
            _driverCache.Add(driver);
            cameraController?.RegisterDriver(driver);
        }

        bool TryResolveDriver(Unit unit, out HexBoardTestDriver driver)
        {
            driver = null;
            if (unit == null)
                return false;

            foreach (var drv in _driverCache)
            {
                if (drv != null && drv.UnitRef == unit)
                {
                    driver = drv;
                    return true;
                }
            }

            if (!autoDiscoverDrivers)
                return false;

            foreach (var drv in FindAllDrivers())
            {
                AddDriverToCache(drv);
                if (drv != null && drv.UnitRef == unit)
                {
                    driver = drv;
                    return true;
                }
            }

            return false;
        }

        static HexBoardTestDriver[] FindAllDrivers()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<HexBoardTestDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<HexBoardTestDriver>();
#endif
        }

        void TryFocus(Unit unit)
        {
            if (cameraController == null || unit == null)
                return;

            if (cameraController.TryGetUnitFocusPosition(unit, out var world))
            {
                cameraController.AutoFocus(world);
                return;
            }

            if (TryResolveDriver(unit, out var driver) && driver != null)
            {
                cameraController.RegisterDriver(driver);

                Transform source = driver.unitView != null ? driver.unitView : driver.transform;
                if (source != null)
                {
                    cameraController.AutoFocus(source.position);
                    return;
                }
            }

            var context = turnManager != null ? turnManager.GetContext(unit) : null;
            if (context != null)
            {
                var ctxDriver = context.GetComponentInParent<HexBoardTestDriver>();
                if (ctxDriver != null)
                {
                    cameraController.RegisterDriver(ctxDriver);
                    var view = ctxDriver.unitView != null ? ctxDriver.unitView : ctxDriver.transform;
                    if (view != null)
                    {
                        cameraController.AutoFocus(view.position);
                        return;
                    }
                }

                var ctxTransform = context.transform;
                if (ctxTransform != null)
                {
                    cameraController.AutoFocus(ctxTransform.position);
                }
            }
        }
    }
}

