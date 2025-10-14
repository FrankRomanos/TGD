// File: TGD.CombatV2/System/AttackSystem/IAllegianceProvider.cs
using System;
using TGD.HexBoard;

namespace TGD.CombatV2
{
    public interface IAllegianceProvider
    {
        bool TryGetAllegiance(Unit unit, out string allegianceId);
        bool AreAllies(Unit source, Unit target);
        bool AreEnemies(Unit source, Unit target);
    }

    public sealed class DefaultAllegianceProvider : IAllegianceProvider
    {
        readonly Func<Unit> _selfGetter;
        readonly Func<TurnManagerV2> _turnManagerGetter;
        readonly Func<IEnemyLocator> _enemyLocatorGetter;
        readonly string _playerToken;
        readonly string _enemyToken;

        public DefaultAllegianceProvider(
            Func<Unit> selfGetter,
            Func<TurnManagerV2> turnManagerGetter,
            Func<IEnemyLocator> enemyLocatorGetter,
            string playerToken = "Player",
            string enemyToken = "Enemy")
        {
            _selfGetter = selfGetter ?? (() => null);
            _turnManagerGetter = turnManagerGetter ?? (() => null);
            _enemyLocatorGetter = enemyLocatorGetter ?? (() => null);
            _playerToken = string.IsNullOrEmpty(playerToken) ? "Player" : playerToken;
            _enemyToken = string.IsNullOrEmpty(enemyToken) ? "Enemy" : enemyToken;
        }

        public bool TryGetAllegiance(Unit unit, out string allegianceId)
        {
            allegianceId = null;
            if (unit == null)
                return false;

            var self = _selfGetter();

            var tm = _turnManagerGetter();
            if (tm != null)
            {
                if (tm.IsPlayerUnit(unit))
                {
                    allegianceId = _playerToken;
                    return true;
                }
                if (tm.IsEnemyUnit(unit))
                {
                    allegianceId = _enemyToken;
                    return true;
                }
            }

            var locator = _enemyLocatorGetter();
            if (locator != null)
            {
                bool selfKnown = self != null;
                bool selfFlag = false;
                if (selfKnown)
                    selfFlag = locator.IsEnemy(self.Position);

                if (selfKnown && ReferenceEquals(unit, self))
                {
                    allegianceId = selfFlag ? _enemyToken : _playerToken;
                    return true;
                }

                bool targetFlag = locator.IsEnemy(unit.Position);
                if (selfKnown)
                {
                    bool sameSide = targetFlag == selfFlag;
                    allegianceId = sameSide
                        ? (selfFlag ? _enemyToken : _playerToken)
                        : (selfFlag ? _playerToken : _enemyToken);
                    return true;
                }

                allegianceId = targetFlag ? _enemyToken : _playerToken;
                return true;
            }

            if (self != null && ReferenceEquals(unit, self))
            {
                allegianceId = _playerToken;
                return true;
            }

            return false;
        }

        public bool AreAllies(Unit source, Unit target)
        {
            if (source == null || target == null)
                return false;
            if (ReferenceEquals(source, target))
                return true;

            if (TryGetAllegiance(source, out var s) && TryGetAllegiance(target, out var t) &&
                !string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(t))
                return string.Equals(s, t, StringComparison.Ordinal);

            var locator = _enemyLocatorGetter();
            if (locator != null)
            {
                bool sourceEnemy = locator.IsEnemy(source.Position);
                bool targetEnemy = locator.IsEnemy(target.Position);
                return sourceEnemy == targetEnemy;
            }

            return false;
        }

        public bool AreEnemies(Unit source, Unit target)
        {
            if (source == null || target == null)
                return false;
            if (ReferenceEquals(source, target))
                return false;

            if (TryGetAllegiance(source, out var s) && TryGetAllegiance(target, out var t) &&
                !string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(t))
                return !string.Equals(s, t, StringComparison.Ordinal);

            var locator = _enemyLocatorGetter();
            if (locator != null)
            {
                bool sourceEnemy = locator.IsEnemy(source.Position);
                bool targetEnemy = locator.IsEnemy(target.Position);
                return sourceEnemy != targetEnemy;
            }

            return false;
        }
    }
}

