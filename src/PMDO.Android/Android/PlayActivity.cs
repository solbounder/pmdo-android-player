using System;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework;
using RogueEssence;
using RogueEssence.Content;
using PMDO.Portable;

namespace PMDO.Android
{
    [Activity(
        Label = "PMDO Android Player",
        Process = ":game",
        ScreenOrientation = ScreenOrientation.Landscape,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
        Theme = "@android:style/Theme.Material.Light.NoActionBar.Fullscreen")]
    public sealed class PlayActivity : AndroidGameActivity
    {
        public const string RuntimeRootExtra = "pmdo.runtimeRoot";
        public const string ErrorReportFileName = "last-engine-error.txt";

        private GameBase game;
        private View touchOverlay;
        private FrameLayout rootView;
        private FrameLayout gameHost;
        private View gameView;
        private string runtimeRoot;
        private bool errorDialogVisible;
        private bool consumeTouchActivation;
        private Handler mainHandler;
        private int windowMode;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RenderOnUIThread = false;
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            EnterImmersiveMode();
            TouchController.TouchModeChanged += OnTouchModeChanged;
            PlatformDisplay.WindowModeChanged += OnWindowModeChanged;
            mainHandler = new Handler(Looper.MainLooper);

            try
            {
                string active = Intent.GetStringExtra(RuntimeRootExtra);
                if (string.IsNullOrWhiteSpace(active)) throw new InvalidOperationException("Runtime path missing.");
                runtimeRoot = active;
                global::Android.Graphics.Rect windowBounds = WindowManager.CurrentWindowMetrics.Bounds;
                int availableWidth = Math.Max(windowBounds.Width(), windowBounds.Height());
                int availableHeight = Math.Min(windowBounds.Width(), windowBounds.Height());
                Log.Info("PMDO-ENGINE", $"Activity window metrics {availableWidth}x{availableHeight}");
                var displayPreferences = GetSharedPreferences("pmdo-android-display", FileCreationMode.Private);
                bool initializeDisplayMode = !displayPreferences.GetBoolean("display-mode-v4", false);
                game = GameStartup.Create(active, availableWidth, availableHeight, initializeDisplayMode, OnEngineError);
                windowMode = PlatformDisplay.NormalizeWindowMode(DiagManager.Instance.CurSettings.Window);
                displayPreferences.Edit().PutBoolean("display-mode-v4", true).Apply();
                gameView = game.Services.GetService(typeof(View)) as View ?? throw new InvalidOperationException("MonoGame view unavailable.");

                rootView = new FrameLayout(this);
                rootView.SetBackgroundColor(global::Android.Graphics.Color.Black);
                rootView.SetClipChildren(false);
                rootView.SetClipToPadding(false);
                gameHost = new FrameLayout(this);
                gameHost.SetBackgroundColor(global::Android.Graphics.Color.Black);
                gameHost.AddView(gameView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                rootView.AddView(gameHost, CreateGameHostLayout(availableWidth, availableHeight));
                touchOverlay = TouchOverlay.Create(this);
                touchOverlay.Visibility = TouchController.IsTouchMode ? ViewStates.Visible : ViewStates.Gone;
                rootView.AddView(touchOverlay, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
                SetContentView(rootView);
                rootView.Post(ApplyMeasuredGameLayout);
                if (gameView is MonoGameAndroidGameView monoGameView)
                    monoGameView.Resume();
                game.Run();
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                Toast.MakeText(this, "Startup error: " + ex.Message, ToastLength.Long).Show();
                Finish();
            }
        }

        private void OnTouchModeChanged(bool touchMode) => RunOnUiThread(() =>
        {
            if (!touchMode) TouchOverlay.ReleaseAll();
            if (touchOverlay != null) touchOverlay.Visibility = touchMode ? ViewStates.Visible : ViewStates.Gone;
        });

        private void OnWindowModeChanged(int mode) => RunOnUiThread(() =>
        {
            windowMode = PlatformDisplay.NormalizeWindowMode(mode);
            ApplyMeasuredGameLayout();
        });

        public override bool DispatchTouchEvent(MotionEvent motionEvent)
        {
            if (motionEvent != null)
            {
                if (motionEvent.ActionMasked == MotionEventActions.Down && !TouchController.IsTouchMode)
                {
                    consumeTouchActivation = true;
                    TouchController.UseTouchInput();
                }

                if (consumeTouchActivation)
                {
                    if (motionEvent.ActionMasked == MotionEventActions.Up ||
                        motionEvent.ActionMasked == MotionEventActions.Cancel)
                        consumeTouchActivation = false;
                    return true;
                }
            }
            return base.DispatchTouchEvent(motionEvent);
        }

        public override bool DispatchKeyEvent(KeyEvent keyEvent)
        {
            if (keyEvent != null && keyEvent.Action == KeyEventActions.Down && IsControllerKey(keyEvent.KeyCode))
                TouchController.UsePhysicalInput();
            return base.DispatchKeyEvent(keyEvent);
        }

        private static bool IsControllerKey(Keycode keyCode) => keyCode == Keycode.ButtonA ||
            keyCode == Keycode.ButtonB || keyCode == Keycode.ButtonX || keyCode == Keycode.ButtonY ||
            keyCode == Keycode.ButtonL1 || keyCode == Keycode.ButtonR1 ||
            keyCode == Keycode.ButtonL2 || keyCode == Keycode.ButtonR2 ||
            keyCode == Keycode.ButtonSelect || keyCode == Keycode.ButtonStart ||
            keyCode == Keycode.ButtonThumbl || keyCode == Keycode.ButtonThumbr ||
            keyCode == Keycode.DpadUp || keyCode == Keycode.DpadDown ||
            keyCode == Keycode.DpadLeft || keyCode == Keycode.DpadRight;

        private AndroidViewportPlacement CalculateGamePlacement(int availableWidth, int availableHeight) =>
            ViewportLayout.CalculateAndroid(
                availableWidth,
                availableHeight,
                GraphicsManager.ScreenWidth,
                GraphicsManager.ScreenHeight,
                windowMode);

        private FrameLayout.LayoutParams CreateGameHostLayout(int availableWidth, int availableHeight)
        {
            AndroidViewportPlacement placement = CalculateGamePlacement(availableWidth, availableHeight);
            return new FrameLayout.LayoutParams(placement.Width, placement.Height, GravityFlags.Center);
        }

        private void ApplyMeasuredGameLayout()
        {
            if (rootView == null || gameHost == null || gameView == null || rootView.Width <= 0 || rootView.Height <= 0) return;
            AndroidViewportPlacement placement = CalculateGamePlacement(rootView.Width, rootView.Height);
            FrameLayout.LayoutParams parameters = new FrameLayout.LayoutParams(
                placement.Width, placement.Height, GravityFlags.Center);
            gameHost.LayoutParameters = parameters;
            gameHost.PivotX = placement.Width / 2f;
            gameHost.PivotY = placement.Height / 2f;
            gameHost.ScaleX = placement.ScaleX;
            gameHost.ScaleY = placement.ScaleY;
            gameView.LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            gameHost.RequestLayout();
            gameView.RequestLayout();
            rootView.Post(LogMeasuredGameLayout);
        }

        private void LogMeasuredGameLayout()
        {
            if (rootView == null || gameHost == null) return;
            int[] location = new int[2];
            gameHost.GetLocationOnScreen(location);
            Log.Info("PMDO-ENGINE", $"Viewport mode {windowMode}; host {gameHost.Width}x{gameHost.Height} " +
                $"scale {gameHost.ScaleX:0.###}x{gameHost.ScaleY:0.###} at screen {location[0]},{location[1]} " +
                $"inside {rootView.Width}x{rootView.Height}");
        }

        private void OnEngineError(string message)
        {
            PersistErrorReport("PMDO engine error:\n" + message);
            mainHandler?.PostDelayed(() => ShowEngineError(message), 750);
        }

        private void ShowEngineError(string message)
        {
            if (IsFinishing || IsDestroyed || errorDialogVisible) return;
            errorDialogVisible = true;
            string report = BuildErrorReport(message);
            PersistErrorReport(report);

            AlertDialog dialog = new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_device_error))
                .SetMessage(report)
                .SetPositiveButton(GetString(Resource.String.button_return_launcher), (_, _) => Finish())
                .SetNegativeButton(GetString(Resource.String.button_stay_game), (_, _) => { })
                .SetNeutralButton(GetString(Resource.String.button_copy_text), (_, _) =>
                {
                    ClipboardManager clipboard = (ClipboardManager)GetSystemService(ClipboardService);
                    clipboard.PrimaryClip = ClipData.NewPlainText("PMDO error", report);
                    Toast.MakeText(this, GetString(Resource.String.toast_error_copied), ToastLength.Short).Show();
                })
                .Create();
            dialog.DismissEvent += (_, _) => errorDialogVisible = false;
            dialog.Show();
        }

