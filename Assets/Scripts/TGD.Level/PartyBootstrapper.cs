using System.Collections.Generic;
using UnityEngine;
using TGD.Combat;

namespace TGD.Level
{
    /// <summary>
    /// 开局把场景里的 UnitActor 收集进 CombatLoop 并启动战斗。
    /// </summary>
    public class PartyBootstrapper : MonoBehaviour
    {
        public CombatLoop combat;                 // 可空自动找
        public bool clearInspectorParties = true; // 是否覆盖 Inspector 里原有

        void Start()
        {
            if (!combat) combat = FindFirstObjectByType<CombatLoop>();
            if (!combat) { Debug.LogError("[PartyBootstrapper] 没找到 CombatLoop"); return; }

#if UNITY_2023_1_OR_NEWER
            var actors = UnityEngine.Object.FindObjectsByType<UnitActor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var actors = Resources.FindObjectsOfTypeAll<UnitActor>();
#endif
            var players = new List<Unit>();
            var enemies = new List<Unit>();

            foreach (var a in actors)
            {
                if (!a || !a.gameObject.activeInHierarchy) continue;
                var u = a.BuildUnit();
                if (u.TeamId == 0) players.Add(u); else enemies.Add(u);
                a.Bind(u); // 视觉层也立即绑定（环染色/飘字锚点等）
            }

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

            // 初始化并开跑
            combat.ReinitializeAndStart();

            // 让桥重新索引并立即能亮环
            CombatViewBridge.Instance?.RefreshBindings();
        }
    }
}
