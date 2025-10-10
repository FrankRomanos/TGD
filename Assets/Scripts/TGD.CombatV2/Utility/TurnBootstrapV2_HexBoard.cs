// File: TGD.CombatV2/Utility/TurnBootstrapV2_HexBoard.cs
using System.Collections.Generic;
using UnityEngine;
using TGD.HexBoard;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    /// <summary>
    /// �� HexBoardTestDriver �ṩ�� Unit ��ͬ�����ϵ� UnitRuntimeContext �󶨵� TurnManagerV2���������غ�ѭ����
    /// - ֧�� 1~4 ����ң�Boss ��ѡ��Ϊ����з���λ����ʾ��ʼ��ʾ�� 1s ͣ�٣���
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnBootstrapV2_HexBoard : MonoBehaviour
    {
        [Header("Core")]
        public TurnManagerV2 turnManager;

        [Header("Players (1~4)")]
        public List<HexBoardTestDriver> playerDrivers = new(); // �� 1~4 ��
        [Header("Boss (optional)")]
        public HexBoardTestDriver bossDriver;

        [Header("Auto Add Helpers")]
        public bool autoAddMissingContext = false; // true ʱ����ȱ�� UnitRuntimeContext ���Զ����һ����СĬ��
        public bool autoWireAdapters = true;       // true ʱ���Զ�Ϊ��ɫ�ϵ� Adapter �� turnManager/context

        void Start()
        {
            if (turnManager == null)
            {
                Debug.LogError("[TurnBootstrap] TurnManagerV2 is null.", this);
                return;
            }

            // ����� Unit �б�
            var playerUnits = new List<Unit>();
            foreach (var drv in playerDrivers)
            {
                if (!EnsureReady(drv)) continue;
                var ctx = drv.GetComponent<UnitRuntimeContext>();
                if (ctx == null && autoAddMissingContext)
                    ctx = drv.gameObject.AddComponent<UnitRuntimeContext>(); // Ĭ�� stats Ϊ���л����Դ���

                if (ctx == null)
                {
                    Debug.LogError($"[TurnBootstrap] Missing UnitRuntimeContext on {drv.name}", drv);
                    continue;
                }

                // ��ѡ�Զ����ߣ��� Adapter ������ TM �� Ctx
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

            // Boss���ɿգ�
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

            // ��ս��֧�� 1~4 ��ң�Boss �ɿգ�
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

