// Assets/Scripts/TGD.Combat/System/Interfaces.cs
using TGD.Data;

namespace TGD.Combat
{
    public interface ICombatLogger
    {
        void Emit(LogOp op, RuntimeCtx ctx);
        void Log(string eventType, params object[] args);
    }

    public interface IDamageSystem
    {
        void Execute(DealDamageOp op, RuntimeCtx ctx);
        void Execute(HealOp op, RuntimeCtx ctx);
    }

    public interface IResourceSystem
    {
        void Execute(ModifyResourceOp op, RuntimeCtx ctx);
    }

    public interface IStatusSystem
    {
        void Execute(ApplyStatusOp op, RuntimeCtx ctx);
        void Execute(RemoveStatusOp op, RuntimeCtx ctx);
        void Tick(Unit unit, int deltaSeconds);
    }

    public interface ICooldownSystem
    {
        void Execute(ModifyCooldownOp op, RuntimeCtx ctx);
        void TickEndOfTurn();
    }

    public interface ISkillModSystem
    {
        void Execute(ModifySkillOp op, RuntimeCtx ctx);
        void Execute(ReplaceSkillOp op, RuntimeCtx ctx);
    }

    public interface IMovementSystem
    {
        void Execute(MoveOp op, RuntimeCtx ctx);
    }

    public interface IAuraSystem
    {
        void Execute(AuraOp op, RuntimeCtx ctx);
    }

    public interface IScheduler
    {
        void Execute(ScheduleOp op, RuntimeCtx ctx);
    }

    public interface ISkillResolver
    {
        SkillDefinition ResolveById(string skillId);
    }
}
