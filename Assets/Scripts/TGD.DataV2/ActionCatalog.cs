using System.Collections.Generic;
using UnityEngine;

namespace TGD.DataV2
{
    [CreateAssetMenu(menuName = "TGD/Actions/ActionCatalog", fileName = "ActionCatalog")]
    public sealed class ActionCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string actionId;    // 与CAM一致
            public string displayName; // 可选：给编辑器看
            public Sprite icon;        // 可选：UI
            public bool noCooldown;    // 可选：校验用
        }

        public List<Entry> entries = new();

        public bool Contains(string id) => entries.Exists(e => e.actionId == id);
        public bool IsNoCooldown(string id)
        {
            var i = entries.FindIndex(e => e.actionId == id);
            return i >= 0 && entries[i].noCooldown;
        }
    }
}
