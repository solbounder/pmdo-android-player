using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework.Input;
using PMDO.Portable;
using RogueEssence;

namespace PMDO.Android;

internal static class TouchOverlay
{
    private const string Preferences = "pmdo-touch-layout";
    private const string LayoutKey = "TouchLayoutV1";
    private static readonly Dictionary<string, Buttons> Bindings = new(StringComparer.Ordinal)
    {
        ["A"] = Buttons.A, ["B"] = Buttons.B, ["X"] = Buttons.X, ["Y"] = Buttons.Y,
        ["L"] = Buttons.LeftShoulder, ["R"] = Buttons.RightShoulder, ["ZL"] = Buttons.LeftTrigger, ["ZR"] = Buttons.RightTrigger,
        ["Start"] = Buttons.Start, ["Select"] = Buttons.Back, ["L3"] = Buttons.LeftStick, ["R3"] = Buttons.RightStick
    };

    private static Editor current;

    public static View Create(Activity activity)
    {
        current = new Editor(activity);
        return current.Root;
    }

    public static void ReleaseAll() => current?.ReleaseAllTouches();

    private sealed class Editor
    {
        private readonly Activity activity;
        private readonly FrameLayout canvas;
        private readonly Dictionary<string, View> views = new(StringComparer.Ordinal);
        private readonly TouchHoldState<View, Buttons> heldTouches = new();
        private TouchLayoutV1 saved;
        private TouchLayoutV1 working;
        private bool editing;
        private View editorBar;
        public FrameLayout Root { get; }

        public Editor(Activity activity)
        {
            this.activity = activity;
            saved = TouchLayoutStorage.DeserializeOrDefault(activity.GetSharedPreferences(Preferences, FileCreationMode.Private).GetString(LayoutKey, null));
            working = saved;
            Root = new FrameLayout(activity); canvas = new FrameLayout(activity);
            Root.AddView(canvas, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            AddGear(); AddKeyboard(); Render();
        }

        private void AddGear()
        {
            var gear = Button("⚙"); gear.Click += (_, _) => ToggleEditor();
            Root.AddView(gear, new FrameLayout.LayoutParams(Px(42), Px(36), GravityFlags.Top | GravityFlags.Right) { TopMargin = Px(4), RightMargin = Px(4) });
        }

        private void AddKeyboard()
        {
            var keyboard = Button("⌨");
            keyboard.Click += (_, _) =>
            {
                var input = new EditText(activity)
                {
                    InputType = global::Android.Text.InputTypes.ClassText |
                        global::Android.Text.InputTypes.TextFlagCapSentences
                };
                input.SetSingleLine(true);
                var dialog = new AlertDialog.Builder(activity)
                    .SetTitle("Text eingeben")
                    .SetView(input)
                    .SetNegativeButton("Abbrechen", (_, _) => { })
                    .SetPositiveButton("Einfügen", (_, _) =>
                    {
                        var clipboard = (global::Android.Content.ClipboardManager)
                            activity.GetSystemService(Context.ClipboardService)!;
                        clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText(
                            "PMDO", input.Text ?? String.Empty);
                        TouchController.Clear();
                        TouchController.Set(Buttons.LeftStick, true);
                        new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper)
                            .PostDelayed(() => TouchController.Set(Buttons.LeftStick, false), 180);
                    }).Create();
                dialog.Show();
                input.RequestFocus();
                dialog.Window?.SetSoftInputMode(
                    global::Android.Views.SoftInput.StateAlwaysVisible);
            };
            Root.AddView(keyboard, new FrameLayout.LayoutParams(Px(42), Px(36),
                GravityFlags.Top | GravityFlags.Right) { TopMargin = Px(44), RightMargin = Px(4) });
        }

        private void ToggleEditor()
        {
            editing = !editing;
            if (!editing) { working = saved; }
            Render();
        }

        private void Render()
        {
            ReleaseAllTouches();
            if (editorBar is not null) { Root.RemoveView(editorBar); editorBar = null; }
            canvas.RemoveAllViews(); views.Clear();
            AddDPad();
            foreach (TouchControlLayout control in working.Buttons) AddButton(control);
            if (editing) AddEditorBar();
        }

