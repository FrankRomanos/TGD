using System;

namespace TGD.Combat
{
    public static class CombatClock
    {
        /// <summary>固定基础回合秒数（你的规则 = 6）</summary>
        public const int BaseTurnSeconds = 6;

        /// <summary>把冷却秒数换算为冷却轮数（取上整）</summary>
        public static int CooldownToRounds(int seconds)
        {
            if (seconds <= 0) return 0;
            return (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds;
        }
    }
}
