using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Combat
{
    /// <summary>
    /// 最小的回合驱动：玩家回合 → Boss回合 → 玩家回合 ... 
    /// 这里只是骨架，用于挂在场景中的一个物体上驱动流程。
    /// </summary>
    public class CombatLoop : MonoBehaviour
    {
        [Tooltip("玩家队伍（最多4人）")]
        public List<Unit> playerParty = new List<Unit>();

        [Tooltip("Boss/敌人方（本Demo先用1个Boss即可）")]
        public List<Unit> enemyParty = new List<Unit>();

        public bool autoStart = true;

        private void Start()
        {
            if (autoStart)
                StartCoroutine(RunLoop());
        }

        private IEnumerator RunLoop()
        {
            while (true)
            {
                // 玩家回合（逐个单位开回合）
                foreach (var u in playerParty)
                {
                    if (u == null) continue;
                    StartTurn(u);
                    yield return RunUnitTurn(u, isPlayer: true);
                    EndTurn(u);
                }

                // 敌方回合（Boss）
                foreach (var u in enemyParty)
                {
                    if (u == null) continue;
                    StartTurn(u);
                    yield return RunUnitTurn(u, isPlayer: false);
                    EndTurn(u);
                }

                yield return null;
            }
        }

        private void StartTurn(Unit u)
        {
            u.StartTurn();
            // TODO: 回合开始触发（光环、DOT/HOT）若有
        }

        private void EndTurn(Unit u)
        {
            u.EndTurn();
            // TODO: 回合结束触发（冷却-1已在Unit里），清理临时状态等
        }

        private IEnumerator RunUnitTurn(Unit u, bool isPlayer)
        {
            // 最小版：等待1帧表示该单位“完成回合”
            // 以后接你的UI（技能栏/选择/执行）
            yield return null;
        }
    }
}
