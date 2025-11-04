using TGD.CoreV2;
namespace TGD.HexBoard.Path
{
    public interface IPassability
    {
        bool IsBlocked(Hex h);
    }
}
