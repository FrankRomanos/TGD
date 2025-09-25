using System.Collections.Generic;
using UnityEngine;
using TGD.Combat;

namespace TGD.Level
{
    /// <summary>
    /// ���ְѳ������ UnitActor �ռ��� CombatLoop ������ս����
    /// </summary>
    public class PartyBootstrapper : MonoBehaviour
    {
        public CombatLoop combat;                 // �ɿ��Զ���
        public bool clearInspectorParties = true; // �Ƿ񸲸� Inspector ��ԭ��

        void Start()
        {
            if (!combat) combat = FindFirstObjectByType<CombatLoop>();
            if (!combat) { Debug.LogError("[PartyBootstrapper] û�ҵ� CombatLoop"); return; }

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
                a.Bind(u); // �Ӿ���Ҳ�����󶨣���Ⱦɫ/Ʈ��ê��ȣ�
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

            // ��ʼ��������
            combat.ReinitializeAndStart();

            // ������������������������
            CombatViewBridge.Instance?.RefreshBindings();
        }
    }
}
