using TGD.CoreV2;
using UnityEngine;

// File: TGD.HexBoard/UnitGridAdapter.cs
namespace TGD.HexBoard
{
    [DisallowMultipleComponent]
    public sealed class UnitGridAdapter : MonoBehaviour, IGridActor
    {
        [SerializeField]
        Unit unit;

        [SerializeField]
        FootprintShape footprint;

        Hex _fallbackAnchor = Hex.Zero;
        Facing4 _fallbackFacing = Facing4.PlusQ;

        public string Id => unit != null ? unit.Id : name;

        public Unit Unit
        {
            get => unit;
            set => unit = value;
        }

        public FootprintShape Footprint
        {
            get => footprint;
            set => footprint = value;
        }

        public Hex Anchor
        {
            get
            {
                if (unit != null)
                    return unit.Position;
                return _fallbackAnchor;
            }
            set
            {
                if (unit != null)
                    unit.Position = value;
                _fallbackAnchor = value;
            }
        }

        public Facing4 Facing
        {
            get
            {
                if (unit != null)
                    return unit.Facing;
                return _fallbackFacing;
            }
            set
            {
                if (unit != null)
                    unit.Facing = value;
                _fallbackFacing = value;
            }
        }
    }
}
