using System.Collections.Generic;
using TGD.CoreV2;
using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Lightweight lookup so gameplay/UI code can resolve portrait sprites for units
    /// without tight coupling between assemblies.
    /// </summary>
    public static class UnitAvatarRegistry
    {
        static readonly Dictionary<string, Sprite> Avatars = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnDomainReload()
        {
            Avatars.Clear();
        }

        public static bool Register(string unitId, Sprite avatar)
        {
            if (string.IsNullOrEmpty(unitId))
                return false;

            if (avatar == null)
            {
                Avatars.Remove(unitId);
                return false;
            }

            Avatars[unitId] = avatar;
            return true;
        }

        public static void Unregister(string unitId, Sprite expectedAvatar = null)
        {
            if (string.IsNullOrEmpty(unitId))
                return;

            if (!Avatars.TryGetValue(unitId, out var existing))
                return;

            if (expectedAvatar == null || existing == expectedAvatar)
                Avatars.Remove(unitId);
        }

        public static bool TryGetAvatar(Unit unit, out Sprite avatar)
        {
            string id = unit != null ? unit.Id : null;
            return TryGetAvatar(id, out avatar);
        }

        public static bool TryGetAvatar(string unitId, out Sprite avatar)
        {
            avatar = null;
            if (string.IsNullOrEmpty(unitId))
                return false;

            if (!Avatars.TryGetValue(unitId, out var stored) || stored == null)
                return false;

            avatar = stored;
            return true;
        }
    }
}
