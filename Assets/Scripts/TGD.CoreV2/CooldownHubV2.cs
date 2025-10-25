using UnityEngine;

namespace TGD.CoreV2
{
    /// <summary>场景内共享一份冷却存储，方便多个动作一起用。</summary>
    public sealed class CooldownHubV2 : MonoBehaviour
    {
        public CooldownStoreSecV2 secStore = new CooldownStoreSecV2();
    }
}