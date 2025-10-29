namespace TGD.CombatV2
{
    public interface ITurnBudget
    {
        bool HasTime(int seconds);
        void SpendTime(int seconds, bool silent = false);
        void RefundTime(int seconds, bool silent = false);
        int Remaining { get; }
    }

    public interface IResourcePool
    {
        bool Has(string id, int value);
        void Spend(string id, int value, string reason = "", bool silent = false);
        void Refund(string id, int value, string reason = "", bool silent = false);
        int Get(string id);
        int GetMax(string id);
    }

    public interface ICooldownSink
    {
        bool Ready(string skillId);
        void StartSeconds(string skillId, int seconds);
        void AddSeconds(string skillId, int deltaSeconds);
        int SecondsLeft(string skillId);
        int TurnsLeft(string skillId);
    }
}
