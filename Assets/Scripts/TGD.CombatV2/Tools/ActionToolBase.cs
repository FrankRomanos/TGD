using UnityEngine;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    [DisallowMultipleComponent]
    public abstract class ActionToolBase : MonoBehaviour, IToolOwner, IBindContext
    {
        [SerializeField]
        protected UnitRuntimeContext ctx;

        bool _subscribed;

        public UnitRuntimeContext Ctx => ctx;

        public virtual void Bind(UnitRuntimeContext context, TurnManagerV2 tm = null)
        {
            Bind(context);
        }

        public void Bind(UnitRuntimeContext context)
        {
            ctx = context;
        }

        public virtual void BindContext(UnitRuntimeContext context, TurnManagerV2 turnManager)
        {
            Bind(context, turnManager);
        }

        protected Unit OwnerUnit => ctx != null ? ctx.boundUnit : null;

        protected Unit ResolveSelfUnit() => OwnerUnit;

        protected virtual void OnEnable()
        {
            if (!Application.isPlaying || _subscribed)
                return;

            HookEvents(true);
            _subscribed = true;
        }

        protected virtual void OnDisable()
        {
            if (!Application.isPlaying || !_subscribed)
                return;

            HookEvents(false);
            _subscribed = false;
        }

        protected virtual void OnDestroy()
        {
            if (!Application.isPlaying)
                return;

            if (_subscribed)
            {
                HookEvents(false);
                _subscribed = false;
            }
        }

        protected abstract void HookEvents(bool bind);

        protected static bool Dead(Object o) => o == null || o.Equals(null);

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (ctx == null)
                ctx = GetComponentInParent<UnitRuntimeContext>(true);
        }
#endif
    }
}
