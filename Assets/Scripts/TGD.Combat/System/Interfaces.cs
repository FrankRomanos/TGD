// Assets/Scripts/TGD.Combat/Systems/Interfaces.cs
using TGD.Data;

namespace TGD.Combat
{
    // ��־�ӿڣ����������� EffectOpRunner �ĵ��ã�
    public interface ICombatLogger
    {
        void Emit(LogOp op, RuntimeCtx ctx);
    }

    // �˺�/����ϵͳ
    public interface IDamageSystem
    {
        void Execute(DealDamageOp op, RuntimeCtx ctx);
        void Execute(HealOp op, RuntimeCtx ctx);
    }

    // ��Դ����ϵͳ������/����/HP���޵ȣ�
    public interface IResourceSystem
    {
        void Execute(ModifyResourceOp op, RuntimeCtx ctx);
    }



    // ��ȴϵͳ��ֻ�� TurnEnd ͳһ -6s��
    public interface ICooldownSystem
    {
        void Execute(ModifyCooldownOp op, RuntimeCtx ctx);
        void TickEndOfTurn(); // ���ⵥλ TurnEnd ����һ��
    }

    // ������ʱ�޸ģ�AddCost �ȣ��ɵ��ӡ��� SourceHandle �ع���
    public interface ISkillModSystem
    {
        void Execute(ModifySkillOp op, RuntimeCtx ctx);
        void Execute(ReplaceSkillOp op, RuntimeCtx ctx);
    }

    // �ƶ�ϵͳ���ƶ�=���ܣ�Commit �Ÿ����꣩
    public interface IMovementSystem
    {
        void Execute(MoveOp op, RuntimeCtx ctx);
    }

    // �⻷/��Χϵͳ��AnchorUnit ΪԲ�ģ�Within/Between/Exact��


    // ����ϵͳ�������֧/�ظ�/DoT��HoT ����/������ʱ�ȣ�
    public interface IScheduler
    {
        void Execute(ScheduleOp op, RuntimeCtx ctx);
    }
}
