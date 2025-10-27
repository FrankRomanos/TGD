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
        const string LogPrefix = "[HexCameraTurnFocusDriver]";

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
            Debug.LogFormat("{0} AddDriverToCache driver={1} unitId={2}", LogPrefix, driver.name, driver.UnitRef != null ? driver.UnitRef.Id : "<null>");
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
                    Debug.LogFormat("{0} TryResolveDriver cached unit={1} driver={2}", LogPrefix, unit.Id, drv.name);
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
                    Debug.LogFormat("{0} TryResolveDriver discovered unit={1} driver={2}", LogPrefix, unit.Id, drv.name);
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
            if (cameraController == null)
            {
                Debug.LogWarningFormat("{0} TryFocus skipped, cameraController missing", LogPrefix);
                return;
            }

            if (unit == null)
            {
                Debug.LogWarningFormat("{0} TryFocus skipped, unit is null", LogPrefix);
                return;
            }

            var unitId = string.IsNullOrEmpty(unit.Id) ? "<unnamed>" : unit.Id;

            if (TryResolveDriver(unit, out var driver) && driver != null)
            {
                cameraController.RegisterDriver(driver);

                if (cameraController.Layout != null)
                {
                    Debug.LogFormat("{0} TryFocus unit={1} via driver layout -> AutoFocusHex", LogPrefix, unitId);
                    cameraController.AutoFocusHex(unit.Position);
                    return;
                }

                if (driver.Layout != null)
                {
                    var raw = driver.Layout.World(unit.Position, 0f);
                    Debug.LogFormat("{0} TryFocus unit={1} via driver rawWorld={2}", LogPrefix, unitId, raw);
                    cameraController.AutoFocus(raw);
                    return;
                }

                Debug.LogWarningFormat("{0} TryFocus unit={1} driver={2} has no layout", LogPrefix, unitId, driver.name);
            }

            if (cameraController.TryGetUnitFocusPosition(unit, out var world))
            {
                Debug.LogFormat("{0} TryFocus unit={1} via controller.TryGet world={2}", LogPrefix, unitId, world);
                cameraController.AutoFocus(world);
                return;
            }

            if (cameraController.Layout != null)
            {
                Debug.LogFormat("{0} TryFocus unit={1} fallback AutoFocusHex via controller layout", LogPrefix, unitId);
                cameraController.AutoFocusHex(unit.Position);
                return;
            }

            Debug.LogWarningFormat("{0} TryFocus unit={1} failed (no layout available)", LogPrefix, unitId);
        }
    }
}

