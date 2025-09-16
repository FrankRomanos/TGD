using System;

namespace TGD.Combat
{
    public static class CombatClock
    {
        /// <summary>�̶������غ���������Ĺ��� = 6��</summary>
        public const int BaseTurnSeconds = 6;

        /// <summary>����ȴ��������Ϊ��ȴ������ȡ������</summary>
        public static int CooldownToRounds(int seconds)
        {
            if (seconds <= 0) return 0;
            return (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;
        }
    }
}
