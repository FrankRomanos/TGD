using System;

namespace TGD.Combat
{
    public interface ICombatTime
    {
        float Now { get; }
        void Advance(float deltaSeconds);
    }

    public sealed class CombatTime : ICombatTime
    {
        public float Now { get; private set; }

        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;
            Now += deltaSeconds;
        }
    }
}