        private void AddDPad()
        {
            TouchControlLayout control = working.DPad; if (!control.Visible) return;
            var grid = new GridLayout(activity) { RowCount = 3, ColumnCount = 3, Alpha = .7f };
            AddDPadKey(grid, "↑", Buttons.DPadUp, 0, 1, control.Scale); AddDPadKey(grid, "←", Buttons.DPadLeft, 1, 0, control.Scale); AddDPadKey(grid, "→", Buttons.DPadRight, 1, 2, control.Scale); AddDPadKey(grid, "↓", Buttons.DPadDown, 2, 1, control.Scale);
            AddControl(control, grid, Px((int)(150 * control.Scale)), Px((int)(150 * control.Scale)));
        }

        private void AddDPadKey(GridLayout grid, string label, Buttons key, int row,
            int col, float scale)
        {
            var view = Button(label);
            view.Touch += (_, e) => { if (editing) HandleEditTouch("dpad", grid, e); else HandleGameTouch(view, key, e); };
            grid.AddView(view, new GridLayout.LayoutParams(GridLayout.InvokeSpec(row),
                GridLayout.InvokeSpec(col)) { Width = Px((int)(48 * scale)), Height = Px((int)(48 * scale)) });
        }

        private void AddButton(TouchControlLayout control)
        {
            if (!control.Visible) return;
            var view = Button(control.Binding); view.Alpha = .7f;
            view.Touch += (_, e) =>
            {
                if (editing) { HandleEditTouch(control.Id, view, e); return; }
                HandleGameTouch(view, Bindings[control.Binding], e);
            };
            AddControl(control, view, Px((int)(64 * control.Scale)), Px((int)(52 * control.Scale)));
        }

        private void AddControl(TouchControlLayout control, View view, int width, int height)
        {
            views[control.Id] = view;
            var p = new FrameLayout.LayoutParams(width, height) { LeftMargin = (int)(control.X * Math.Max(1, canvas.Width)), TopMargin = (int)(control.Y * Math.Max(1, canvas.Height)) };
            canvas.AddView(view, p);
            canvas.Post(() => { if (view.LayoutParameters is FrameLayout.LayoutParams lp) { lp.LeftMargin = (int)(control.X * Math.Max(0, canvas.Width - view.Width)); lp.TopMargin = (int)(control.Y * Math.Max(0, canvas.Height - view.Height)); view.LayoutParameters = lp; } });
            if (editing && control.Id == "dpad")
                view.Touch += (_, e) => HandleEditTouch(control.Id, view, e);
        }

        private float downX, downY;
        private long downAt;
        private bool dragged;
        private void HandleEditTouch(string id, View view, View.TouchEventArgs e)
        {
            if (e.Event.ActionMasked == MotionEventActions.Down)
            {
                downX = e.Event.RawX; downY = e.Event.RawY;
                downAt = global::Android.OS.SystemClock.ElapsedRealtime(); dragged = false;
                e.Handled = true; return;
            }
            if (e.Event.ActionMasked == MotionEventActions.Move)
            {
                var lp = (FrameLayout.LayoutParams)view.LayoutParameters;
                float dx = e.Event.RawX - downX, dy = e.Event.RawY - downY;
                if (Math.Abs(dx) + Math.Abs(dy) > 3) dragged = true;
                lp.LeftMargin = Math.Clamp(lp.LeftMargin + (int)dx, 0, Math.Max(0, canvas.Width - view.Width)); lp.TopMargin = Math.Clamp(lp.TopMargin + (int)dy, 0, Math.Max(0, canvas.Height - view.Height));
                downX = e.Event.RawX; downY = e.Event.RawY; view.LayoutParameters = lp; e.Handled = true;
                UpdatePosition(id, lp.LeftMargin / (float)Math.Max(1, canvas.Width - view.Width), lp.TopMargin / (float)Math.Max(1, canvas.Height - view.Height));
            }
            else if (e.Event.ActionMasked == MotionEventActions.Up && !dragged &&
                global::Android.OS.SystemClock.ElapsedRealtime() - downAt >= 600)
                Configure(id);
            e.Handled = true;
        }

