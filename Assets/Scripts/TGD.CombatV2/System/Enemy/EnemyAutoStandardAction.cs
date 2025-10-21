using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class EnemyAutoStandardAction : MonoBehaviour
    {
        public CombatActionManagerV2 actionManager;
        public TestEnemyStandardAction action;
        public TurnManagerV2 turnManager;
        [Tooltip("When true, uses the owner's current position as the action target.")]
        public bool useUnitPositionAsTarget = true;
        public Hex manualTarget;

        Coroutine _pending;

        void Awake()
        {
            if (!actionManager)
                actionManager = GetComponentInParent<CombatActionManagerV2>(true);
            if (!turnManager && actionManager != null)
                turnManager = actionManager.turnManager;
            if (!action)
                action = GetComponent<TestEnemyStandardAction>();
            if (!action && actionManager != null)
            {
                foreach (var tool in actionManager.tools)
                {
                    if (tool is TestEnemyStandardAction enemy)
                    {
                        action = enemy;
                        break;
                    }
                }
            }
        }

        void OnEnable()
        {
            if (turnManager != null)
                turnManager.TurnStarted += OnTurnStarted;
        }

        void OnDisable()
        {
            if (turnManager != null)
                turnManager.TurnStarted -= OnTurnStarted;
            if (_pending != null)
            {
                StopCoroutine(_pending);
                _pending = null;
            }
        }

        void OnTurnStarted(Unit unit)
        {
            if (!enabled || actionManager == null || action == null || turnManager == null)
                return;

            var owner = action.ResolveUnit();
            if (unit == null || owner == null || unit != owner)
                return;

            if (!turnManager.IsEnemyUnit(unit))
                return;

            if (_pending != null)
                StopCoroutine(_pending);

            _pending = StartCoroutine(RunAuto(unit));
        }

        IEnumerator RunAuto(Unit unit)
        {
            while (turnManager != null && !turnManager.HasUnitReachedIdle(unit))
                yield return null;

            var target = DetermineTarget(unit);
            actionManager?.ExecuteAutoAction(action, target);
            _pending = null;
        }

        Hex DetermineTarget(Unit unit)
        {
            if (!useUnitPositionAsTarget)
                return manualTarget;
            return unit != null ? unit.Position : manualTarget;
        }
    }
}
