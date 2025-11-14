using System.Collections.Generic;
using UnityEngine;

namespace TGD.HexBoard
{
    static class HexTileTintRegistry
    {
        struct TintEntry
        {
            public object owner;
            public Color color;
            public int priority;
            public int order;
        }

        static readonly Dictionary<Renderer, List<TintEntry>> s_entries = new();
        static readonly MaterialPropertyBlock s_block = new();
        static int s_orderCounter;

        public static void Apply(Renderer renderer, object owner, Color color, int priority)
        {
            if (!renderer || owner == null)
                return;

            if (!s_entries.TryGetValue(renderer, out var list))
            {
                list = new List<TintEntry>();
                s_entries[renderer] = list;
            }

            int index = list.FindIndex(e => ReferenceEquals(e.owner, owner));
            var entry = new TintEntry
            {
                owner = owner,
                color = color,
                priority = priority,
                order = ++s_orderCounter
            };

            if (index >= 0)
                list[index] = entry;
            else
                list.Add(entry);

            UpdateRenderer(renderer, list);
        }

        public static void Remove(Renderer renderer, object owner)
        {
            if (!renderer || owner == null)
                return;

            if (!s_entries.TryGetValue(renderer, out var list))
            {
                UpdateRenderer(renderer, null);
                return;
            }

            int index = list.FindIndex(e => ReferenceEquals(e.owner, owner));
            if (index >= 0)
                list.RemoveAt(index);

            if (list.Count == 0)
            {
                s_entries.Remove(renderer);
                UpdateRenderer(renderer, null);
            }
            else
            {
                UpdateRenderer(renderer, list);
            }
        }

        static void UpdateRenderer(Renderer renderer, List<TintEntry> list)
        {
            if (!renderer)
                return;

            Color final = Color.white;

            if (list != null && list.Count > 0)
            {
                var best = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    var candidate = list[i];
                    if (candidate.priority > best.priority ||
                        (candidate.priority == best.priority && candidate.order > best.order))
                    {
                        best = candidate;
                    }
                }

                final = best.color;
            }

            s_block.Clear();
            renderer.GetPropertyBlock(s_block);
            s_block.SetColor("_BaseColor", final);
            s_block.SetColor("_Color", final);
            renderer.SetPropertyBlock(s_block);
        }
    }
}
