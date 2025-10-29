using System.Collections.Generic;
using UnityEngine;

namespace TGD.UI
{
    /// <summary>
    /// Centralized registry that maps unit identifiers to portrait sprites for the turn timeline UI.
    /// </summary>
    public static class TurnTimelineAvatarRegistry
    {
        static readonly Dictionary<string, Sprite> Registry = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnReload()
        {
            Registry.Clear();
        }

        /// <summary>
        /// Assigns a portrait sprite to the specified unit id. Passing a null sprite removes the entry.
        /// </summary>
        public static void SetAvatar(string unitId, Sprite sprite)
        {
            if (string.IsNullOrEmpty(unitId))
                return;

            if (sprite)
                Registry[unitId] = sprite;
            else
                Registry.Remove(unitId);
        }

        /// <summary>
        /// Removes the avatar mapping for the given unit id.
        /// </summary>
        public static void RemoveAvatar(string unitId, Sprite expectedSprite = null)
        {
            if (string.IsNullOrEmpty(unitId))
                return;

            if (!Registry.TryGetValue(unitId, out var existing))
                return;

            if (expectedSprite != null && existing != expectedSprite)
                return;

            Registry.Remove(unitId);
        }

        /// <summary>
        /// Attempts to retrieve the portrait sprite bound to a unit id.
        /// </summary>
        public static bool TryGetAvatar(string unitId, out Sprite sprite)
        {
            if (!string.IsNullOrEmpty(unitId) && Registry.TryGetValue(unitId, out sprite) && sprite)
                return true;

            sprite = null;
            return false;
        }
    }
}
