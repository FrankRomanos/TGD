// Assets/Scripts/TGD.CombatV2/Runtime/ICooldownKeyProvider.cs
namespace TGD.CombatV2
{
    /// 谁来都行，给我一个用于冷却系统的唯一键
    public interface ICooldownKeyProvider
    {
        string CooldownKey { get; }
    }
}
