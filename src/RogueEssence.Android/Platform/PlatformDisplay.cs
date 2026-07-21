using System;

namespace RogueEssence.Content
{
    public static class PlatformDisplay
    {
        public const int FullScreenMode = 0;
        public const int MaximumAspectMode = 5;

        private static int availableWidth = 1920;
        private static int availableHeight = 1080;

        public static event Action<int> WindowModeChanged;

        public static int NormalizeWindowMode(int mode) => Math.Clamp(mode, FullScreenMode, MaximumAspectMode);

        public static void Configure(int width, int height)
        {
            availableWidth = Math.Max(width, height);
            availableHeight = Math.Min(width, height);
        }

        public static int GetRenderScale(int mode)
        {
            mode = NormalizeWindowMode(mode);
            int maximumScale = Math.Clamp(Math.Min(
                availableWidth / Math.Max(1, GraphicsManager.ScreenWidth),
                availableHeight / Math.Max(1, GraphicsManager.ScreenHeight)), 1, 8);
            return mode is > FullScreenMode and < MaximumAspectMode
                ? Math.Min(mode, maximumScale)
                : maximumScale;
        }

        public static void SetWindowMode(int mode)
        {
            mode = NormalizeWindowMode(mode);
            int renderScale = GetRenderScale(mode);
            if (GraphicsManager.WindowZoom != renderScale)
                GraphicsManager.SetWindowMode(renderScale);
            WindowModeChanged?.Invoke(mode);
        }
    }
}
