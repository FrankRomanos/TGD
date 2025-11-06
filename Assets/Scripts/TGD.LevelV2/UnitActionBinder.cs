using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using TGD.CombatV2;
using TGD.CoreV2;
using TGD.DataV2;
using UnityEngine;

namespace TGD.LevelV2
{
    /// <summary>
    /// Centralizes action availability wiring for units spawned by the factory.
    /// </summary>
    public static class UnitActionBinder
    {
        /// <summary>Data describing a single actionable entry exposed to UI.</summary>
        [Serializable]
        public struct ActionAvailability
        {
            public string actionId;
            public bool unlocked;
            public int initialCooldownSeconds;

            public ActionAvailability(string actionId, bool unlocked, int initialCooldownSeconds)
            {
                this.actionId = string.IsNullOrWhiteSpace(actionId) ? string.Empty : actionId.Trim();
                this.unlocked = unlocked;
                this.initialCooldownSeconds = Mathf.Max(0, initialCooldownSeconds);
            }
        }

        /// <summary>Interface consumed by UI layers to receive action metadata.</summary>
        public interface IActionProvider
        {
            event Action<IReadOnlyList<ActionAvailability>> AvailableActionsChanged;

            IReadOnlyList<ActionAvailability> GetAvailableActions();

            void SetAvailableActions(IReadOnlyList<ActionAvailability> actions);
        }

        sealed class UnitActionProviderBehaviour : MonoBehaviour, IActionProvider
        {
            readonly List<ActionAvailability> _actions = new();

            public event Action<IReadOnlyList<ActionAvailability>> AvailableActionsChanged;

            public IReadOnlyList<ActionAvailability> GetAvailableActions() => _actions;

            public void SetAvailableActions(IReadOnlyList<ActionAvailability> actions)
            {
                _actions.Clear();
                if (actions != null)
                    _actions.AddRange(actions);
                AvailableActionsChanged?.Invoke(_actions);
            }
        }

        public static void Bind(GameObject go, UnitRuntimeContext ctx, IEnumerable<FinalUnitConfig.LearnedAbility> learned, CombatActionManagerV2 cam)
        {
            _ = cam;

            if (go == null)
            {
                Debug.LogWarning("[ActionBinder] Cannot bind actions without a GameObject.");
                return;
            }

            var learnedList = learned != null
                ? new List<FinalUnitConfig.LearnedAbility>(learned)
                : new List<FinalUnitConfig.LearnedAbility>();

            var abilityLookup = BuildAbilityLookup(learnedList);

            string moveActionId = NormalizeActionId(ctx != null ? ctx.MoveActionId : MoveProfileRules.DefaultActionId)
                ?? MoveProfileRules.DefaultActionId;
            bool hasMove = abilityLookup.ContainsKey(moveActionId);
            ConfigureMovers(go, hasMove);

            string attackActionId = AttackProfileRules.DefaultActionId;
            bool hasAttack = abilityLookup.ContainsKey(attackActionId);
            ConfigureAttackers(go, hasAttack);

            var availabilities = BuildAvailabilities(learnedList, moveActionId, hasMove, attackActionId, hasAttack, abilityLookup);

            ctx?.SetLearnedActions(availabilities
                .Where(a => a.unlocked && !string.IsNullOrWhiteSpace(a.actionId))
                .Select(a => a.actionId));
            BroadcastToProviders(go, availabilities);

            LogActions(go, availabilities);
        }

        static Dictionary<string, FinalUnitConfig.LearnedAbility> BuildAbilityLookup(List<FinalUnitConfig.LearnedAbility> learned)
        {
            var lookup = new Dictionary<string, FinalUnitConfig.LearnedAbility>(StringComparer.OrdinalIgnoreCase);
            if (learned == null)
                return lookup;

            foreach (var ability in learned)
            {
                var id = NormalizeActionId(ability.actionId);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!lookup.ContainsKey(id))
                    lookup[id] = ability;
            }

            return lookup;
        }

        static List<ActionAvailability> BuildAvailabilities(
            List<FinalUnitConfig.LearnedAbility> learned,
            string moveActionId,
            bool hasMove,
            string attackActionId,
            bool hasAttack,
            Dictionary<string, FinalUnitConfig.LearnedAbility> lookup)
        {
            var list = new List<ActionAvailability>();

            if (!string.IsNullOrEmpty(moveActionId))
            {
                int cooldown = lookup.TryGetValue(moveActionId, out var entry)
                    ? Mathf.Max(0, entry.initialCooldownSeconds)
                    : 0;
                list.Add(new ActionAvailability(moveActionId, hasMove, cooldown));
            }

            if (!string.IsNullOrEmpty(attackActionId))
            {
                int cooldown = lookup.TryGetValue(attackActionId, out var entry)
                    ? Mathf.Max(0, entry.initialCooldownSeconds)
                    : 0;
                list.Add(new ActionAvailability(attackActionId, hasAttack, cooldown));
            }

            if (learned != null)
            {
                foreach (var ability in learned)
                {
                    var id = NormalizeActionId(ability.actionId);
                    if (string.IsNullOrEmpty(id))
                        continue;
                    if (!string.IsNullOrEmpty(moveActionId) && string.Equals(id, moveActionId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(attackActionId) && string.Equals(id, attackActionId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    list.Add(new ActionAvailability(id, true, Mathf.Max(0, ability.initialCooldownSeconds)));
                }
            }

            return list;
        }

        static void ConfigureMovers(GameObject go, bool hasMove)
        {
            var movers = go.GetComponentsInChildren<HexClickMover>(true);
            foreach (var mover in movers)
            {
                if (mover == null)
                    continue;

                mover.enabled = hasMove;
                if (!hasMove)
                    mover.driver = null;
            }
        }

        static void ConfigureAttackers(GameObject go, bool hasAttack)
        {
            var attackers = go.GetComponentsInChildren<AttackControllerV2>(true);
            foreach (var attack in attackers)
            {
                if (attack == null)
                    continue;

                attack.enabled = hasAttack;
                if (!hasAttack)
                    attack.driver = null;
            }
        }

        static void BroadcastToProviders(GameObject go, IReadOnlyList<ActionAvailability> actions)
        {
            bool any = false;
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour is IActionProvider provider)
                {
                    provider.SetAvailableActions(actions);
                    any = true;
                }
            }

            if (any)
                return;

            var fallback = go.GetComponent<UnitActionProviderBehaviour>();
            if (fallback == null)
                fallback = go.AddComponent<UnitActionProviderBehaviour>();
            fallback.SetAvailableActions(actions);
        }

        static void LogActions(GameObject go, List<ActionAvailability> actions)
        {
            if (go == null)
                return;

            if (actions == null || actions.Count == 0)
            {
                Debug.Log($"[ActionBinder] {go.name} actions -> (none)");
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < actions.Count; i++)
            {
                var entry = actions[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append(entry.actionId);
                sb.Append(entry.unlocked ? "[on]" : "[off]");
                sb.Append(" cd=");
                sb.Append(entry.initialCooldownSeconds);
            }

            Debug.Log($"[ActionBinder] {go.name} actions -> {sb}");
        }

        static string NormalizeActionId(string actionId)
        {
            return string.IsNullOrWhiteSpace(actionId) ? null : actionId.Trim();
        }
    }
}
