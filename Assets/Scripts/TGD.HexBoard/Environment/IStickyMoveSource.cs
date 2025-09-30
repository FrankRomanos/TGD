// File: TGD.HexBoard/IStickyMoveSource.cs
namespace TGD.HexBoard
{
    /// �ṩ������ĳ��ʱʩ��������٣����ٻ���٣�������Ϣ��
    /// ���� multiplier (>1=����, <1=����), durationTurns��<0=���ã�0=�����ţ�
    public interface IStickyMoveSource
    {
        bool TryGetSticky(Hex cell, out float multiplier, out int durationTurns);
    }
}

