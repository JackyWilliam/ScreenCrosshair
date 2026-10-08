using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ScreenCrosshair
{
    public sealed class SliderRow : UserControl
    {
        readonly DecimalTrack slider;
        readonly TextBox input;
        readonly double minimum, maximum;
        double current;
        bool loading;
        public event EventHandler ValueChanged;
        public double Value
        {
            get { return current; }
            set { SetValue(value, true); }
        }
        public SliderRow(string title, double min, double max)
        {
            Size = new Size(486, 62); BackColor = Theme.Card; ForeColor = Theme.Text;
            minimum = min; maximum = max; current = min;
            Controls.Add(Theme.Label(title, new Rectangle(0, 0, 240, 23)));
            Controls.Add(Theme.Label("px", new Rectangle(461, 25, 25, 28), true));
            input = Theme.Input(new Rectangle(371, 25, 82, 28)); input.TextAlign = HorizontalAlignment.Right;
            input.AccessibleName = title + "（像素，支持小数）"; input.MaxLength = 12; Controls.Add(input);
            slider = new DecimalTrack(min, max) { Bounds = new Rectangle(0, 27, 353, 28), AccessibleName = title + "滑块" };
            slider.Changed += delegate { SetValue(slider.Value, true); }; Controls.Add(slider);
            input.TextChanged += delegate
            {
                if (loading) return;
                double number;
                bool valid = double.TryParse(input.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)
                    && !double.IsNaN(number) && !double.IsInfinity(number) && number >= minimum && number <= maximum;
                input.ForeColor = valid ? Theme.Text : Theme.Error;
                // Incomplete input (e.g. an empty field or trailing decimal point) must not destroy the last valid value.
                if (valid) SetValue(number, false);
            };
            input.Leave += delegate { SetValue(current, true); };
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) { SetValue(current, true); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { SetValue(current + (e.KeyCode == Keys.Up ? .1 : -.1), true); e.SuppressKeyPress = true; }
            };
            SetValue(min, true);
        }
        void SetValue(double value, bool format)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            double next = Math.Round(Math.Max(minimum, Math.Min(maximum, value)), 2);
            bool changed = current != next; current = next; slider.Value = current;
            if (format) { loading = true; input.Text = current.ToString("0.##", CultureInfo.InvariantCulture); input.ForeColor = Theme.Text; loading = false; }
            if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        sealed class DecimalTrack : Control
        {
            readonly double min, max;
            double value;
            public event EventHandler Changed;
            public double Value { get { return value; } set { this.value = value; Invalidate(); } }
            public DecimalTrack(double minimum, double maximum)
            {
                min = minimum; max = maximum; TabStop = true; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            void Pick(int x)
            {
                Value = Math.Round(Math.Max(min, Math.Min(max, min + (x - 7.0) / (Width - 14) * (max - min))), 1);
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; Pick(e.X); } }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Pick(e.X); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
            protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
                {
                    Value = e.KeyCode == Keys.Home ? min : e.KeyCode == Keys.End ? max : Math.Round(Math.Max(min, Math.Min(max, value + (e.KeyCode == Keys.Right ? .1 : -.1))), 2);
                    if (Changed != null) Changed(this, EventArgs.Empty); e.Handled = true;
                }
            }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float x = 7 + (float)((value - min) / (max - min) * (Width - 14)), y = Height / 2F;
                using (var pen = new Pen(Theme.Border, 4)) e.Graphics.DrawLine(pen, 7, y, Width - 7, y);
                using (var pen = new Pen(Enabled ? Theme.Accent : Theme.Muted, 4)) e.Graphics.DrawLine(pen, 7, y, x, y);
                if (Focused) using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawEllipse(pen, x - 9, y - 9, 18, 18);
                using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted)) e.Graphics.FillEllipse(brush, x - 6, y - 6, 12, 12);
            }
        }
    }
}
