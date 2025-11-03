// File: TGD.HexBoard/UnitGridAdapter.cs
namespace TGD.HexBoard
{
    public sealed class UnitGridAdapter : IGridActor
    {
        readonly Unit unit;
        readonly FootprintShape footprint;

        public UnitGridAdapter(Unit u, FootprintShape fp) { unit = u; footprint = fp; }

        public string Id => unit.Id;
        public Unit Unit => unit;
        public Hex Anchor { get => unit.Position; set => unit.Position = value; }
        public Facing4 Facing { get => unit.Facing; set => unit.Facing = value; }
        public FootprintShape Footprint => footprint;
    }
}
