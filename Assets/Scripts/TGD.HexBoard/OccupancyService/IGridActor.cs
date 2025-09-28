// File: TGD.HexBoard/IGridActor.cs
namespace TGD.HexBoard
{
    /// ���κΡ���λ�����䵽���Ӳ���Ҫ����С��Ϣ
    public interface IGridActor
    {
        string Id { get; }
        Hex Anchor { get; set; }   // ռλê�㣨������
        Facing4 Facing { get; set; }   // �� ��Q / ��R
        FootprintShape Footprint { get; } // ����ռλ��SO��
    }
}
