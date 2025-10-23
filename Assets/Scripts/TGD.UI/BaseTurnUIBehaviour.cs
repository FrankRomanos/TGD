// Assets/Scripts/TGD.UI/BaseTurnUiBehaviour.cs
using UnityEngine;
using TGD.Combat;
using TGD.CombatV2;
using Unit = TGD.HexBoard.Unit;

namespace TGD.UI
{
    /// <summary>
    /// 统一：查找 TurnManagerV2，订阅统一的 UnitRuntimeChanged 事件；仍保留 legacy CombatLoop 引用以兼容旧式执行入口。
    /// </summary>
    public abstract class BaseTurnUiBehaviour : MonoBehaviour
    {
        [Header("Combat (optional)")]
        [SerializeField] protected CombatLoop combat;

        [Header("Turn Manager (optional)")]
        [SerializeField] protected TurnManagerV2 turnManager;

        bool _pendingInitialSync;
        Unit _active;

        protected virtual void Awake()
        {
            if (!combat)
                combat = FindFirstObjectByTypeSafe<CombatLoop>();
            if (!turnManager)
                turnManager = FindFirstObjectByTypeSafe<TurnManagerV2>();
            _pendingInitialSync = true;
        }

        protected virtual void OnEnable()
        {
            RefreshReferences();
            SubscribeTurnManager(turnManager);
            TryInitialSync();
        }

        protected virtual void OnDisable()
        {
            SubscribeTurnManager(null);
        }

        protected virtual void LateUpdate()
        {
            RefreshReferences();
            TryInitialSync();
        }

        void RefreshReferences()
        {
            if (!combat)
                combat = FindFirstObjectByTypeSafe<CombatLoop>();
            var resolvedTm = turnManager ? turnManager : FindFirstObjectByTypeSafe<TurnManagerV2>();
            SubscribeTurnManager(resolvedTm);
        }

        void SubscribeTurnManager(TurnManagerV2 next)
        {
            if (ReferenceEquals(next, turnManager))
                return;

            if (turnManager != null)
                turnManager.UnitRuntimeChanged -= OnRuntimeChanged;

            turnManager = next;

            if (turnManager != null)
            {
                turnManager.UnitRuntimeChanged += OnRuntimeChanged;
                _pendingInitialSync = true;
            }
        }

        void TryInitialSync()
        {
            if (!_pendingInitialSync || turnManager == null)
                return;

            var cur = turnManager.ActiveUnit;
            if (cur != null)
            {
                _active = cur;
                HandleTurnBegan(cur);
                HandleRuntimeChanged(cur);
            }

            _pendingInitialSync = false;
        }

        void OnRuntimeChanged(Unit unit)
        {
            if (turnManager == null || unit == null)
                return;

            var active = turnManager.ActiveUnit;
            if (active != null && unit == active)
            {
                if (_active != active)
                {
                    _active = active;
                    HandleTurnBegan(active);
                }
                HandleRuntimeChanged(active);
                return;
            }

            if (_active != null && _active == unit && active != unit)
            {
                var ended = _active;
                _active = null;
                HandleTurnEnded(ended);
            }
            else if (_active != null && active == null && unit == _active)
            {
                var ended = _active;
                _active = null;
                HandleTurnEnded(ended);
            }
        }

        protected abstract void HandleTurnBegan(Unit u);
        protected abstract void HandleTurnEnded(Unit u);
        protected virtual void HandleRuntimeChanged(Unit u) { }

#if UNITY_2023_1_OR_NEWER
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindObjectOfType<T>();
#endif
    }
}
