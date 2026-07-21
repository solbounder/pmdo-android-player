using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework.Input;
using RogueEssence;

namespace PMDO.Android
{
    internal static class TouchOverlay
    {
        public static View Create(Activity activity)
        {
            FrameLayout overlay = new FrameLayout(activity);
            LinearLayout bar = new LinearLayout(activity) { Orientation = Orientation.Horizontal, Alpha = 0.62f };
            bar.SetGravity(GravityFlags.Bottom | GravityFlags.CenterHorizontal);
            bar.SetBackgroundColor(Color.Transparent);

            GridLayout dpad = new GridLayout(activity) { RowCount = 3, ColumnCount = 3 };
            Add(dpad, activity, "↑", Buttons.DPadUp, 0, 1);
            Add(dpad, activity, "←", Buttons.DPadLeft, 1, 0);
            Add(dpad, activity, "→", Buttons.DPadRight, 1, 2);
            Add(dpad, activity, "↓", Buttons.DPadDown, 2, 1);

            GridLayout middle = new GridLayout(activity) { RowCount = 2, ColumnCount = 3 };
            Add(middle, activity, "L", Buttons.LeftShoulder, 0, 0);
            Add(middle, activity, "SELECT", Buttons.Back, 1, 0);
            Add(middle, activity, "START", Buttons.Start, 1, 1);
            Add(middle, activity, "R", Buttons.RightShoulder, 0, 2);

            GridLayout actions = new GridLayout(activity) { RowCount = 3, ColumnCount = 3 };
            Add(actions, activity, "Y", Buttons.Y, 0, 1);
            Add(actions, activity, "X", Buttons.X, 1, 0);
            Add(actions, activity, "B", Buttons.B, 1, 2);
            Add(actions, activity, "A", Buttons.A, 2, 1);

            bar.AddView(dpad, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bar.AddView(middle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bar.AddView(actions, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            overlay.AddView(bar, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

            Button keyboard = CreateKeyboard(activity);
            float density = activity.Resources.DisplayMetrics.Density;
            FrameLayout.LayoutParams keyboardLayout = new FrameLayout.LayoutParams(
                (int)(46 * density), (int)(20 * density), GravityFlags.Top | GravityFlags.CenterHorizontal)
            {
                TopMargin = (int)(1 * density)
            };
            overlay.AddView(keyboard, keyboardLayout);
            return overlay;
        }

        private static void Add(GridLayout grid, Activity activity, string label, Buttons button, int row, int column)
        {
            Button view = new Button(activity) { Text = label };
            view.SetSingleLine(true);
            view.TextSize = 10;
            view.SetPadding(0, 0, 0, 0);
            view.SetMinimumWidth(0);
            view.SetMinimumHeight(0);
            view.SetTextColor(Color.White);
            view.SetBackgroundColor(Color.Argb(90, 20, 20, 28));
            view.Touch += (_, args) =>
            {
                bool down = args.Event.ActionMasked == MotionEventActions.Down || args.Event.ActionMasked == MotionEventActions.PointerDown;
                bool up = args.Event.ActionMasked == MotionEventActions.Up || args.Event.ActionMasked == MotionEventActions.PointerUp || args.Event.ActionMasked == MotionEventActions.Cancel;
                if (down || up) TouchController.Set(button, down);
                args.Handled = true;
            };
            GridLayout.LayoutParams parameters = new GridLayout.LayoutParams(GridLayout.InvokeSpec(row), GridLayout.InvokeSpec(column))
            {
                Width = (int)(58 * activity.Resources.DisplayMetrics.Density),
                Height = (int)(48 * activity.Resources.DisplayMetrics.Density)
            };
            grid.AddView(view, parameters);
        }

        private static Button CreateKeyboard(Activity activity)
        {
            Button view = new Button(activity) { Text = "⌨", TextSize = 11 };
            view.SetPadding(0, 0, 0, 0);
            view.SetMinimumWidth(0);
            view.SetMinimumHeight(0);
            view.SetTextColor(Color.White);
            view.SetBackgroundColor(Color.Argb(90, 20, 20, 28));
            view.Click += (_, _) =>
            {
                EditText input = new EditText(activity)
                {
                    InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences
                };
                input.SetSingleLine(true);
                AlertDialog dialog = new AlertDialog.Builder(activity)
                    .SetTitle("Text eingeben")
                    .SetView(input)
                    .SetNegativeButton("Abbrechen", (_, _) => { })
                    .SetPositiveButton("Einfügen", (_, _) =>
                    {
                        global::Android.Content.ClipboardManager clipboard = (global::Android.Content.ClipboardManager)activity.GetSystemService(Context.ClipboardService);
                        clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText("PMDO", input.Text ?? string.Empty);
                        TouchController.Clear();
                        TouchController.Set(Buttons.LeftStick, true);
                        new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper).PostDelayed(
                            () => TouchController.Set(Buttons.LeftStick, false), 180);
                    })
                    .Create();
                dialog.Show();
                input.RequestFocus();
                dialog.Window?.SetSoftInputMode(SoftInput.StateAlwaysVisible);
            };
            return view;
        }
    }
}
