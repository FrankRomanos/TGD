using System.Collections.Generic;
using UnityEngine;
using TGD.Combat;

namespace TGD.Level
{
    /// <summary>
    /// ���ְѳ������ UnitActor �ռ��� Unit��
    /// �� teamId �ֵ� CombatLoop �� player/enemy���������غ�ѭ����
    /// </summary>
    public class PartyBootstrapper : MonoBehaviour
    {
        public CombatLoop combat;          // ������Զ���
        public bool clearInspectorParties = true; // ���� Inspector ��ԭ������

        void Start()
        {
            if (!combat) combat = FindFirstObjectByType<CombatLoop>();
            if (!combat) { Debug.LogError("[PartyBootstrapper] û�ҵ� CombatLoop"); return; }

            var actors = FindObjectsOfType<UnitActor>(includeInactive: true);
            var players = new List<Unit>();
            var enemies = new List<Unit>();

            foreach (var a in actors)
            {
                var u = a.BuildUnit();
                if (u.TeamId == 0) players.Add(u); else enemies.Add(u);
                a.Bind(u); // �Ӿ����ս��ģ�ͣ��ŵ׻�/Ʈ�ֵȣ�
            }

            // ���� CombatLoop �Ķ��鲢�ؽ�ϵͳ
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

            combat.ReinitializeAndStart(); // ���·� CombatLoop �Ĳ��䷽��
        }
    }
}
