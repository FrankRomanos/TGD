using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Combat
{
    /// <summary>
    /// ��С�Ļغ���������һغ� �� Boss�غ� �� ��һغ� ... 
    /// ����ֻ�ǹǼܣ����ڹ��ڳ����е�һ���������������̡�
    /// </summary>
    public class CombatLoop : MonoBehaviour
    {
        [Tooltip("��Ҷ��飨���4�ˣ�")]
        public List<Unit> playerParty = new List<Unit>();

        [Tooltip("Boss/���˷�����Demo����1��Boss���ɣ�")]
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
                // ��һغϣ������λ���غϣ�
                foreach (var u in playerParty)
                {
                    if (u == null) continue;
                    StartTurn(u);
                    yield return RunUnitTurn(u, isPlayer: true);
                    EndTurn(u);
                }

                // �з��غϣ�Boss��
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
            // TODO: �غϿ�ʼ�������⻷��DOT/HOT������
        }

        private void EndTurn(Unit u)
        {
            u.EndTurn();
            // TODO: �غϽ�����������ȴ-1����Unit���������ʱ״̬��
        }

        private IEnumerator RunUnitTurn(Unit u, bool isPlayer)
        {
            // ��С�棺�ȴ�1֡��ʾ�õ�λ����ɻغϡ�
            // �Ժ�����UI��������/ѡ��/ִ�У�
            yield return null;
        }
    }
}
