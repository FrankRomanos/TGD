using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Registry that maps unit identifiers to their visible transforms.
    /// </summary>
    public static class UnitLocator
    {
        static readonly Dictionary<string, Transform> Registry = new();

        public static bool Register(IUnitView view)
        {
            if (view == null)
                return false;

            var id = view.UnitId;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[UnitLocator] Attempted to register a view without a UnitId.");
                return false;
            }

            var transform = ResolveTransform(view);
            if (transform == null)
            {
                Debug.LogWarning($"[UnitLocator] View '{id}' has no Transform to register.");
                return false;
            }

            Registry[id] = transform;
            return true;
        }

        public static void Unregister(IUnitView view)
        {
            if (view == null)
                return;

            var id = view.UnitId;
            if (string.IsNullOrEmpty(id))
                return;

            if (Registry.TryGetValue(id, out var existing))
            {
                var transform = ResolveTransform(view);
                if (transform == null || transform == existing)
                    Registry.Remove(id);
            }
        }

        public static bool TryGetTransform(string unitId, out Transform transform)
        {
            if (!string.IsNullOrEmpty(unitId) && Registry.TryGetValue(unitId, out var existing) && existing)
            {
                transform = existing;
                return true;
            }

            transform = null;
            return false;
        }

        static Transform ResolveTransform(IUnitView view)
        {
            if (view == null)
                return null;

            var transform = view.ViewTransform;
            if (transform != null)
                return transform;

            if (view is Component component)
                return component.transform;

            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Logs the current registry content for debugging within the editor.
        /// </summary>
        public static void Dump()
        {
            if (Registry.Count == 0)
            {
                Debug.Log("[UnitLocator] Registry is empty.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[UnitLocator] Registered unit views:");
            foreach (var kvp in Registry)
            {
                var t = kvp.Value;
                sb.Append(" - ")
                  .Append(kvp.Key)
                  .Append(": ")
                  .AppendLine(t != null ? t.name : "<missing transform>");
            }

            Debug.Log(sb.ToString());
        }
#endif
    }
}
