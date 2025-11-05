using System;
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Final composed configuration used to spawn a unit in combat.
    /// </summary>
    [Serializable]
    public sealed class FinalUnitConfig
    {
        public string unitId;
        public string displayName;
        public UnitFaction faction = UnitFaction.Friendly;
        public StatsV2 stats = new StatsV2();
        public List<LearnedAbility> abilities = new List<LearnedAbility>();
        public Sprite avatar;

        public bool IsFriendly => faction == UnitFaction.Friendly;
        public bool IsEnemy => faction == UnitFaction.Enemy;

        [Serializable]
        public struct LearnedAbility
        {
            public string actionId;
            public int initialCooldownSeconds;
        }
    }

    public enum UnitFaction
    {
        Friendly,
        Enemy
    }
}