        private string BuildErrorReport(string message)
        {
            string layout = "Layout not measured yet";
            if (rootView != null && gameHost != null)
            {
                int[] location = new int[2];
                gameHost.GetLocationOnScreen(location);
                layout = $"Display {rootView.Width}x{rootView.Height}; game {gameHost.Width}x{gameHost.Height} at {location[0]},{location[1]}";
            }

            string logTail = string.Empty;
            try
            {
                string logDirectory = Path.Combine(runtimeRoot, "LOG");
                string latest = Directory.Exists(logDirectory)
                    ? Directory.EnumerateFiles(logDirectory, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                    : null;
                if (latest != null)
                {
                    string log = File.ReadAllText(latest);
                    logTail = log.Length <= 6000 ? log : log.Substring(log.Length - 6000);
                }
            }
            catch (Exception ex)
            {
                logTail = "Could not read log: " + ex.Message;
            }

            string savePermissionGuidance = IsSavePermissionError(message)
                ? "\n\nSave permission guidance: import SAVE.rssv through the launcher, or restore ownership of the SAVE folder to this app."
                : string.Empty;
            return "Please photograph this dialog or copy the text.\n\n" +
                layout + "\n\nError: " + message + savePermissionGuidance +
                (string.IsNullOrWhiteSpace(logTail) ? string.Empty : "\n\nLatest engine log:\n" + logTail);
        }

        internal static string NormalizeErrorReport(string report) => report
            .Replace("PMDO-Engine-Fehler", "PMDO engine error", StringComparison.Ordinal)
            .Replace("Bitte diesen Dialog fotografieren oder den Text kopieren.", "Please photograph this dialog or copy the text.", StringComparison.Ordinal)
            .Replace("Layout noch nicht gemessen", "Layout not measured yet", StringComparison.Ordinal)
            .Replace("Spiel ", "Game ", StringComparison.Ordinal)
            .Replace(" bei ", " at ", StringComparison.Ordinal)
            .Replace("Fehler: ", "Error: ", StringComparison.Ordinal)
            .Replace("Letztes Engine-Protokoll:", "Latest engine log:", StringComparison.Ordinal);

        private static bool IsSavePermissionError(string message) =>
            message?.IndexOf("SAVE", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (message.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0 ||
             message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0 ||
             message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0);

        private void PersistErrorReport(string report)
        {
            try
            {
                File.WriteAllText(Path.Combine(FilesDir.AbsolutePath, ErrorReportFileName), report);
            }
            catch (Exception ex)
            {
                Log.Warn("PMDO-ANDROID", "Could not persist error report: " + ex.Message);
            }
        }

        protected override void OnPause()
        {
            TouchOverlay.ReleaseAll();
            TouchController.Clear();
            if (game != null) SoundManager.OnApplicationPause();
            base.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
            RequestedOrientation = ScreenOrientation.Landscape;
            EnterImmersiveMode();
            if (game != null) { game.ResetElapsedTime(); SoundManager.OnApplicationResume(); }
        }

        protected override void OnDestroy()
        {
            TouchController.TouchModeChanged -= OnTouchModeChanged;
            PlatformDisplay.WindowModeChanged -= OnWindowModeChanged;
            mainHandler?.RemoveCallbacksAndMessages(null);
            try
            {
                // AndroidGameActivity owns the Game instance and disposes it here.
                // Keeping the engine in its own process lets a later launch start
                // with clean PMDO singletons instead of reusing disposed state.
                base.OnDestroy();
            }
            finally
            {
                game = null;
                global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
            }
        }

        private void EnterImmersiveMode() => Window.DecorView.SystemUiVisibility =
            (StatusBarVisibility)(SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
    }
}
