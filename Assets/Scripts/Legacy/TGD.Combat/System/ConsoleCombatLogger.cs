using System;
using System.Linq;

namespace TGD.Combat
{
    public sealed class ConsoleCombatLogger : ICombatLogger
    {
        public void Emit(LogOp op, RuntimeCtx ctx)
        {
            if (op == null)
                return;
            Log(op.Message);
        }

        public void Log(string eventType, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            if (args == null || args.Length == 0)
            {
                Console.WriteLine(eventType);
                return;
            }

            var formatted = string.Join(", ", args.Where(a => a != null).Select(a => a.ToString()));
            if (string.IsNullOrWhiteSpace(formatted))
                Console.WriteLine(eventType);
            else
                Console.WriteLine($"{eventType}: {formatted}");
        }
    }
}
