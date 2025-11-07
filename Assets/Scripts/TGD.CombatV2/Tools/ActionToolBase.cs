using UnityEngine;

namespace TGD.CombatV2
{
    public abstract class ActionToolBase : MonoBehaviour
    {
        bool _subscribed;

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
    }
}
