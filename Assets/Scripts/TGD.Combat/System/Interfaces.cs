// Assets/Scripts/TGD.Combat/Systems/Interfaces.cs
using System;
using TGD.Data;

namespace TGD.Combat
{
        void Log(string eventType, params object[] args);
    public interface IStatusSystem
    {
        void Execute(ApplyStatusOp op, RuntimeCtx ctx);
        void Execute(RemoveStatusOp op, RuntimeCtx ctx);
        void Tick(Unit unit, int deltaSeconds);
    }

    public interface IAuraSystem
    {
        void Execute(AuraOp op, RuntimeCtx ctx);
    }

    // 日志接口（本轮先满足 EffectOpRunner 的调用）
    public interface ICombatLogger
    {
        void Emit(LogOp op, RuntimeCtx ctx);
    }

    // 伤害/治疗系统
    public interface IDamageSystem
    {
        void Execute(DealDamageOp op, RuntimeCtx ctx);
        void Execute(HealOp op, RuntimeCtx ctx);
    }

    // 资源增减系统（精力/纪律/HP上限等）
    public interface IResourceSystem
    {
        void Execute(ModifyResourceOp op, RuntimeCtx ctx);
    }



    // 冷却系统（只在 TurnEnd 统一 -6s）
    public interface ICooldownSystem
    {
        void Execute(ModifyCooldownOp op, RuntimeCtx ctx);
        void TickEndOfTurn(); // 任意单位 TurnEnd 调用一次
    }

    // 技能临时修改（AddCost 等，可叠加、按 SourceHandle 回滚）
    public interface ISkillModSystem
    {
        void Execute(ModifySkillOp op, RuntimeCtx ctx);
        void Execute(ReplaceSkillOp op, RuntimeCtx ctx);
    }

    // 移动系统（移动=技能；Commit 才改坐标）
    public interface IMovementSystem
    {
        void Execute(MoveOp op, RuntimeCtx ctx);
    }

    // 光环/范围系统（AnchorUnit 为圆心，Within/Between/Exact）


    // 调度系统（随机分支/重复/DoT·HoT 附加/将来延时等）
    public interface IScheduler
    {
        void Execute(ScheduleOp op, RuntimeCtx ctx);
    }
}
