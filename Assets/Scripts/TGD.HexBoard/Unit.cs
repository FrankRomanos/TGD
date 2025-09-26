namespace TGD.HexBoard
{
    /// <summary> ���� = ����֮һ����Q����R����</summary>
    public enum Facing4 { PlusQ, MinusQ, PlusR, MinusR }

    public sealed class Unit
    {
        public string Id;
        public Hex Position;    // ����������
        public Facing4 Facing;  // ǰ���ȡ ��Q/��R

        public Unit(string id, Hex pos, Facing4 facing)
        {
            Id = id; Position = pos; Facing = facing;
        }
    }
}