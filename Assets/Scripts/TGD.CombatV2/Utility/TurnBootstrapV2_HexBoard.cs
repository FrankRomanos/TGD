// File: TGD.CombatV2/Utility/TurnBootstrapV2_HexBoard.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// 把 HexBoardTestDriver 提供的 Unit 与同物体上的 UnitRuntimeContext 绑定到 TurnManagerV2，并启动回合循环。
    /// - 支持 1~4 名玩家；Boss 可选（为空则敌方相位仅显示起始提示与 1s 停顿）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnBootstrapV2_HexBoard : MonoBehaviour
    {
        [Header("Core")]
        public TurnManagerV2 turnManager;

        [Header("Players (1~4)")]
        public List<HexBoardTestDriver> playerDrivers = new(); 
        [Header("Boss (optional)")]
        public HexBoardTestDriver bossDriver;

        [Header("Auto Add Helpers")]
        public bool autoAddMissingContext = false;
        public bool autoWireAdapters = true;       

        void Start()
        {
            if (turnManager == null)
            {
                Debug.LogError("[TurnBootstrap] TurnManagerV2 is null.", this);
                return;
            }

            //  Unit б
            var playerUnits = new List<Unit>();
            var readyDrivers = new List<HexBoardTestDriver>();
            foreach (var drv in playerDrivers)
            {
                if (!EnsureReady(drv)) continue;
                var ctx = drv.GetComponent<UnitRuntimeContext>();
                if (ctx == null && autoAddMissingContext)
                    ctx = drv.gameObject.AddComponent<UnitRuntimeContext>(); 

                if (ctx == null)
                {
                    Debug.LogError($"[TurnBootstrap] Missing UnitRuntimeContext on {drv.name}", drv);
                    continue;
                }

                readyDrivers.Add(drv);

                if (autoWireAdapters)
                {
                    var wire = drv.GetComponent<UnitAutoWireV2>();
                    if (wire == null) wire = drv.gameObject.AddComponent<UnitAutoWireV2>();
                    wire.turnManager = turnManager;
                    wire.context = ctx;
                    wire.Apply();
                }

                turnManager.Bind(drv.UnitRef, ctx);
                playerUnits.Add(drv.UnitRef);
            }

            Unit bossUnit = null;
            if (bossDriver != null && EnsureReady(bossDriver))
            {
                var ctx = bossDriver.GetComponent<UnitRuntimeContext>();
                if (ctx == null && autoAddMissingContext)
                    ctx = bossDriver.gameObject.AddComponent<UnitRuntimeContext>();

                if (ctx != null)
                {
                    readyDrivers.Add(bossDriver);
                    if (autoWireAdapters)
                    {
                        var wire = bossDriver.GetComponent<UnitAutoWireV2>();
                        if (wire == null) wire = bossDriver.gameObject.AddComponent<UnitAutoWireV2>();
                        wire.turnManager = turnManager;
                        wire.context = ctx;
                        wire.Apply();
                    }

                    turnManager.Bind(bossDriver.UnitRef, ctx);
                    bossUnit = bossDriver.UnitRef;
                }
            }

            RegisterUnitsOnMaps(readyDrivers, playerUnits, bossUnit);

            turnManager.StartBattle(playerUnits, bossUnit);
            Debug.Log($"[TurnBootstrap] StartBattle players={playerUnits.Count} boss={(bossUnit != null ? "yes" : "no")}", this);
        }

        bool EnsureReady(HexBoardTestDriver drv)
        {
            if (drv == null) return false;
            drv.EnsureInit();
            if (!drv.IsReady)
            {
                Debug.LogError($"[TurnBootstrap] Driver not ready: {drv.name}", drv);
                return false;
            }
            return true;
        }

        void RegisterUnitsOnMaps(List<HexBoardTestDriver> drivers, List<Unit> players, Unit bossUnit)
        {
            if (drivers == null || drivers.Count == 0)
                return;

            var units = new List<Unit>();
            if (players != null)
                units.AddRange(players);
            if (bossUnit != null)
                units.Add(bossUnit);

            if (units.Count == 0)
                return;

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                var coord = unit.Position;
                string label = TurnManagerV2.FormatUnitLabel(unit);
                Debug.Log($"[Map] Register {label} at ({coord.q},{coord.r})", this);

                foreach (var drv in drivers)
                {
                    if (drv == null || drv.Map == null)
                        continue;
                    drv.Map.Set(unit, coord);
                }
            }
        }
    }
}
