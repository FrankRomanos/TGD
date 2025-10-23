using System.Collections;
using UnityEngine;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public sealed class TestEnemyAutoActionDriver : MonoBehaviour
    {
        public CombatActionManagerV2 actionManager;
        public TurnManagerV2 turnManager;
        public ChainTestActionBase action;
        public HexBoardTestDriver driver;
        [Tooltip("Optional delay before auto confirming after idle (seconds).")]
        public float confirmDelaySeconds = 0f;

        Coroutine _pendingRoutine;

        void Awake()
        {
            if (driver == null)
                driver = GetComponentInParent<HexBoardTestDriver>(true);
            if (action == null)
                action = GetComponent<ChainTestActionBase>();
            if (actionManager == null)
                actionManager = FindFirstObjectByType<CombatActionManagerV2>();
            if (turnManager == null)
            {
                if (actionManager != null && actionManager.turnManager != null)
                    turnManager = actionManager.turnManager;
                else
                    turnManager = FindFirstObjectByType<TurnManagerV2>();
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
            if (_pendingRoutine != null)
            {
                StopCoroutine(_pendingRoutine);
                _pendingRoutine = null;
            }
        }

        void OnTurnStarted(Unit unit)
        {
            if (driver == null || driver.UnitRef == null || unit != driver.UnitRef)
                return;

            if (_pendingRoutine != null)
                StopCoroutine(_pendingRoutine);

            _pendingRoutine = StartCoroutine(RunAutoAction(unit));
        }

        IEnumerator RunAutoAction(Unit unit)
        {
            if (turnManager == null || actionManager == null || action == null)
            {
                _pendingRoutine = null;
                yield break;
            }

            while (!turnManager.HasReachedIdle(unit))
                yield return null;

            if (confirmDelaySeconds > 0f)
                yield return new WaitForSeconds(confirmDelaySeconds);

            var target = unit.Position;
            if (!actionManager.TryAutoExecuteAction(action.Id, target))
            {
                _pendingRoutine = null;
                yield break;
            }

            yield return null;
            while (actionManager.IsExecuting)
                yield return null;

            //turnManager.EndTurn(unit); // 仅用作早期定位

            _pendingRoutine = null;
        }
    }
}
