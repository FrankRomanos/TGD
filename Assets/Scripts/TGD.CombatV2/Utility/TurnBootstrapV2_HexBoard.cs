// File: TGD.CombatV2/Utility/TurnBootstrapV2_HexBoard.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// HexBoardTestDriver 提供 Unit 及其 UnitRuntimeContext 绑定到 TurnManagerV2。
    /// - 支持 1~4 名玩家与可选敌人列表，自动补全缺失组件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnBootstrapV2_HexBoard : MonoBehaviour
    {
        [Header("Core")]
        public TurnManagerV2 turnManager;

        [Header("Players (1~4)")]
        public List<HexBoardTestDriver> playerDrivers = new();

        [Header("Enemies (0~4)")]
        public List<HexBoardTestDriver> enemyDrivers = new();

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

            var playerUnits = new List<Unit>();
            foreach (var drv in playerDrivers)
                TryBindDriver(drv, playerUnits);

            var enemyUnits = new List<Unit>();
            foreach (var drv in enemyDrivers)
                TryBindDriver(drv, enemyUnits);

            if (bossDriver != null && !enemyDrivers.Contains(bossDriver))
                TryBindDriver(bossDriver, enemyUnits);

            turnManager.StartBattle(playerUnits, enemyUnits);
            Debug.Log($"[TurnBootstrap] StartBattle players={playerUnits.Count} enemies={enemyUnits.Count}", this);
        }

        bool EnsureReady(HexBoardTestDriver drv)
        {
            if (drv == null)
                return false;

            drv.EnsureInit();
            if (!drv.IsReady)
            {
                Debug.LogError($"[TurnBootstrap] Driver not ready: {drv.name}", drv);
                return false;
            }
            return true;
        }

        void TryBindDriver(HexBoardTestDriver drv, List<Unit> target)
        {
            if (drv == null || target == null)
                return;

            if (!EnsureReady(drv))
                return;

            var ctx = drv.GetComponent<UnitRuntimeContext>();
            if (ctx == null && autoAddMissingContext)
                ctx = drv.gameObject.AddComponent<UnitRuntimeContext>();

            if (ctx == null)
            {
                Debug.LogError($"[TurnBootstrap] Missing UnitRuntimeContext on {drv.name}", drv);
                return;
            }

            if (autoWireAdapters)
            {
                var wire = drv.GetComponent<UnitAutoWireV2>();
                if (wire == null)
                    wire = drv.gameObject.AddComponent<UnitAutoWireV2>();
                wire.turnManager = turnManager;
                wire.context = ctx;
                wire.Apply();
            }

            turnManager.Bind(drv.UnitRef, ctx);
            if (drv.UnitRef != null && !target.Contains(drv.UnitRef))
                target.Add(drv.UnitRef);
        }
    }
}
