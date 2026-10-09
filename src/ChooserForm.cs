using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CouchPilot
{
    /// <summary>
    /// The picker that appears when a controller is switched on and more than
    /// one frontend is available.
    ///
    /// It has to be driveable from the pad, because the whole point of the app
    /// is that you have not picked up a keyboard. D-pad or stick to move, A to
    /// choose, B to cancel, and it picks the default by itself if you do
    /// nothing, so it can never leave you stuck on a menu.
    /// </summary>
    internal sealed class ChooserForm : Form
    {
        private readonly List<FrontendEntry> _items;
        private readonly string _defaultName;
        private readonly int _timeoutSeconds;

        private int _index;
        private int _remaining;
        private PadButton _lastButtons;
        private int _lastStickX, _lastStickY;
        private DateTime _opened;

        private readonly System.Windows.Forms.Timer _input = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _countdown = new System.Windows.Forms.Timer();

        public FrontendEntry Chosen { get; private set; }

        public ChooserForm(List<FrontendEntry> items, string defaultName, int timeoutSeconds)
        {
            _items = items;
            _defaultName = defaultName;
            _timeoutSeconds = Math.Max(0, timeoutSeconds);
            _remaining = _timeoutSeconds;

            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Name, defaultName, StringComparison.OrdinalIgnoreCase))
                    _index = i;

            Build();
        }

        private void Build()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(12, 13, 16);
            Opacity = 0.97;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Default;

            _opened = DateTime.Now;

            KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Left:
                    case Keys.Up: Move(-1); break;
                    case Keys.Right:
                    case Keys.Down: Move(1); break;
                    case Keys.Enter:
                    case Keys.Space: Accept(); break;
                    case Keys.Escape: Cancel(); break;
                }
            };

            MouseClick += (s, e) =>
            {
                var hit = HitTest(e.Location);
                if (hit >= 0) { _index = hit; Accept(); }
            };

            MouseMove += (s, e) =>
            {
                var hit = HitTest(e.Location);
                if (hit >= 0 && hit != _index) { _index = hit; Invalidate(); }
            };

            _input.Interval = 90;
            _input.Tick += (s, e) => PollPad();
            _input.Start();

            if (_timeoutSeconds > 0)
            {
                _countdown.Interval = 1000;
                _countdown.Tick += (s, e) =>
                {
                    _remaining--;
                    if (_remaining <= 0) Accept();
                    else Invalidate();
                };
                _countdown.Start();
            }
        }

        private void PollPad()
        {
            try
            {
                var buttons = Native.PressedButtons();
                Native.StickDirection(out var sx, out var sy);

                // Edge detection only. Without it a held direction would scroll
                // through the whole list in a fraction of a second.
                bool Pressed(PadButton b) => buttons.HasFlag(b) && !_lastButtons.HasFlag(b);

                if (Pressed(PadButton.DPadRight) || Pressed(PadButton.DPadDown)) Move(1);
                else if (Pressed(PadButton.DPadLeft) || Pressed(PadButton.DPadUp)) Move(-1);

                if (sx != 0 && _lastStickX == 0) Move(sx > 0 ? 1 : -1);
                else if (sy != 0 && _lastStickY == 0) Move(sy > 0 ? -1 : 1);

                // Ignore the first moment so the button that woke the PC, still
                // held as the window appears, does not instantly pick something.
                var settled = (DateTime.Now - _opened).TotalMilliseconds > 450;

                if (settled && (Pressed(PadButton.A) || Pressed(PadButton.Start))) Accept();
                else if (settled && Pressed(PadButton.B)) Cancel();

                _lastButtons = buttons;
                _lastStickX = sx;
                _lastStickY = sy;
            }
            catch { }
        }

        private void Move(int delta)
        {
            if (_items.Count == 0) return;
            _index = (_index + delta + _items.Count) % _items.Count;
            StopCountdown();      // once you are driving, stop choosing for you
            Invalidate();
        }

        private void StopCountdown()
        {
            if (!_countdown.Enabled) return;
            _countdown.Stop();
            _remaining = 0;
            Invalidate();
        }

        private void Accept()
        {
            if (_items.Count > 0) Chosen = _items[Math.Max(0, Math.Min(_index, _items.Count - 1))];
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel()
        {
            Chosen = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // ---------------------------------------------------------- rendering
        private const int TileW = 300;
        private const int TileH = 190;
        private const int Gap = 26;

        private Rectangle TileRect(int i)
        {
            var total = _items.Count * TileW + (_items.Count - 1) * Gap;
            var startX = (ClientSize.Width - total) / 2;
            var y = (ClientSize.Height - TileH) / 2;
            return new Rectangle(startX + i * (TileW + Gap), y, TileW, TileH);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
                if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var title = new Font("Segoe UI Light", 26f);
            using var small = new Font("Segoe UI", 10f);
            using var tileFont = new Font("Segoe UI Semibold", 15f);
            using var hintFont = new Font("Segoe UI", 10.5f);

            var centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (var b = new SolidBrush(Color.FromArgb(235, 238, 243)))
                g.DrawString("What are we playing?", title,
                    b, new RectangleF(0, TileRect(0).Y - 130, ClientSize.Width, 44), centre);

            for (int i = 0; i < _items.Count; i++)
            {
                var r = TileRect(i);
                var selected = i == _index;

                using (var back = new SolidBrush(selected
                           ? Color.FromArgb(30, 44, 50)
                           : Color.FromArgb(28, 30, 35)))
                using (var pen = new Pen(selected
                           ? Color.FromArgb(63, 200, 228)
                           : Color.FromArgb(55, 60, 69), selected ? 3f : 1f))
                {
                    var path = Rounded(r, 16);
                    g.FillPath(back, path);
                    g.DrawPath(pen, path);
                    path.Dispose();
                }

                using (var fb = new SolidBrush(selected
                           ? Color.FromArgb(240, 245, 250)
                           : Color.FromArgb(188, 196, 208)))
                    g.DrawString(_items[i].Name, tileFont, fb,
                        new RectangleF(r.X + 12, r.Y + 12, r.Width - 24, r.Height - 24), centre);

                if (string.Equals(_items[i].Name, _defaultName, StringComparison.OrdinalIgnoreCase))
                    using (var db = new SolidBrush(Color.FromArgb(130, 140, 152)))
                        g.DrawString("default", small, db,
                            new RectangleF(r.X, r.Bottom - 34, r.Width, 20), centre);
            }

            var hint = _remaining > 0
                ? $"D-pad to move    A to choose    B to cancel    opening {_defaultName} in {_remaining}s"
                : "D-pad to move    A to choose    B to cancel";

            using (var hb = new SolidBrush(Color.FromArgb(140, 149, 162)))
                g.DrawString(hint, hintFont, hb,
                    new RectangleF(0, TileRect(0).Bottom + 54, ClientSize.Width, 26), centre);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _input.Stop(); _input.Dispose(); } catch { }
            try { _countdown.Stop(); _countdown.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