        private void Configure(string id)
        {
            TouchControlLayout item = id == "dpad" ? working.DPad : working.Buttons.First(x => x.Id == id);
            var box = new LinearLayout(activity) { Orientation = Orientation.Vertical }; var visible = new CheckBox(activity) { Text = "Sichtbar", Checked = item.Visible }; box.AddView(visible);
            Spinner binding = new Spinner(activity); var names = id == "dpad" ? new[] { "DPad" } : TouchLayoutStorage.AllowedBindings; binding.Adapter = new ArrayAdapter<string>(activity, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, names); binding.SetSelection(Array.IndexOf(names, item.Binding)); box.AddView(binding);
            var scale = new SeekBar(activity) { Max = 15, Progress = (int)Math.Round((item.Scale - .5f) * 10) }; box.AddView(scale);
            new AlertDialog.Builder(activity).SetTitle(id == "dpad" ? "D-Pad bearbeiten" : "Taste bearbeiten (lang drücken)").SetView(box).SetNegativeButton("Abbrechen", (_, _) => { }).SetPositiveButton("Übernehmen", (_, _) => UpdateControl(id, item with { Binding = id == "dpad" ? "DPad" : names[binding.SelectedItemPosition], Visible = visible.Checked, Scale = .5f + scale.Progress / 10f })).Show();
        }

        private void AddEditorBar()
        {
            var bar = new LinearLayout(activity) { Orientation = Orientation.Horizontal }; bar.SetBackgroundColor(Color.Argb(220, 25, 25, 35));
            AddBarButton(bar, "Speichern", () => { saved = TouchLayoutStorage.Normalize(working); activity.GetSharedPreferences(Preferences, FileCreationMode.Private).Edit().PutString(LayoutKey, TouchLayoutStorage.Serialize(saved)).Apply(); editing = false; Render(); });
            AddBarButton(bar, "Abbrechen", () => { working = saved; editing = false; Render(); });
            AddBarButton(bar, "Zurücksetzen", () => { working = TouchLayoutV1.Default; Render(); });
            AddBarButton(bar, "Tasten", ChooseControl);
            Root.AddView(bar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Px(42), GravityFlags.Top | GravityFlags.CenterHorizontal));
            editorBar = bar;
        }

        private void ChooseControl()
        {
            string[] ids = new[] { "dpad" }.Concat(working.Buttons.Select(x => x.Id)).ToArray();
            new AlertDialog.Builder(activity).SetTitle("Auch ausgeblendete Taste bearbeiten").SetItems(ids, (_, e) => Configure(ids[e.Which])).Show();
        }

        private void AddBarButton(LinearLayout bar, string text, Action action) { var b = Button(text); b.Click += (_, _) => action(); bar.AddView(b, new LinearLayout.LayoutParams(Px(92), Px(42))); }
        private void UpdatePosition(string id, float x, float y) => UpdateControl(id, Get(id) with { X = x, Y = y }, false);
        private TouchControlLayout Get(string id) => id == "dpad" ? working.DPad : working.Buttons.First(x => x.Id == id);
        private void UpdateControl(string id, TouchControlLayout value, bool render = true) { working = id == "dpad" ? working with { DPad = value } : working with { Buttons = working.Buttons.Select(x => x.Id == id ? value : x).ToArray() }; if (render) Render(); }
        private void HandleGameTouch(View source, Buttons button, View.TouchEventArgs e)
        {
            bool down = e.Event.ActionMasked is MotionEventActions.Down or MotionEventActions.PointerDown;
            bool up = e.Event.ActionMasked is MotionEventActions.Up or MotionEventActions.PointerUp or MotionEventActions.Cancel;
            if (down && heldTouches.Press(source, button))
            {
                TouchController.Set(button, true);
            }
            else if (up && heldTouches.Release(source, out Buttons held))
            {
                TouchController.Set(held, false);
            }
            e.Handled = true;
        }

        public void ReleaseAllTouches()
        {
            foreach (Buttons button in heldTouches.Reset()) TouchController.Set(button, false);
        }
        private Button Button(string text) { var b = new Button(activity) { Text = text, TextSize = 10 }; b.SetPadding(0, 0, 0, 0); b.SetMinimumWidth(0); b.SetMinimumHeight(0); b.SetTextColor(Color.White); b.SetBackgroundColor(Color.Argb(100, 20, 20, 28)); return b; }
        private int Px(int value) => (int)(value * activity.Resources.DisplayMetrics.Density);
    }
}
