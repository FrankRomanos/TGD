namespace TGD.Core
{
    public static class CombatClock
    {
        public const int BaseTurnSeconds = 6;
        public static int CooldownToRounds(int seconds)
            => (seconds + BaseTurnSeconds - 1) / BaseTurnSeconds; // ceil
    }
}

