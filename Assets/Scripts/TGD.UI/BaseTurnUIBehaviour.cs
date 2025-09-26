// Assets/Scripts/TGD.UI/BaseTurnUiBehaviour.cs
using UnityEngine;
using TGD.Combat;

namespace TGD.UI
{
    /// <summary>
    /// 统一：查找 CombatLoop，订阅战斗总线的回合事件，启用时若已有激活单位则补一次回调。
    /// </summary>
    public abstract class BaseTurnUiBehaviour : MonoBehaviour
    {
        [Header("Combat (optional)")]
        [SerializeField] protected CombatLoop combat;

        ICombatEventBus _eventBus;
        bool _pendingInitialSync;

        protected virtual void Awake()
        {
            if (!combat)
                combat = FindFirstObjectByTypeSafe<CombatLoop>();
            _pendingInitialSync = true;
        }

        protected virtual void OnEnable()
        {
            RefreshCombatReference();
            TryInitialSync();
        }

        protected virtual void OnDisable()
        {
            SubscribeEventBus(null);
        }

        protected virtual void LateUpdate()
        {
            RefreshCombatReference();
            TryInitialSync();
        }

        void RefreshCombatReference()
        {
            if (!combat)
                combat = FindFirstObjectByTypeSafe<CombatLoop>();

            SubscribeEventBus(combat ? combat.EventBus : null);
        }

        void SubscribeEventBus(ICombatEventBus next)
        {
            if (ReferenceEquals(next, _eventBus))
                return;

            if (_eventBus != null)
            {
                _eventBus.OnTurnBegin -= OnTurnBeginEvent;
                _eventBus.OnTurnEnd -= OnTurnEndEvent;
            }

            _eventBus = next;

            if (_eventBus != null)
            {
                _eventBus.OnTurnBegin += OnTurnBeginEvent;
                _eventBus.OnTurnEnd += OnTurnEndEvent;
                _pendingInitialSync = true;
            }
        }

        void TryInitialSync()
        {
            if (!_pendingInitialSync || combat == null)
                return;

            var cur = combat.GetActiveUnit();
            if (cur != null)
                HandleTurnBegan(cur);

            _pendingInitialSync = false;
        }

        void OnTurnBeginEvent(Unit unit)
        {
            if (unit == null)
                return;
            HandleTurnBegan(unit);
        }

        void OnTurnEndEvent(Unit unit)
        {
            if (unit == null)
                return;
            HandleTurnEnded(unit);
        }

        protected abstract void HandleTurnBegan(Unit u);
        protected abstract void HandleTurnEnded(Unit u);

#if UNITY_2023_1_OR_NEWER
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
        protected static T FindFirstObjectByTypeSafe<T>() where T : Object
            => Object.FindObjectOfType<T>();
#endif
    }
}
