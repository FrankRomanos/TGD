using System;
using System.Collections.Generic;
using UnityEngine;

namespace TGD.Combat
{
    /// <summary>
    /// In-memory combat logger that mirrors entries to the Unity console.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatLog", menuName = "TGD/Combat Log", order = 0)]
    public sealed class CombatLog : ScriptableObject, ICombatLogger
    {
        [SerializeField]
        private List<string> entries = new();

        public IReadOnlyList<string> Entries => entries;

        public event Action<string> OnEntryAdded;

        public void Clear()
        {
            entries.Clear();
        }

        public void Emit(LogOp op, RuntimeCtx ctx)
        {
            if (op == null || string.IsNullOrWhiteSpace(op.Message))
                return;
            Log(op.Message);
        }

        public void Log(string eventType, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            string message = args == null || args.Length == 0
                ? eventType
                : $"{eventType}: {string.Join(", ", FormatArguments(args))}";

            entries.Add(message);
            Debug.Log(message);
            OnEntryAdded?.Invoke(message);
        }

        private static IEnumerable<string> FormatArguments(IEnumerable<object> args)
        {
            foreach (var arg in args)
            {
                if (arg == null)
                    continue;
                switch (arg)
                {
                    case float f:
                        yield return f.ToString("0.###");
                        break;
                    case double d:
                        yield return d.ToString("0.###");
                        break;
                    default:
                        yield return arg.ToString();
                        break;
                }
            }
        }
    }
}
