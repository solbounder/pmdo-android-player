using Android.App;
using Android.Content;

namespace SDL2
{
    /// <summary>
    /// Compatibility surface for the two clipboard calls used by desktop PMDO menus.
    /// Keeping the namespace avoids changes to upstream gameplay source files.
    /// </summary>
    public static class SDL
    {
        public static string SDL_GetClipboardText()
        {
            ClipboardManager clipboard = (ClipboardManager)Application.Context.GetSystemService(Context.ClipboardService);
            return clipboard?.PrimaryClip?.GetItemAt(0)?.CoerceToText(Application.Context)?.ToString() ?? string.Empty;
        }

        public static int SDL_SetClipboardText(string text)
        {
            ClipboardManager clipboard = (ClipboardManager)Application.Context.GetSystemService(Context.ClipboardService);
            if (clipboard != null)
                clipboard.PrimaryClip = ClipData.NewPlainText("PMDO", text ?? string.Empty);
            return clipboard == null ? -1 : 0;
        }
    }
}
