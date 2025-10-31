using System;

namespace TGD.UIV2.Battle
{
    public static class ChainPopupState
    {
        public static event Action<bool> VisibilityChanged;

        public static bool IsVisible { get; private set; }

        internal static void NotifyVisibility(bool visible)
        {
            if (IsVisible == visible)
                return;

            IsVisible = visible;
            VisibilityChanged?.Invoke(visible);
        }
    }
}
