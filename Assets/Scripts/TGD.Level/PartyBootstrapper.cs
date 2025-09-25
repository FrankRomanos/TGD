using System.Collections.Generic;
using UnityEngine;
using TGD.Combat;

namespace TGD.Level
{
    /// <summary>
    /// 开局把场景里的 UnitActor 收集成 Unit，
    /// 按 teamId 分到 CombatLoop 的 player/enemy，并启动回合循环。
    /// </summary>
    public class PartyBootstrapper : MonoBehaviour
    {
        public CombatLoop combat;          // 不填就自动找
        public bool clearInspectorParties = true; // 覆盖 Inspector 里原有配置

        void Start()
        {
            if (!combat) combat = FindFirstObjectByType<CombatLoop>();
            if (!combat) { Debug.LogError("[PartyBootstrapper] 没找到 CombatLoop"); return; }

            var actors = FindObjectsOfType<UnitActor>(includeInactive: true);
            var players = new List<Unit>();
            var enemies = new List<Unit>();

            foreach (var a in actors)
            {
                var u = a.BuildUnit();
                if (u.TeamId == 0) players.Add(u); else enemies.Add(u);
                a.Bind(u); // 视觉层绑定战斗模型（脚底环/飘字等）
            }

            // 覆盖 CombatLoop 的队伍并重建系统
            if (clearInspectorParties)
            {
                combat.playerParty = players;
                combat.enemyParty = enemies;
            }
            else
            {
                combat.playerParty.AddRange(players);
                combat.enemyParty.AddRange(enemies);
            }

            combat.ReinitializeAndStart(); // 见下方 CombatLoop 的补充方法
        }
    }
}
