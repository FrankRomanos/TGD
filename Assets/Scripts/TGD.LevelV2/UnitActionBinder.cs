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
            public string skillId;
            public bool unlocked;
            public int initialCooldownSeconds;

            public ActionAvailability(string skillId, bool unlocked, int initialCooldownSeconds)
            {
                this.skillId = string.IsNullOrWhiteSpace(skillId) ? string.Empty : skillId.Trim();
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

        public static IReadOnlyList<ActionAvailability> Bind(GameObject go, UnitRuntimeContext ctx, IEnumerable<FinalUnitConfig.LearnedAbility> learned)
        {
            if (go == null)
            {
                Debug.LogWarning("[ActionBinder] Cannot bind actions without a GameObject.");
                return Array.Empty<ActionAvailability>();
            }

            var learnedList = learned != null
                ? new List<FinalUnitConfig.LearnedAbility>(learned)
                : new List<FinalUnitConfig.LearnedAbility>();

            var abilityLookup = BuildAbilityLookup(learnedList);

            string moveSkillId = DetermineMoveSkillId(go, ctx);
            bool hasMove = abilityLookup.ContainsKey(moveSkillId);
            if (!hasMove && IsBuiltinMove(moveSkillId) && HasMoverComponent(go))
            {
                hasMove = true;
                Debug.LogWarning($"[ActionBinder] Missing learned entry for builtin move '{moveSkillId}'. Forcing enable.", go);
            }
            ConfigureMovers(go, hasMove);

            string attackSkillId = DetermineAttackSkillId(go);
            bool hasAttack = abilityLookup.ContainsKey(attackSkillId);
            if (!hasAttack && IsBuiltinAttack(attackSkillId) && HasAttackComponent(go))
            {
                hasAttack = true;
                Debug.LogWarning($"[ActionBinder] Missing learned entry for builtin attack '{attackSkillId}'. Forcing enable.", go);
            }
            ConfigureAttackers(go, hasAttack);

            var availabilities = BuildAvailabilities(learnedList, moveSkillId, hasMove, attackSkillId, hasAttack, abilityLookup);

            ctx?.SetLearnedActions(availabilities
                .Where(a => a.unlocked && !string.IsNullOrWhiteSpace(a.skillId))
                .Select(a => a.skillId));
            BroadcastToProviders(go, availabilities);

            LogActions(go, availabilities);
            return availabilities;
        }

        static Dictionary<string, FinalUnitConfig.LearnedAbility> BuildAbilityLookup(List<FinalUnitConfig.LearnedAbility> learned)
        {
            var lookup = new Dictionary<string, FinalUnitConfig.LearnedAbility>(StringComparer.OrdinalIgnoreCase);
            if (learned == null)
                return lookup;

            foreach (var ability in learned)
            {
                var id = NormalizeSkillId(ability.skillId);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!lookup.ContainsKey(id))
                    lookup[id] = ability;
            }

            return lookup;
        }

        static List<ActionAvailability> BuildAvailabilities(
            List<FinalUnitConfig.LearnedAbility> learned,
            string moveSkillId,
            bool hasMove,
            string attackSkillId,
            bool hasAttack,
            Dictionary<string, FinalUnitConfig.LearnedAbility> lookup)
        {
            var list = new List<ActionAvailability>();

            if (!string.IsNullOrEmpty(moveSkillId))
            {
                int cooldown = lookup.TryGetValue(moveSkillId, out var entry)
                    ? Mathf.Max(0, entry.initialCooldownSeconds)
                    : 0;
                list.Add(new ActionAvailability(moveSkillId, hasMove, cooldown));
            }

            if (!string.IsNullOrEmpty(attackSkillId))
            {
                int cooldown = lookup.TryGetValue(attackSkillId, out var entry)
                    ? Mathf.Max(0, entry.initialCooldownSeconds)
                    : 0;
                list.Add(new ActionAvailability(attackSkillId, hasAttack, cooldown));
            }

            if (learned != null)
            {
                foreach (var ability in learned)
                {
                    var id = NormalizeSkillId(ability.skillId);
                    if (string.IsNullOrEmpty(id))
                        continue;
                    if (!string.IsNullOrEmpty(moveSkillId) && string.Equals(id, moveSkillId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(attackSkillId) && string.Equals(id, attackSkillId, StringComparison.OrdinalIgnoreCase))
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
            }
        }

        static bool HasMoverComponent(GameObject go)
            => go != null && go.GetComponentInChildren<HexClickMover>(true) != null;

        static bool HasAttackComponent(GameObject go)
            => go != null && go.GetComponentInChildren<AttackControllerV2>(true) != null;

        static bool IsBuiltinMove(string skillId)
            => string.Equals(NormalizeSkillId(skillId), MoveProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase);

        static bool IsBuiltinAttack(string skillId)
            => string.Equals(NormalizeSkillId(skillId), AttackProfileRules.DefaultSkillId, StringComparison.OrdinalIgnoreCase);

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
                sb.Append(entry.skillId);
                sb.Append(entry.unlocked ? "[on]" : "[off]");
                sb.Append(" cd=");
                sb.Append(entry.initialCooldownSeconds);
            }

            Debug.Log($"[ActionBinder] {go.name} actions -> {sb}");
        }

        static string NormalizeSkillId(string skillId)
        {
            return string.IsNullOrWhiteSpace(skillId) ? null : skillId.Trim();
        }

        static string DetermineMoveSkillId(GameObject go, UnitRuntimeContext ctx)
        {
            var fromContext = NormalizeSkillId(ctx != null ? ctx.MoveSkillId : null);
            if (!string.IsNullOrEmpty(fromContext))
                return fromContext;

            if (go != null)
            {
                var mover = go.GetComponentInChildren<HexClickMover>(true);
                if (mover != null)
                {
                    var fromMover = NormalizeSkillId(mover.ResolveMoveSkillId());
                    if (!string.IsNullOrEmpty(fromMover))
                        return fromMover;
                }
            }

            return MoveProfileRules.DefaultSkillId;
        }

        static string DetermineAttackSkillId(GameObject go)
        {
            if (go != null)
            {
                var attack = go.GetComponentInChildren<AttackControllerV2>(true);
                if (attack != null)
                {
                    var resolved = NormalizeSkillId(attack.Id);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }
            }

            return AttackProfileRules.DefaultSkillId;
        }
    }
}
