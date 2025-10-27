using UnityEngine;

namespace TGD.HexBoard
{
    /// <summary>
    /// Interface implemented by view objects that expose a unit's visible transform.
    /// </summary>
    public interface IUnitView
    {
        /// <summary>Unique identifier that matches the logical unit.</summary>
        string UnitId { get; }

        /// <summary>Transform that represents the unit in the world.</summary>
        Transform ViewTransform { get; }
    }
}
