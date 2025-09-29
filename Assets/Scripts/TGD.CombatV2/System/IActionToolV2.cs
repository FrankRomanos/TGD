using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    /// �κΡ�������׼������ƶ�Ԥ�������ȷ�ϡ��Ҽ�/ESC ȡ�����Ķ�������ʵ������ӿڡ�
    public interface IActionToolV2
    {
        /// ��������ʶ��/�󶨣��� "Move", "Attack"��
        string Id { get; }

        /// ����/�˳�����׼ģʽ��ʱ���ã������Լ�����ɫ/Ԥ��/����
        void OnEnterAim();
        void OnExitAim();

        /// ��� hover �� hex��������ÿ֡��ú�ת����
        void OnHover(Hex hex);

        /// ���ȷ�ϣ����߷���һ����ʵ��ִ�С���Э�̣������ƶ�����
        /// ���������������Э�̣�����ִ���ڼ��ģʽ�е� Busy��
        IEnumerator OnConfirm(Hex hex);
    }
}
