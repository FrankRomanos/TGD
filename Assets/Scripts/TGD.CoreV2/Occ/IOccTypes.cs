
namespace TGD.CoreV2
{
    public struct OccTxnId
    {
        public int Value;
        public OccTxnId(int v) { Value = v; }
        public override string ToString() { return Value.ToString(); }
    }

    public enum OccAction { Place, Move, Remove, Refit, Reserve, Commit, Cancel }
    public enum OccFailReason { None, NoStore, ActorMissing, Blocked, AlreadyPlaced, NotPlaced, MultiStoreMismatch, AdapterMissing }

    // 用类代替 record struct
    public sealed class OccSnapshot
    {
        public string BoardId;
        public string ActorId;
        public Hex Anchor;
        public Facing4 Facing;
        public string FootprintKey;
        public int StoreVersion;

        public OccSnapshot(string boardId, string actorId, Hex anchor, Facing4 facing, string key, int ver)
        {
            BoardId = boardId; ActorId = actorId; Anchor = anchor; Facing = facing; FootprintKey = key; StoreVersion = ver;
        }
    }
}
