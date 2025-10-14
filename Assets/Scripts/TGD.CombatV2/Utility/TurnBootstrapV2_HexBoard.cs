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
            var readyDrivers = new List<HexBoardTestDriver>();
                readyDrivers.Add(drv);

                    readyDrivers.Add(bossDriver);
            RegisterUnitsOnMaps(readyDrivers, playerUnits, bossUnit);


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
        public List<HexBoardTestDriver> playerDrivers = new(); // 拖 1~4 个
        [Header("Boss (optional)")]
        public HexBoardTestDriver bossDriver;

        [Header("Auto Add Helpers")]
        public bool autoAddMissingContext = false; // true 时，若缺少 UnitRuntimeContext 将自动添加一个最小默认
        public bool autoWireAdapters = true;       // true 时，自动为角色上的 Adapter 赋 turnManager/context

        void Start()
        {
            if (turnManager == null)
            {
                Debug.LogError("[TurnBootstrap] TurnManagerV2 is null.", this);
                return;
            }

            // 组玩家 Unit 列表
            var playerUnits = new List<Unit>();
            foreach (var drv in playerDrivers)
            {
                if (!EnsureReady(drv)) continue;
                var ctx = drv.GetComponent<UnitRuntimeContext>();
                if (ctx == null && autoAddMissingContext)
                    ctx = drv.gameObject.AddComponent<UnitRuntimeContext>(); // 默认 stats 为序列化里自带的

                if (ctx == null)
                {
                    Debug.LogError($"[TurnBootstrap] Missing UnitRuntimeContext on {drv.name}", drv);
                    continue;
                }

                // 可选自动布线：把 Adapter 们连上 TM 与 Ctx
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

            // Boss（可空）
            Unit bossUnit = null;
            if (bossDriver != null && EnsureReady(bossDriver))
            {
                var ctx = bossDriver.GetComponent<UnitRuntimeContext>();
                if (ctx == null && autoAddMissingContext)
                    ctx = bossDriver.gameObject.AddComponent<UnitRuntimeContext>();

                if (ctx != null)
                {
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

            // 开战（支持 1~4 玩家，Boss 可空）
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
    }
}

