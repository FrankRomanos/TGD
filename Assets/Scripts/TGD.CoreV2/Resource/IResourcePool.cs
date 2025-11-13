using System;

namespace TGD.CoreV2.Resource
{
    public interface IResourcePool
    {
        int Get(string id);

        int Cap(string id);

        bool TrySpend(string id, int amount);

        void Gain(string id, int amount);

        // ★ 供天赋/状态调用：直接设新的上限
        void SetCap(string id, int newCap, bool clampCurrent = true);

        event Action<ResourceChangeEvent> Changed;
    }
}
