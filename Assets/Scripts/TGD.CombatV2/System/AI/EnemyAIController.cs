using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TGD.CoreV2;
using TGD.HexBoard;

namespace TGD.CombatV2.AI
{
    [DisallowMultipleComponent]
    public sealed class EnemyAIController : MonoBehaviour
    {
        [Tooltip("Optional override for the runtime context associated with this unit.")]
        public UnitRuntimeContext context;

        [Tooltip("Optional override for the turn manager driving combat.")]
        public TurnManagerV2 turnManager;

        [Tooltip("Optional override for the combat action manager executing skills.")]
        public CombatActionManagerV2 actionManager;

        [Tooltip("Delay (seconds) before the auto action is confirmed once the unit is idle.")]
        public float confirmDelaySeconds = 0.1f;

        Coroutine _turnRoutine;
        bool _subscribed;

        void Awake()
        {
            if (context == null)
                context = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>();
        }

        void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            if (_turnRoutine != null)
            {
                StopCoroutine(_turnRoutine);
                _turnRoutine = null;
            }
        }

        void ResolveDependencies()
        {
            if (actionManager == null)
                actionManager = FindOne<CombatActionManagerV2>();

            if (turnManager == null)
            {
                if (actionManager != null && actionManager.turnManager != null)
                    turnManager = actionManager.turnManager;
                else
                    turnManager = FindOne<TurnManagerV2>();
            }

            if (context == null)
                context = GetComponent<UnitRuntimeContext>() ?? GetComponentInParent<UnitRuntimeContext>();
        }

        void Subscribe()
        {
            if (_subscribed || turnManager == null)
                return;

            turnManager.TurnStarted += HandleTurnStarted;
            turnManager.TurnEnded += HandleTurnEnded;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (turnManager != null)
            {
                turnManager.TurnStarted -= HandleTurnStarted;
                turnManager.TurnEnded -= HandleTurnEnded;
            }

            _subscribed = false;
        }

        void HandleTurnStarted(Unit unit)
        {
            if (!IsControlledUnit(unit))
                return;

            if (_turnRoutine != null)
            {
                StopCoroutine(_turnRoutine);
                _turnRoutine = null;
            }

            _turnRoutine = StartCoroutine(RunTurn(unit));
        }

        void HandleTurnEnded(Unit unit)
        {
            if (!IsControlledUnit(unit))
                return;

            if (_turnRoutine != null)
            {
                StopCoroutine(_turnRoutine);
                _turnRoutine = null;
            }
        }

        bool IsControlledUnit(Unit unit)
        {
            var bound = context != null ? context.boundUnit : null;
            if (unit == null || bound == null || unit != bound)
                return false;

            if (turnManager != null && !turnManager.IsEnemyUnit(unit))
                return false;

            return true;
        }

        IEnumerator RunTurn(Unit unit)
        {
            if (turnManager == null || actionManager == null || context == null)
            {
                _turnRoutine = null;
                yield break;
            }

            while (!turnManager.HasReachedIdle(unit))
                yield return null;

            if (confirmDelaySeconds > 0f)
                yield return new WaitForSeconds(confirmDelaySeconds);

            string skillId = SelectSkillId();
            if (string.IsNullOrEmpty(skillId))
            {
                Debug.LogWarning($"[EnemyAI] {name} has no ready skill to execute.", this);
                _turnRoutine = null;
                yield break;
            }

            var target = DetermineTarget(unit);

            if (!actionManager.TryAutoExecuteActionForUnit(unit, skillId, target))
            {
                Debug.LogWarning($"[EnemyAI] Auto execution failed for {skillId}.", this);
                _turnRoutine = null;
                yield break;
            }

            Debug.Log($"[EnemyAI] Unit {unit.Id} auto-casts {skillId} at {target}.", this);

            yield return null;
            while (actionManager.IsExecuting)
                yield return null;

            _turnRoutine = null;
        }

        string SelectSkillId()
        {
            var learned = context?.LearnedActions;
            if (learned == null || learned.Count == 0)
                return null;

            for (int i = 0; i < learned.Count; i++)
            {
                var id = learned[i];
                if (IsSkillReady(id))
                    return id;
            }

            return learned[0];
        }

        bool IsSkillReady(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return false;

            var hub = context?.cooldownHub;
            if (hub == null || hub.secStore == null)
                return true;

            return hub.secStore.Ready(skillId);
        }

        Hex DetermineTarget(Unit unit)
        {
            if (unit != null)
                return unit.Position;

            var bound = context != null ? context.boundUnit : null;
            return bound != null ? bound.Position : Hex.Zero;
        }

        static T FindOne<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<T>(true);
#endif
        }
    }
}
