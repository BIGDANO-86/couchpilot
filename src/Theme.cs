using System.Drawing;
using System.Windows.Forms;

namespace CouchPilot
{
    /// <summary>
    /// A small dark theme. WinForms does not theme itself, so the colours are
    /// applied by hand. Kept in one place so the window stays consistent.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg      = Color.FromArgb(24, 26, 30);
        public static readonly Color Panel   = Color.FromArgb(32, 35, 41);
        public static readonly Color Border  = Color.FromArgb(52, 57, 66);
        public static readonly Color Text    = Color.FromArgb(232, 235, 240);
        public static readonly Color Dim     = Color.FromArgb(150, 158, 170);
        public static readonly Color Accent  = Color.FromArgb(63, 200, 228);
        public static readonly Color Good    = Color.FromArgb(104, 211, 145);
        public static readonly Color Warn    = Color.FromArgb(246, 173, 85);
        public static readonly Color Bad     = Color.FromArgb(245, 101, 101);

        public static readonly Font Body    = new Font("Segoe UI", 9.0f);
        public static readonly Font BodyBold = new Font("Segoe UI", 9.0f, FontStyle.Bold);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 11.0f);
        public static readonly Font Mono    = new Font("Consolas", 8.5f);

        public static Label Head(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = Heading,
                ForeColor = Accent,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, y)
            };
        }

        public static Label Say(string text, int x, int y, int width = 420, bool dim = false)
        {
            return new Label
            {
                Text = text,
                Font = Body,
                ForeColor = dim ? Dim : Text,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(width, 17),
                Location = new Point(x, y)
            };
        }

        public static CheckBox Check(string text, int x, int y, bool value, int width = 460)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                Font = Body,
                ForeColor = Text,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(width, 22),
                Location = new Point(x, y),
                FlatStyle = FlatStyle.Flat
            };
        }

        public static TextBox Input(int x, int y, int width, string value)
        {
            return new TextBox
            {
                Text = value ?? "",
                Font = Body,
                ForeColor = Text,
                BackColor = Panel,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(width, 23),
                Location = new Point(x, y)
            };
        }

        public static NumericUpDown Num(int x, int y, int min, int max, int value, int width = 70)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value < min ? min : (value > max ? max : value),
                Font = Body,
                ForeColor = Text,
                BackColor = Panel,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(width, 23),
                Location = new Point(x, y)
            };
        }

        public static Button Btn(string text, int x, int y, int width = 110, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                Font = primary ? BodyBold : Body,
                ForeColor = primary ? Color.FromArgb(16, 18, 21) : Text,
                BackColor = primary ? Accent : Panel,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 28),
                Location = new Point(x, y),
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        public static Panel Card(int x, int y, int width, int height)
        {
            return new Panel
            {
                BackColor = Panel,
                Location = new Point(x, y),
                Size = new Size(width, height)
            };
        }
    }
}
