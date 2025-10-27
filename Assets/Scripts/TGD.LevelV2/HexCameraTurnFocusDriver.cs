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
        readonly Dictionary<Unit, HexBoardTestDriver> _driverByUnit = new();
        readonly Dictionary<string, HexBoardTestDriver> _driverById = new();
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
            _driverByUnit.Clear();
            _driverById.Clear();
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

            driver.EnsureInit();
            if (!_driverCache.Contains(driver))
                _driverCache.Add(driver);

            CacheDriverBindings(driver);
        }

        void CacheDriverBindings(HexBoardTestDriver driver)
        {
            if (driver == null)
                return;

            var unit = driver.UnitRef;
            if (unit != null)
            {
                _driverByUnit[unit] = driver;
                if (!string.IsNullOrEmpty(unit.Id))
                    _driverById[unit.Id] = driver;
            }

            cameraController?.RegisterDriver(driver);
        }

        bool TryResolveDriver(Unit unit, out HexBoardTestDriver driver)
        {
            driver = null;
            if (unit == null)
                return false;

            if (_driverByUnit.TryGetValue(unit, out driver) && driver != null)
                return true;

            if (!string.IsNullOrEmpty(unit.Id) && _driverById.TryGetValue(unit.Id, out driver) && driver != null)
            {
                _driverByUnit[unit] = driver;
                return true;
            }

            foreach (var drv in _driverCache)
            {
                if (drv == null)
                    continue;

                if (drv.UnitRef == unit)
                {
                    driver = drv;
                    CacheDriverBindings(drv);
                    return true;
                }

                if (!string.IsNullOrEmpty(unit.Id) && drv.UnitRef != null && drv.UnitRef.Id == unit.Id)
                {
                    driver = drv;
                    CacheDriverBindings(drv);
                    _driverByUnit[unit] = drv;
                    return true;
                }
            }

            if (!autoDiscoverDrivers)
                return false;

            foreach (var drv in FindAllDrivers())
            {
                AddDriverToCache(drv);
                if (drv == null)
                    continue;

                if (drv.UnitRef == unit)
                {
                    driver = drv;
                    return true;
                }

                if (!string.IsNullOrEmpty(unit.Id) && drv.UnitRef != null && drv.UnitRef.Id == unit.Id)
                {
                    driver = drv;
                    _driverByUnit[unit] = drv;
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
                CacheDriverBindings(driver);

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
                    AddDriverToCache(ctxDriver);
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

