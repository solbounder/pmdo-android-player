using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Util;
using PMDO.Portable;
using AndroidUri = global::Android.Net.Uri;

namespace PMDO.Android
{
    [Activity(
        Label = "PMDO Android Player",
        MainLauncher = true,
        AlwaysRetainTaskState = true,
        LaunchMode = LaunchMode.SingleTask,
        ScreenOrientation = ScreenOrientation.Landscape,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
        Theme = "@android:style/Theme.Material.Light.NoActionBar.Fullscreen")]
    public sealed class MainActivity : Activity
    {
        private const int ImportRuntimeRequest = 100;
        private const int ImportModZipRequest = 101;
        private const int ImportSaveRequest = 102;
        private const int ExportSaveRequest = 103;
        private const int ImportModFolderRequest = 104;

        private RuntimeImporter runtimeImporter;
        private TextView status;
        private LinearLayout launcher;
        private bool isBusy;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            EnterImmersiveMode();
            runtimeImporter = new RuntimeImporter(FilesDir.AbsolutePath);
            ShowLauncher();
        }

        protected override void OnResume()
        {
            base.OnResume();
            RequestedOrientation = ScreenOrientation.Landscape;
            EnterImmersiveMode();
            if (status != null && !isBusy)
                UpdateStatus(File.Exists(ErrorReportPath)
                    ? "Fehlerprotokoll vorhanden – hier tippen"
                    : "Bereit");
        }

        private void ShowLauncher()
        {
            launcher = new LinearLayout(this) { Orientation = Orientation.Vertical };
            launcher.SetGravity(GravityFlags.Center);
            int padding = (int)(24 * Resources.DisplayMetrics.Density);
            launcher.SetPadding(padding, padding, padding, padding);
            status = new TextView(this) { TextSize = 20, Gravity = GravityFlags.Center };
            status.Click += (_, _) => ShowLastEngineError();
            launcher.AddView(status);
            AddButton("PMDO-0.8.12-Ordner importieren", () => OpenTree(ImportRuntimeRequest));
            AddButton("Mod-Ordner importieren", () => OpenTree(ImportModFolderRequest));
            AddButton("Mod-ZIP importieren", () => OpenDocument(ImportModZipRequest, "application/zip"));
            AddButton("Spielstand importieren", () => OpenDocument(ImportSaveRequest, "application/zip"));
            AddButton("Spielstand exportieren", ExportSave);
            AddButton("Spiel starten", StartGame, runtimeImporter.ActivePath() != null);
            UpdateStatus(File.Exists(ErrorReportPath)
                ? "Fehlerprotokoll vorhanden – hier tippen"
                : "Bereit");
            SetContentView(launcher);
        }

