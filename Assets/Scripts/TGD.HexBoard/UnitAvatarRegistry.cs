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
        static readonly Dictionary<string, IUnitAvatarSource> Sources = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnDomainReload()
        {
            Sources.Clear();
        }

        public static bool Register(IUnitAvatarSource source)
        {
            if (source == null)
                return false;

            string id = source.UnitId;
            if (string.IsNullOrEmpty(id))
                return false;

            Sources[id] = source;
            return true;
        }

        public static void Unregister(IUnitAvatarSource source)
        {
            if (source == null)
                return;

            string id = source.UnitId;
            if (string.IsNullOrEmpty(id))
                return;

            if (Sources.TryGetValue(id, out var existing) && ReferenceEquals(existing, source))
                Sources.Remove(id);
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

            if (!Sources.TryGetValue(unitId, out var source) || source == null)
                return false;

            avatar = source.GetAvatarSprite();
            return avatar != null;
        }
    }
}

