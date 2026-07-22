using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using System.Linq;
using Android.Util;
using Android.App;
using Microsoft.Xna.Framework.Input;
using PMDC.Dev;
using RogueEssence;
using RogueEssence.Content;
using RogueEssence.Dev;
using RogueEssence.Data;
using PMDO.Portable;

namespace PMDO.Android
{
    internal static class GameStartup
    {
        public static GameBase Create(
            string runtimeRoot,
            int availableWidth,
            int availableHeight,
            bool initializeAndroidDisplayMode,
            Action<string> engineError = null)
        {
            AppContext.SetSwitch("Switch.System.Runtime.Serialization.SerializationGuard.AllowFileWrites", true);
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Serializer.InitSettings(new SerializerContractResolver(), new UpgradeBinder());
            Serializer.Settings.Converters.Add(new MonoGameColorConverter());
            Serializer.Settings.Converters.Add(new MonoGameVector2Converter());

            string marker = Path.Combine(runtimeRoot, "PMDC");
            PathMod.InitPathMod(marker);
            string root = runtimeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            PathMod.ASSET_PATH = root;
            PathMod.APP_PATH = root;
            PathMod.DEV_PATH = Path.Combine(root, "RawAsset") + Path.DirectorySeparatorChar;

            DiagManager.InitInstance();
            DiagManager.Instance.SetErrorListener(null, () => Environment.NewLine + Environment.StackTrace);
            DiagManager.Instance.AddErrorObserver(message =>
            {
                Log.Error("PMDO-ENGINE", message);
                engineError?.Invoke(message);
            });
            DiagManager.Instance.CurSettings = DiagManager.Instance.LoadSettings();
            DiagManager.Instance.DevMode = false;
            DiagManager.Instance.DebugLua = false;
            DiagManager.Instance.DevEditor = new EmptyEditor();
            DiagManager.Instance.LogInfo("PMDO Android session started");
            DiagManager.Instance.LogInfo("Version: " + typeof(GameStartup).Assembly.GetName().Version);

            PathMod.InitNamespaces();
            GraphicsManager.InitParams();
            int width = Math.Max(availableWidth, availableHeight);
            int height = Math.Min(availableWidth, availableHeight);
            PlatformDisplay.Configure(width, height);
            int previousWindowMode = DiagManager.Instance.CurSettings.Window;
            int windowMode = initializeAndroidDisplayMode
                ? PlatformDisplay.MaximumAspectMode
                : PlatformDisplay.NormalizeWindowMode(previousWindowMode);
            DiagManager.Instance.CurSettings.Window = windowMode;
            if (initializeAndroidDisplayMode || previousWindowMode != windowMode)
                DiagManager.Instance.SaveSettings(DiagManager.Instance.CurSettings);
            Log.Info("PMDO-ENGINE", "Android display mode " + windowMode +
                "; render scale " + PlatformDisplay.GetRenderScale(windowMode) + "x");
            RemoveUnsupportedControllerLabels();
            DiagManager.Instance.SetupInputs();
            (ModHeader quest, ModHeader[] mods) = DiagManager.Instance.LoadModSettings();
            List<int> order = new List<int>();
            List<(ModRelationship, List<ModHeader>)> errors = new List<(ModRelationship, List<ModHeader>)>();
            PathMod.ValidateModLoad(quest, mods, order, errors);
            PathMod.SetMods(quest, mods, order);
            foreach ((ModRelationship relationship, List<ModHeader> involved) in errors)
            {
                string warning = DescribeModLoadError(relationship, involved);
                DiagManager.Instance.LogInfo("Mod load warning: " + warning);
                Log.Warn("PMDO-ENGINE", "Mod load warning: " + warning);
            }
            DiagManager.Instance.PrintModSettings();

            Text.Init();
            Text.SetCultureCode(string.IsNullOrEmpty(DiagManager.Instance.CurSettings.Language) ? "" : DiagManager.Instance.CurSettings.Language);
            Log.Info("PMDO-ENGINE", "Startup configuration complete; creating game host.");
            return new AndroidGameBase();
        }

        private static void RemoveUnsupportedControllerLabels()
        {
            if (!Directory.Exists(DiagManager.CONTROLS_LABEL_PATH)) return;
            foreach (string path in Directory.EnumerateFiles(DiagManager.CONTROLS_LABEL_PATH, "*.xml"))
            {
                XDocument document = XDocument.Load(path);
                bool changed = false;
                foreach (XElement element in document.Descendants("Button").ToArray())
                {
                    string name = element.Attribute("name")?.Value;
                    if (!Enum.TryParse(name, out Buttons _))
                    {
                        element.Remove();
                        changed = true;
                    }
                }
                if (changed) document.Save(path);
            }
        }

        private static string DescribeModLoadError(ModRelationship relationship, List<ModHeader> involved)
        {
            if (relationship == ModRelationship.Incompatible && involved.Count >= 2)
                return involved[0].Name + " is incompatible with " + involved[1].Name + ".";
            if (relationship == ModRelationship.DependsOn && involved.Count >= 2)
                return String.IsNullOrEmpty(involved[1].Namespace)
                    ? involved[0].Name + " requires PMDO " + involved[1].Version + "."
                    : involved[0].Name + " requires missing mod " + involved[1].Namespace + ".";
            return "Load-order conflict: " + String.Join(" -> ", involved.Select(header =>
                String.IsNullOrWhiteSpace(header.Name) ? header.Namespace : header.Name)) + ".";
        }
    }
}
