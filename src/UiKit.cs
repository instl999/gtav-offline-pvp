using System;
using System.Collections.Generic;
using GTA;
using Color = System.Drawing.Color;
using Keys = System.Windows.Forms.Keys;
using PointF = System.Drawing.PointF;
using SizeF = System.Drawing.SizeF;

namespace OfflinePvP
{
    /// <summary>Thin drawing helpers on SHVDN's 1280x720 UI space. No NativeUI/LemonUI dependency.</summary>
    internal static class Draw
    {
        public static void Rect(float x, float y, float w, float h, Color c)
        {
            new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), c).Draw();
        }

        public static void Text(string s, float x, float y, float scale, Color c, GTA.UI.Alignment align)
        {
            Text(s, x, y, scale, c, align, GTA.UI.Font.ChaletLondon);
        }

        public static void Text(string s, float x, float y, float scale, Color c, GTA.UI.Alignment align, GTA.UI.Font font)
        {
            GTA.UI.TextElement t = new GTA.UI.TextElement(s, new PointF(x, y), scale, c, font, align);
            t.Shadow = true;
            t.Outline = true;
            t.Draw();
        }
    }

    internal sealed class MenuItem
    {
        public string Label;
        public string Help;
        public Func<string> Value;      // null = plain action item
        public Action<int> Change;      // called with -1 / +1 on left/right
        public Action Activate;         // called on Enter
        public Func<bool> Enabled;      // null = always enabled

        public bool IsEnabled { get { return Enabled == null || Enabled(); } }
    }

    internal sealed class SimpleMenu
    {
        const float X = 30f, Y = 50f, W = 400f, HeaderH = 46f, RowH = 25f;

        public string Title;
        public string Subtitle;
        public readonly List<MenuItem> Items = new List<MenuItem>();
        public bool Visible;
        int index;

        public SimpleMenu(string title)
        {
            Title = title;
        }

        public MenuItem Add(string label, string help, Func<string> value, Action<int> change, Func<bool> enabled)
        {
            MenuItem it = new MenuItem { Label = label, Help = help, Value = value, Change = change, Enabled = enabled };
            Items.Add(it);
            return it;
        }

        public MenuItem AddAction(string label, string help, Action activate, Func<bool> enabled)
        {
            MenuItem it = new MenuItem { Label = label, Help = help, Activate = activate, Enabled = enabled };
            Items.Add(it);
            return it;
        }

        public void Tick()
        {
            if (!Visible) return;

            // Arrow keys / Enter / Backspace double as phone and frontend inputs in vanilla GTA.
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
            Game.DisableControlThisFrame(Control.CharacterWheel);
            Game.DisableControlThisFrame(Control.SelectWeapon);

            float y = Y;
            Draw.Rect(X, y, W, HeaderH, Color.FromArgb(230, 180, 20, 30));
            Draw.Text(Title, X + W / 2f, y + 4f, 0.9f, Color.White, GTA.UI.Alignment.Center, GTA.UI.Font.HouseScript);
            y += HeaderH;

            Draw.Rect(X, y, W, RowH, Color.FromArgb(240, 0, 0, 0));
            Draw.Text(Subtitle ?? "", X + 8f, y + 3f, 0.33f, Color.FromArgb(255, 93, 182, 229), GTA.UI.Alignment.Left);
            Draw.Text(string.Format("{0}/{1}", index + 1, Items.Count), X + W - 8f, y + 3f, 0.33f, Color.White, GTA.UI.Alignment.Right);
            y += RowH;

            for (int i = 0; i < Items.Count; i++)
            {
                MenuItem it = Items[i];
                bool sel = i == index;
                bool en = it.IsEnabled;
                Draw.Rect(X, y, W, RowH, sel ? Color.FromArgb(235, 240, 240, 240) : Color.FromArgb(170, 0, 0, 0));
                Color fg = sel ? Color.Black : Color.White;
                if (!en) fg = Color.FromArgb(255, 120, 120, 120);
                Draw.Text(it.Label, X + 8f, y + 2f, 0.36f, fg, GTA.UI.Alignment.Left);
                if (it.Value != null)
                {
                    string v = it.Value();
                    if (sel && en && it.Change != null) v = "< " + v + " >";
                    Draw.Text(v, X + W - 8f, y + 2f, 0.36f, fg, GTA.UI.Alignment.Right);
                }
                y += RowH;
            }

            MenuItem cur = Items.Count > 0 ? Items[index] : null;
            if (cur != null && !string.IsNullOrEmpty(cur.Help))
            {
                y += 4f;
                Draw.Rect(X, y, W, RowH + 4f, Color.FromArgb(200, 0, 0, 0));
                Draw.Text(cur.IsEnabled ? cur.Help : "Locked while a match is running.", X + 8f, y + 4f, 0.3f, Color.White, GTA.UI.Alignment.Left);
            }
        }

        /// <summary>Returns true when the key was consumed.</summary>
        public bool HandleKey(Keys key)
        {
            if (!Visible || Items.Count == 0) return false;
            MenuItem cur = Items[index];
            switch (key)
            {
                case Keys.Up:
                case Keys.NumPad8:
                    index = (index - 1 + Items.Count) % Items.Count;
                    Sound("NAV_UP_DOWN");
                    return true;
                case Keys.Down:
                case Keys.NumPad2:
                    index = (index + 1) % Items.Count;
                    Sound("NAV_UP_DOWN");
                    return true;
                case Keys.Left:
                case Keys.NumPad4:
                    if (cur.IsEnabled && cur.Change != null) { cur.Change(-1); Sound("NAV_LEFT_RIGHT"); }
                    return true;
                case Keys.Right:
                case Keys.NumPad6:
                    if (cur.IsEnabled && cur.Change != null) { cur.Change(1); Sound("NAV_LEFT_RIGHT"); }
                    return true;
                case Keys.Enter:
                case Keys.NumPad5:
                    if (cur.IsEnabled && cur.Activate != null) { Sound("SELECT"); cur.Activate(); }
                    return true;
                case Keys.Back:
                case Keys.NumPad0:
                    Visible = false;
                    Sound("BACK");
                    return true;
            }
            return false;
        }

        static void Sound(string name)
        {
            Audio.PlaySoundFrontend(name, "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }
    }

    /// <summary>Online-style full screen "shard" messages (WASTED, WINNER, ...).</summary>
    internal sealed class BigMessage
    {
        Scaleform sf;
        int until;

        public void Show(string fn, string title, string subtitle, int durationMs)
        {
            if (sf == null) sf = new Scaleform("MP_BIG_MESSAGE_FREEMODE");
            until = Game.GameTime + durationMs;
            pendingFn = fn;
            pendingTitle = title;
            pendingSub = subtitle;
            sent = false;
        }

        string pendingFn, pendingTitle, pendingSub;
        bool sent;

        public void Tick()
        {
            if (sf == null || Game.GameTime > until) return;
            if (!sf.IsLoaded) return;
            if (!sent)
            {
                sf.CallFunction(pendingFn, pendingTitle, pendingSub);
                sent = true;
            }
            sf.Render2D();
        }

        public void Hide()
        {
            until = 0;
        }
    }
}
