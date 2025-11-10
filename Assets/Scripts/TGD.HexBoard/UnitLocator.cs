// UnitLocator.cs
using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    public static class UnitLocator
    {
        static readonly Dictionary<string, Transform> Registry = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            UnitViewHandle.ViewEnabled -= HandleViewEnabled;
            UnitViewHandle.ViewDisabled -= HandleViewDisabled;
            UnitViewHandle.ViewEnabled += HandleViewEnabled;
            UnitViewHandle.ViewDisabled += HandleViewDisabled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnDomainReload()
        {
            Registry.Clear();
        }

        public static bool Register(IUnitView view)
        {
            if (view == null) return false;
            var id = view.UnitId;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[UnitLocator] View without UnitId.");
                return false;
            }

            var t = ResolveTransform(view);
            if (t == null)
            {
                Debug.LogWarning($"[UnitLocator] '{id}' has no Transform.");
                return false;
            }

            Registry[id] = t;
            return true;
        }

        public static void Unregister(IUnitView view)
        {
            if (view == null) return;
            var id = view.UnitId;
            if (string.IsNullOrEmpty(id)) return;

            if (Registry.TryGetValue(id, out var existing))
            {
                var t = ResolveTransform(view);
                if (t == null || t == existing) Registry.Remove(id);
            }
        }

        public static bool TryGetTransform(string unitId, out Transform transform)
        {
            if (!string.IsNullOrEmpty(unitId) &&
                Registry.TryGetValue(unitId, out var t) && t)
            {
                transform = t;
                return true;
            }
            transform = null;
            return false;
        }

        static Transform ResolveTransform(IUnitView view)
        {
            if (view == null) return null;
            var t = view.ViewTransform;
            if (t != null) return t;

            if (view is Component c) return c.transform;
            return null;
        }

        static void HandleViewEnabled(IUnitView view)
        {
            Register(view);
        }

        static void HandleViewDisabled(IUnitView view)
        {
            Unregister(view);
        }
    }
}
