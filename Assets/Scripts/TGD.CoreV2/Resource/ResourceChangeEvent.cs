using System;

namespace TGD.CoreV2.Resource
{
    public readonly struct ResourceChangeEvent
    {
        public readonly string ResourceId;
        public readonly int Before;
        public readonly int After;

        public ResourceChangeEvent(string resourceId, int before, int after)
        {
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            Before = before;
            After = after;
        }
    }
}
