using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const int ImportRawSaveRequest = 105;
        private const string PendingSaveTargetKey = "pending-save-target";
        private const string PendingSaveQuestKey = "pending-save-quest";

        private RuntimeImporter runtimeImporter;
        private TextView status;
        private LinearLayout launcher;
        private bool isBusy;
        private SaveTarget pendingSaveTarget;
        private readonly List<Button> runtimeDependentButtons = new();

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            EnterImmersiveMode();
            runtimeImporter = new RuntimeImporter(FilesDir.AbsolutePath);
            if (savedInstanceState?.GetBoolean(PendingSaveTargetKey, false) == true)
            {
                string questDirectory = savedInstanceState.GetString(PendingSaveQuestKey);
                pendingSaveTarget = String.IsNullOrEmpty(questDirectory)
                    ? SaveTarget.BaseGame
                    : SaveTarget.ForQuest(questDirectory, questDirectory);
            }
            ShowLauncher();
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            base.OnSaveInstanceState(outState);
            if (pendingSaveTarget is null) return;
            outState.PutBoolean(PendingSaveTargetKey, true);
            if (pendingSaveTarget.QuestDirectoryName is not null)
                outState.PutString(PendingSaveQuestKey, pendingSaveTarget.QuestDirectoryName);
        }

        protected override void OnResume()
        {
            base.OnResume();
            RequestedOrientation = ScreenOrientation.Landscape;
            EnterImmersiveMode();
            if (status != null && !isBusy)
                UpdateStatus(File.Exists(ErrorReportPath)
                    ? GetString(Resource.String.status_error_report)
                    : GetString(Resource.String.status_ready));
        }

        private void ShowLauncher()
        {
            runtimeDependentButtons.Clear();
            launcher = new LinearLayout(this) { Orientation = Orientation.Vertical };
            launcher.SetGravity(GravityFlags.Center);
            int padding = (int)(24 * Resources.DisplayMetrics.Density);
            launcher.SetPadding(padding, padding, padding, padding);
            status = new TextView(this) { TextSize = 20, Gravity = GravityFlags.Center };
            status.Click += (_, _) => ShowLastEngineError();
            launcher.AddView(status);
            AddButton(GetString(Resource.String.button_import_runtime), () => OpenTree(ImportRuntimeRequest));
            AddButton(GetString(Resource.String.button_import_mod_folder), () => OpenTree(ImportModFolderRequest));
            AddButton(GetString(Resource.String.button_import_mod_zip), () => OpenDocument(ImportModZipRequest, "application/zip"));
            AddButton(GetString(Resource.String.button_manage_mods), ShowModManager, true, true);
            AddButton(GetString(Resource.String.button_import_save), ChooseSaveImport, true, true);
            AddButton(GetString(Resource.String.button_export_backup), ExportSave);
            AddButton(GetString(Resource.String.button_start_game), StartGame, true, true);
            UpdateStatus(File.Exists(ErrorReportPath)
                ? GetString(Resource.String.status_error_report)
                : GetString(Resource.String.status_ready));
            SetContentView(launcher);
        }

        private void AddButton(string label, Action action, bool enabled = true, bool requiresRuntime = false)
        {
            bool hasRuntime = runtimeImporter.ActivePath() != null;
            Button button = new Button(this)
            {
                Text = label,
                Enabled = enabled && !isBusy && (!requiresRuntime || hasRuntime)
            };
            button.Click += (_, _) => action();
            if (requiresRuntime) runtimeDependentButtons.Add(button);
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

        private void ChooseSaveImport()
        {
            if (runtimeImporter.ActivePath() == null) { UpdateStatus(GetString(Resource.String.status_import_pmdo_first)); return; }
            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_import_save))
                .SetItems(new[] { GetString(Resource.String.dialog_android_backup), GetString(Resource.String.dialog_pmdo_save) }, (_, args) =>
                {
                    if (args.Which == 0) OpenDocument(ImportSaveRequest, "application/zip");
                    else ChooseRawSaveTarget();
                })
                .SetNegativeButton(GetString(Resource.String.button_cancel), (_, _) => { })
                .Show();
        }

        private void ChooseRawSaveTarget()
        {
            string active = RequireRuntime();
            InstalledMod[] quests = new ModCatalog(active).Installed()
                .Where(mod => String.Equals(mod.Metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase))
                .OrderBy(mod => mod.Metadata.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            string[] labels = new[] { SaveTarget.BaseGame.DisplayName }.Concat(quests.Select(QuestLabel)).ToArray();
            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_choose_save_target))
                .SetItems(labels, (_, args) =>
                {
                    pendingSaveTarget = args.Which == 0
                        ? SaveTarget.BaseGame
                        : SaveTarget.ForQuest(quests[args.Which - 1].DirectoryName, quests[args.Which - 1].Metadata.Name);
                    OpenDocument(ImportRawSaveRequest, "*/*");
                })
                .SetNegativeButton(GetString(Resource.String.button_cancel), (_, _) => pendingSaveTarget = null)
                .Show();
        }

        private static string QuestLabel(InstalledMod mod) => mod.Metadata.Name +
            (mod.Metadata.Warnings.Count == 0 ? String.Empty : " [Warning]");

        private void ShowModManager()
        {
            if (runtimeImporter.ActivePath() == null) { UpdateStatus(GetString(Resource.String.status_import_pmdo_first)); return; }
            string active = RequireRuntime();
            InstalledMod[] installed = new ModCatalog(active).Installed().ToArray();
            ModConfigurationState configuration = ModConfiguration.Load(active);
            InstalledMod[] quests = installed.Where(mod => String.Equals(mod.Metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase)).ToArray();
            InstalledMod[] mods = installed.Where(mod => !String.Equals(mod.Metadata.ModType, "Quest", StringComparison.OrdinalIgnoreCase)).ToArray();
            string[] questLabels = new[] { SaveTarget.BaseGame.DisplayName }.Concat(quests.Select(QuestLabel)).ToArray();
            int selectedQuest = Array.FindIndex(quests, quest => quest.Enabled) + 1;
            string selectedQuestDirectory = selectedQuest == 0 ? null : quests[selectedQuest - 1].DirectoryName;

            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_choose_quest))
                .SetSingleChoiceItems(questLabels, selectedQuest, (_, args) =>
                    selectedQuestDirectory = args.Which == 0 ? null : quests[args.Which - 1].DirectoryName)
                .SetPositiveButton(mods.Length == 0 ? GetString(Resource.String.button_save) : GetString(Resource.String.button_next), (_, _) =>
                {
                    if (mods.Length == 0) SaveModSelection(active, selectedQuestDirectory, []);
                    else ShowAdditionalModManager(active, selectedQuestDirectory, mods, configuration.EnabledModDirectoryNames);
                })
                .SetNegativeButton(GetString(Resource.String.button_cancel), (_, _) => { })
                .Show();
        }

        private void ShowAdditionalModManager(string active, string questDirectory, InstalledMod[] mods, IReadOnlyList<string> previousOrder)
        {
            string[] labels = mods.Select(mod => mod.Metadata.Name +
                (mod.Metadata.Warnings.Count == 0 ? String.Empty : " [Warning]")).ToArray();
            bool[] enabled = mods.Select(mod => mod.Enabled).ToArray();
            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_choose_additional_mods))
                .SetMultiChoiceItems(labels, enabled, (_, args) => enabled[args.Which] = args.IsChecked)
                .SetPositiveButton(GetString(Resource.String.button_save), (_, _) =>
                {
                    string[] selected = mods.Where((_, index) => enabled[index]).Select(mod => mod.DirectoryName).ToArray();
                    SaveModSelection(active, questDirectory, ModConfiguration.PreserveEnabledOrder(previousOrder, selected));
                })
                .SetNegativeButton(GetString(Resource.String.button_cancel), (_, _) => { })
                .Show();
        }

        private void SaveModSelection(string active, string questDirectory, IReadOnlyList<string> mods)
        {
            try
            {
                KillCachedGameProcess();
                ModConfiguration.Save(active, questDirectory, mods);
                UpdateStatus("Mod selection saved.");
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                UpdateStatus("Error: " + ex.Message);
            }
        }

        private void ExportSave()
        {
            if (runtimeImporter.ActivePath() == null) { UpdateStatus(GetString(Resource.String.status_import_pmdo_first)); return; }
            Intent intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/zip");
            intent.PutExtra(Intent.ExtraTitle, "pmdo-save-0.8.12.zip");
            StartActivityForResult(intent, ExportSaveRequest);
        }

        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (resultCode != Result.Ok || data?.Data == null || isBusy)
            {
                if (requestCode == ImportRawSaveRequest) pendingSaveTarget = null;
                return;
            }
            AndroidUri uri = data.Data;
            ActivityFlags grantFlags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            if (requestCode == ImportRuntimeRequest || requestCode == ImportModFolderRequest)
                ContentResolver.TakePersistableUriPermission(uri, grantFlags);
            if (requestCode == ImportSaveRequest || requestCode == ImportRawSaveRequest || requestCode == ExportSaveRequest)
                KillCachedGameProcess();

            SetBusy(true);
            try
            {
                UpdateStatus(requestCode == ImportRuntimeRequest ? "Analyzing PMDO folder..." : "Processing...");
                IProgress<RuntimeImportProgress> progress = new Progress<RuntimeImportProgress>(value =>
                {
                    double copiedMiB = value.CompletedBytes / 1048576d;
                    double totalMiB = value.TotalBytes / 1048576d;
                    UpdateStatus($"PMDO import: {value.CompletedFiles:N0} / {value.TotalFiles:N0} files\n{copiedMiB:N1} / {totalMiB:N1} MiB");
                });
                string message = await Task.Run(() => ProcessActivityResultAsync(requestCode, uri, progress));
                if (requestCode == ImportRuntimeRequest)
                    ShowLauncher();
                UpdateStatus(message);
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                UpdateStatus("Error: " + ex.Message);
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
                return "PMDO " + result.Version + " imported.";
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
                RawSaveImporter.RecoverPending(active);
                string temporary = Path.Combine(CacheDir.AbsolutePath, "save-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using (Stream input = ContentResolver.OpenInputStream(uri))
                    using (FileStream output = File.Create(temporary))
                        await BoundedStreams.CopyAsync(input, output, ImportLimits.MaxArchiveBytes).ConfigureAwait(false);
                    using FileStream archive = File.OpenRead(temporary);
                    await new SaveBackup(active).ImportAsync(archive).ConfigureAwait(false);
                    return "Save imported.";
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (requestCode == ImportRawSaveRequest)
            {
                string active = RequireRuntime();
                SaveTarget target = pendingSaveTarget ?? throw new PortableImportException("No save destination selected.");
                pendingSaveTarget = null;
                using Stream input = ContentResolver.OpenInputStream(uri);
                RawSaveImportResult result = await new RawSaveImporter(active).ImportAsync(input, target).ConfigureAwait(false);
                return "SAVE.rssv for " + result.Target.DisplayName + " imported." +
                    (result.PreviousSaveBackedUp ? " Previous save backed up as SAVE.rssv.bak." : String.Empty);
            }
            if (requestCode == ExportSaveRequest)
            {
                string active = RequireRuntime();
                RawSaveImporter.RecoverPending(active);
                using Stream output = ContentResolver.OpenOutputStream(uri, "w");
                await new SaveBackup(active).ExportAsync(output).ConfigureAwait(false);
                return "Save exported.";
            }
            throw new PortableImportException("Unknown file selection.");
        }

        private async Task<string> ImportMod(IFileTree tree)
        {
            string active = RequireRuntime();
            ModMetadata metadata = await new ModImporter(active).ImportAsync(tree).ConfigureAwait(false);
            bool autoEnabled = metadata.IsGameVersionCompatible && !metadata.HasUnsupportedFiles;
            if (autoEnabled) ModConfiguration.Enable(active, metadata);
            else ModConfiguration.Disable(active, metadata);
            string warnings = metadata.Warnings.Count == 0 ? string.Empty : " Warnings: " + string.Join("; ", metadata.Warnings);
            return metadata.Name + (autoEnabled
                ? " imported and enabled."
                : " imported but not enabled for safety. You can enable it manually under 'Manage mods'.") + warnings;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            if (launcher == null) return;
            bool hasRuntime = runtimeImporter.ActivePath() != null;
            for (int index = 0; index < launcher.ChildCount; index++)
                if (launcher.GetChildAt(index) is Button button)
                    button.Enabled = !busy && (!runtimeDependentButtons.Contains(button) || hasRuntime);
        }

        private string RequireRuntime() => runtimeImporter.ActivePath() ?? throw new PortableImportException("Import PMDO 0.8.12 first.");

        private void StartGame()
        {
            try
            {
                string active = RequireRuntime();
                UpdateStatus("Starting...");
                KillCachedGameProcess();
                RawSaveImporter.RecoverPending(active);
                Intent intent = new Intent(this, typeof(PlayActivity));
                intent.PutExtra(PlayActivity.RuntimeRootExtra, active);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                Log.Error("PMDO-ANDROID", ex.ToString());
                ShowLauncher();
                UpdateStatus("Startup error: " + ex.Message);
            }
        }

        private string ErrorReportPath => Path.Combine(FilesDir.AbsolutePath, PlayActivity.ErrorReportFileName);

        private void ShowLastEngineError()
        {
            if (!File.Exists(ErrorReportPath)) return;
            string report;
            try { report = File.ReadAllText(ErrorReportPath); }
            catch (Exception ex) { report = "Could not read error report: " + ex.Message; }
            report = PlayActivity.NormalizeErrorReport(report);

            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.dialog_latest_error))
                .SetMessage(report)
                .SetPositiveButton(GetString(Resource.String.button_close), (_, _) => { })
                .SetNegativeButton(GetString(Resource.String.button_delete_report), (_, _) =>
                {
                    try { File.Delete(ErrorReportPath); } catch { }
                    UpdateStatus(GetString(Resource.String.status_ready));
                })
                .SetNeutralButton(GetString(Resource.String.button_copy_text), (_, _) =>
                {
                    ClipboardManager clipboard = (ClipboardManager)GetSystemService(ClipboardService);
                    clipboard.PrimaryClip = ClipData.NewPlainText("PMDO error", report);
                    Toast.MakeText(this, GetString(Resource.String.toast_error_copied), ToastLength.Short).Show();
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
