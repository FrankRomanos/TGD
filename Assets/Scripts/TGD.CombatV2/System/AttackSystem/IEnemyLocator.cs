// File: TGD.CombatV2/System/AttackSystem/IEnemyLocator.cs
using System.Collections.Generic;
using TGD.CoreV2;

namespace TGD.CombatV2
{
    public interface IEnemyLocator
    {
        bool IsEnemy(Hex hex);
        IEnumerable<Hex> AllEnemies { get; }
    }
}
