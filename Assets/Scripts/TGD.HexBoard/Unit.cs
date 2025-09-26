namespace TGD.HexBoard
{
    /// <summary> 面向 = 四向之一（±Q、±R）。</summary>
    public enum Facing4 { PlusQ, MinusQ, PlusR, MinusR }

    public sealed class Unit
    {
        public string Id;
        public Hex Position;    // 总是整数格
        public Facing4 Facing;  // 前向仅取 ±Q/±R

        public Unit(string id, Hex pos, Facing4 facing)
        {
            Id = id; Position = pos; Facing = facing;
        }
    }
}