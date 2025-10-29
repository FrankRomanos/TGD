using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Provides an optional avatar sprite for a unit so UI layers can display portraits
    /// without knowing about the concrete view implementation.
    /// </summary>
    public interface IUnitAvatarSource
    {
        /// <summary>Unique identifier that matches the logical unit.</summary>
        string UnitId { get; }

        /// <summary>Returns the sprite that should be used as the avatar, or null when unavailable.</summary>
        Sprite GetAvatarSprite();
    }
}

