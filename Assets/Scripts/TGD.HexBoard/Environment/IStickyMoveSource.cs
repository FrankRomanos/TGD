// File: TGD.HexBoard/IStickyMoveSource.cs
namespace TGD.HexBoard
{
    public interface IStickyMoveSource
    {
        /// <summary>
        /// �ڸ��� at �Ƿ��ṩ�������ٷֱȳ��� + �����غ� + ȥ��tag����
        /// - multiplier: ���� 0.8f��1.2f
        /// - durationTurns: <0=���ã�>=0=���غ�
        /// - tag: ͬԴȥ��Key������ "Patch@8,6"��"Hazard@AcidPool@14,3"��
        /// </summary>
        bool TryGetSticky(Hex at, out float multiplier, out int durationTurns, out string tag);
    }
}