        private void AddButton(string label, Action action, bool enabled = true)
        {
            Button button = new Button(this) { Text = label, Enabled = enabled && !isBusy };
            button.Click += (_, _) => action();
            launcher.AddView(button, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        private void OpenTree(int requestCode)
        {
            Intent intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
            StartActivityForResult(intent, requestCode);
        }

        private void OpenDocument(int requestCode, string mime)
        {
            Intent intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(mime);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivityForResult(intent, requestCode);
        }

        private void ExportSave()
        {
            if (runtimeImporter.ActivePath() == null) { UpdateStatus("Zuerst PMDO importieren."); return; }
            Intent intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/zip");
            intent.PutExtra(Intent.ExtraTitle, "pmdo-save-0.8.12.zip");
            StartActivityForResult(intent, ExportSaveRequest);
        }

        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (resultCode != Result.Ok || data?.Data == null || isBusy) return;
            AndroidUri uri = data.Data;
            ActivityFlags grantFlags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            if (requestCode == ImportRuntimeRequest || requestCode == ImportModFolderRequest)
                ContentResolver.TakePersistableUriPermission(uri, grantFlags);

            SetBusy(true);
            try
            {
                UpdateStatus(requestCode == ImportRuntimeRequest ? "Analysiere PMDO-Ordner …" : "Verarbeite …");
                IProgress<RuntimeImportProgress> progress = new Progress<RuntimeImportProgress>(value =>
                {
                    double copiedMiB = value.CompletedBytes / 1048576d;
                    double totalMiB = value.TotalBytes / 1048576d;
                    UpdateStatus($"PMDO-Import: {value.CompletedFiles:N0} / {value.TotalFiles:N0} Dateien\n{copiedMiB:N1} / {totalMiB:N1} MiB");
                });
                string message = await Task.Run(() => ProcessActivityResultAsync(requestCode, uri, progress));
                if (requestCode == ImportRuntimeRequest)
                    ShowLauncher();
                UpdateStatus(message);
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                UpdateStatus("Fehler: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async Task<string> ProcessActivityResultAsync(int requestCode, AndroidUri uri, IProgress<RuntimeImportProgress> progress)
        {
            if (requestCode == ImportRuntimeRequest)
            {
                using AndroidFileTree tree = new AndroidFileTree(ContentResolver, uri);
                using Stream manifestStream = Assets.Open("runtime-manifest.json");
                RuntimeManifest manifest = await JsonSerializer.DeserializeAsync<RuntimeManifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false);
                ImportResult result = await runtimeImporter.ImportAsync(tree, manifest, default, progress).ConfigureAwait(false);
                return "PMDO " + result.Version + " importiert.";
            }
            if (requestCode == ImportModFolderRequest)
            {
                using AndroidFileTree tree = new AndroidFileTree(ContentResolver, uri);
                return await ImportMod(tree).ConfigureAwait(false);
            }
            if (requestCode == ImportModZipRequest)
            {
                string temporary = Path.Combine(CacheDir.AbsolutePath, "mod-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using (Stream input = ContentResolver.OpenInputStream(uri))
                    using (FileStream output = File.Create(temporary))
                        await BoundedStreams.CopyAsync(input, output, ImportLimits.MaxArchiveBytes).ConfigureAwait(false);
                    using ZipFileTree tree = new ZipFileTree(temporary);
                    return await ImportMod(tree).ConfigureAwait(false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (requestCode == ImportSaveRequest)
            {
                string active = RequireRuntime();
                string temporary = Path.Combine(CacheDir.AbsolutePath, "save-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using (Stream input = ContentResolver.OpenInputStream(uri))
                    using (FileStream output = File.Create(temporary))
                        await BoundedStreams.CopyAsync(input, output, ImportLimits.MaxArchiveBytes).ConfigureAwait(false);
                    using FileStream archive = File.OpenRead(temporary);
                    await new SaveBackup(active).ImportAsync(archive).ConfigureAwait(false);
                    return "Spielstand importiert.";
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (requestCode == ExportSaveRequest)
            {
                string active = RequireRuntime();
                using Stream output = ContentResolver.OpenOutputStream(uri, "w");
                await new SaveBackup(active).ExportAsync(output).ConfigureAwait(false);
                return "Spielstand exportiert.";
            }
            throw new PortableImportException("Unbekannte Dateiauswahl.");
        }

        private async Task<string> ImportMod(IFileTree tree)
        {
            string active = RequireRuntime();
            ModMetadata metadata = await new ModImporter(active).ImportAsync(tree).ConfigureAwait(false);
            ModConfiguration.Enable(active, metadata);
            string warnings = metadata.Warnings.Count == 0 ? string.Empty : " Warnungen: " + string.Join("; ", metadata.Warnings);
            return metadata.Name + " importiert und aktiviert." + warnings;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            if (launcher == null) return;
            for (int index = 0; index < launcher.ChildCount; index++)
                if (launcher.GetChildAt(index) is Button button)
                    button.Enabled = !busy && (button.Text != "Spiel starten" || runtimeImporter.ActivePath() != null);
        }

        private string RequireRuntime() => runtimeImporter.ActivePath() ?? throw new PortableImportException("Zuerst PMDO 0.8.12 importieren.");

        private void StartGame()
        {
            try
            {
                string active = RequireRuntime();
                UpdateStatus("Starte …");
                KillCachedGameProcess();
                Intent intent = new Intent(this, typeof(PlayActivity));
                intent.PutExtra(PlayActivity.RuntimeRootExtra, active);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                ShowLauncher();
                UpdateStatus("Startfehler: " + ex.Message);
            }
        }

        private string ErrorReportPath => Path.Combine(FilesDir.AbsolutePath, PlayActivity.ErrorReportFileName);

        private void ShowLastEngineError()
        {
            if (!File.Exists(ErrorReportPath)) return;
            string report;
            try { report = File.ReadAllText(ErrorReportPath); }
            catch (Exception ex) { report = "Fehlerprotokoll konnte nicht gelesen werden: " + ex.Message; }

            new AlertDialog.Builder(this)
                .SetTitle("Letzter PMDO-Fehler")
                .SetMessage(report)
                .SetPositiveButton("Schließen", (_, _) => { })
                .SetNegativeButton("Protokoll löschen", (_, _) =>
                {
                    try { File.Delete(ErrorReportPath); } catch { }
                    UpdateStatus("Bereit");
                })
                .SetNeutralButton("Text kopieren", (_, _) =>
                {
                    ClipboardManager clipboard = (ClipboardManager)GetSystemService(ClipboardService);
                    clipboard.PrimaryClip = ClipData.NewPlainText("PMDO-Fehler", report);
                    Toast.MakeText(this, "Fehlertext kopiert", ToastLength.Short).Show();
                })
                .Show();
        }

        private void KillCachedGameProcess()
        {
            ActivityManager manager = GetSystemService(ActivityService) as ActivityManager;
            if (manager?.RunningAppProcesses == null) return;
            string gameProcess = PackageName + ":game";
            foreach (ActivityManager.RunningAppProcessInfo process in manager.RunningAppProcesses)
            {
                if (string.Equals(process.ProcessName, gameProcess, StringComparison.Ordinal) && process.Pid != global::Android.OS.Process.MyPid())
                {
                    Log.Info("PMDO-ANDROID", "Stopping cached game process " + process.Pid + " before launch.");
                    global::Android.OS.Process.KillProcess(process.Pid);
                }
            }
        }

        private void EnterImmersiveMode() => Window.DecorView.SystemUiVisibility =
            (StatusBarVisibility)(SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);

        private void UpdateStatus(string message)
        {
            if (status != null) status.Text = "PMDO Android Player\n" + message;
        }
    }
}
