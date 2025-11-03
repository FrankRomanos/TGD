using System;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.DataV2
{
    /// <summary>
    /// Scriptable blueprint describing the static data required to spawn a unit.
    /// </summary>
    [CreateAssetMenu(menuName = "TGD/Units/UnitBlueprint", fileName = "UnitBlueprint")]
    public sealed class UnitBlueprint : ScriptableObject
    {
        [Header("Identity")]
        public string unitId;
        public string displayName;
        public UnitFaction faction = UnitFaction.Friendly;

        [Header("Stats")]
        [Tooltip("Base StatsV2 snapshot copied to runtime on spawn.")]
        public StatsV2 baseStats = new StatsV2();

        [Header("Abilities")]
        [Tooltip("Initial ability loadout for this unit.")]
        public AbilitySlot[] abilities = new AbilitySlot[0];

        [Serializable]
        public struct AbilitySlot
        {
            public string actionId;
            public bool learned;
            [Min(0)]
            public int initialCooldownSeconds;
        }
    }
}
