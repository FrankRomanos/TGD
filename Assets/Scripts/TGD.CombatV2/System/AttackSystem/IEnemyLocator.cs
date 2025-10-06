// File: TGD.CombatV2/System/AttackSystem/IEnemyLocator.cs
using System.Collections.Generic;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public interface IEnemyLocator
    {
        bool IsEnemy(Hex hex);
        IEnumerable<Hex> AllEnemies { get; }
    }
}
