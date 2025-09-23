// Assets/Scripts/TGD.Combat/Core/EventBus.cs
using System;
using UnityEngine;         // Vector2Int
using TGD.Data;           // Unit / DamageSchool （确认你的Unit定义在哪个命名空间）

namespace TGD.Combat
{
    /// <summary>
    /// 轻量级状态事件载体，避免直接耦合到具体的 StatusInstance 类型。
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
        // 数值结算：攻击者、目标、后减免数值、是否DoT、伤害学派、事件时间(相对秒)
        event Action<Unit, Unit, float, bool, DamageSchool, float> OnDamageResolved;

        // 回合钩子
        event Action<Unit> OnTurnBegin;
        event Action<Unit> OnTurnEnd;

        // 位置变化（只在 Commit 时广播）
        event Action<Unit, Vector2Int, Vector2Int> OnUnitPositionChanged;

        // 状态相关
        event Action<StatusEvent> OnStatusApplied;
        event Action<StatusEvent> OnStatusExpired;
        event Action<StatusEvent> OnStatusDispelled;
        void EmitDamageResolved(Unit atk, Unit tgt, float amountPost, bool isDot, DamageSchool school, float t);

        void EmitTurnBegin(Unit u);
        void EmitTurnEnd(Unit u);

        void EmitUnitPositionChanged(Unit u, Vector2Int from, Vector2Int to);

        void EmitStatusApplied(in StatusEvent e);
        void EmitStatusExpired(in StatusEvent e);
        void EmitStatusDispelled(in StatusEvent e);
    }

    /// <summary>
    /// 简单事件总线实现（直接转发事件）。
    /// </summary>
    public sealed class CombatEventBus : ICombatEventBus
    {
        public event Action<Unit, Unit, float, bool, DamageSchool, float> OnDamageResolved;
        public event Action<Unit> OnTurnBegin;
        public event Action<Unit> OnTurnEnd;
        public event Action<Unit, Vector2Int, Vector2Int> OnUnitPositionChanged;
        public event Action<StatusEvent> OnStatusApplied;
        public event Action<StatusEvent> OnStatusExpired;
        public event Action<StatusEvent> OnStatusDispelled;

        // 便捷触发方法（系统内部调用）
        public void EmitDamageResolved(Unit atk, Unit tgt, float amountPost, bool isDot, DamageSchool school, float t)
            => OnDamageResolved?.Invoke(atk, tgt, amountPost, isDot, school, t);

        public void EmitTurnBegin(Unit u) => OnTurnBegin?.Invoke(u);
        public void EmitTurnEnd(Unit u) => OnTurnEnd?.Invoke(u);

        public void EmitUnitPositionChanged(Unit u, Vector2Int from, Vector2Int to)
            => OnUnitPositionChanged?.Invoke(u, from, to);

        public void EmitStatusApplied(in StatusEvent e) => OnStatusApplied?.Invoke(e);
        public void EmitStatusExpired(in StatusEvent e) => OnStatusExpired?.Invoke(e);
        public void EmitStatusDispelled(in StatusEvent e) => OnStatusDispelled?.Invoke(e);
    }
}
