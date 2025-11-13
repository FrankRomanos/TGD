using System;

namespace TGD.CoreV2.Resource
{
    public interface IResourcePool
    {
        int Get(string id);

        int Cap(string id);

        bool TrySpend(string id, int amount);

        void Gain(string id, int amount);

        event Action<ResourceChangeEvent> Changed;
    }
}
