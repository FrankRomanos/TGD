using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.DataV2;

namespace TGD.LevelV2
{
    public sealed class UnitFactorySandbox : MonoBehaviour
    {
        public UnitFactory factory;
        public UnitBlueprint[] party = new UnitBlueprint[4];
        public UnitBlueprint[] enemies = new UnitBlueprint[2];
        public Hex[] partySpawns = new Hex[4];
        public Hex[] enemySpawns = new Hex[2];
        public bool autoSpawnOnPlay = true;

        readonly List<Unit> _spawnedUnits = new();

        void Start()
        {
            if (autoSpawnOnPlay)
                SpawnTestParty();
        }

        [ContextMenu("SpawnTestParty")]
        public void SpawnTestParty()
        {
            ClearAll();
            if (factory == null)
            {
                Debug.LogError("[Sandbox] UnitFactory reference is missing.", this);
                return;
            }

            for (int i = 0; i < party.Length && i < partySpawns.Length; i++)
            {
                if (party[i] == null)
                    continue;
                var unit = factory.Spawn(party[i], UnitFaction.Friendly, partySpawns[i]);
                if (unit != null)
                    _spawnedUnits.Add(unit);
            }

            for (int i = 0; i < enemies.Length && i < enemySpawns.Length; i++)
            {
                if (enemies[i] == null)
                    continue;
                var unit = factory.Spawn(enemies[i], UnitFaction.Enemy, enemySpawns[i]);
                if (unit != null)
                    _spawnedUnits.Add(unit);
            }

            factory.StartBattle();
        }

        [ContextMenu("ClearAll")]
        public void ClearAll()
        {
            if (factory == null)
            {
                _spawnedUnits.Clear();
                return;
            }

            var snapshot = new List<Unit>(_spawnedUnits);
            foreach (var unit in snapshot)
            {
                if (unit != null)
                    factory.Despawn(unit);
            }

            _spawnedUnits.Clear();
        }
    }
}
