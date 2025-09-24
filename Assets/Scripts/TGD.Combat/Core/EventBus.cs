// Assets/Scripts/TGD.Combat/Core/EventBus.cs
using System;
using UnityEngine;         // Vector2Int
using TGD.Data;           // Unit / DamageSchool
using TGD.Grid;

namespace TGD.Combat
{
    /// <summary>
    /// ������״̬�¼����壬����ֱ����ϵ������ StatusInstance ���͡�
    /// </summary>
    public readonly struct StatusEvent
    {
        public readonly Unit Target;
        public readonly Unit Source;
        public readonly string StatusSkillId;
        public readonly int Stacks;
        public readonly int RemainSeconds;

        public StatusEvent(Unit target, Unit source, string statusSkillId, int stacks, int remainSeconds)
        {
            Target = target;
            Source = source;
            StatusSkillId = statusSkillId;
            Stacks = stacks;
            RemainSeconds = remainSeconds;
        }
    }

    public interface ICombatEventBus
    {
        // ��ֵ���㣺�����ߡ�Ŀ�ꡢ�������ֵ���Ƿ�DoT���˺�ѧ�ɡ��¼�ʱ��(�����)
        event Action<Unit, Unit, float, bool, DamageSchool, float> OnDamageResolved;

        // �غϹ���
        event Action<Unit> OnTurnBegin;
        event Action<Unit> OnTurnEnd;



        // ״̬���
        event Action<Unit, HexCoord, HexCoord> OnUnitPositionChanged;
        event Action<StatusEvent> OnStatusApplied;
        event Action<StatusEvent> OnStatusExpired;
        event Action<StatusEvent> OnStatusDispelled;
        void EmitDamageResolved(Unit atk, Unit tgt, float amountPost, bool isDot, DamageSchool school, float t);

        void EmitTurnBegin(Unit u);
        void EmitTurnEnd(Unit u);

        void EmitUnitPositionChanged(Unit u, HexCoord from, HexCoord to);

        void EmitStatusApplied(in StatusEvent e);
        void EmitStatusExpired(in StatusEvent e);
        void EmitStatusDispelled(in StatusEvent e);
    }

    /// <summary>
    /// ���¼�����ʵ�֣�ֱ��ת���¼�����
    /// </summary>
    public sealed class CombatEventBus : ICombatEventBus
    {
        public event Action<Unit, Unit, float, bool, DamageSchool, float> OnDamageResolved;
        public event Action<Unit> OnTurnBegin;
        public event Action<Unit> OnTurnEnd;
        public event Action<Unit, HexCoord, HexCoord> OnUnitPositionChanged;
        public event Action<StatusEvent> OnStatusApplied;
        public event Action<StatusEvent> OnStatusExpired;
        public event Action<StatusEvent> OnStatusDispelled;

        // ��ݴ���������ϵͳ�ڲ����ã�
        public void EmitDamageResolved(Unit atk, Unit tgt, float amountPost, bool isDot, DamageSchool school, float t)
            => OnDamageResolved?.Invoke(atk, tgt, amountPost, isDot, school, t);

        public void EmitTurnBegin(Unit u) => OnTurnBegin?.Invoke(u);
        public void EmitTurnEnd(Unit u) => OnTurnEnd?.Invoke(u);

        public void EmitUnitPositionChanged(Unit u, HexCoord from, HexCoord to)
             => OnUnitPositionChanged?.Invoke(u, from, to);

        public void EmitStatusApplied(in StatusEvent e) => OnStatusApplied?.Invoke(e);
        public void EmitStatusExpired(in StatusEvent e) => OnStatusExpired?.Invoke(e);
        public void EmitStatusDispelled(in StatusEvent e) => OnStatusDispelled?.Invoke(e);
    }
}
