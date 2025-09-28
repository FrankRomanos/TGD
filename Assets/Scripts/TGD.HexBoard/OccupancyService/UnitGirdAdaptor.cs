// File: TGD.HexBoard/UnitGridAdapter.cs
namespace TGD.HexBoard
{
    /// 轻量适配器：把现有 Unit 挂接到规则层
    public sealed class UnitGridAdapter : IGridActor
    {
        readonly Unit unit;
        readonly FootprintShape footprint;

        public UnitGridAdapter(Unit u, FootprintShape fp) { unit = u; footprint = fp; }

        public string Id => unit.Id;
        public Hex Anchor { get => unit.Position; set => unit.Position = value; }
        public Facing4 Facing { get => unit.Facing; set => unit.Facing = value; }
        public FootprintShape Footprint => footprint;
    }
}
