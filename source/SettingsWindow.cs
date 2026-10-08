using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenCrosshair
{
    public sealed class SliderRow : UserControl
    {
        readonly TrackBar slider;
        readonly Label value;
        public event EventHandler ValueChanged;
        public int Value { get { return slider.Value; } set { slider.Value = value; } }
        public SliderRow(string title, int min, int max)
        {
            Size = new Size(380, 54);
            Controls.Add(new Label { Text = title, Bounds = new Rectangle(0, 0, 200, 24) });
            value = new Label { TextAlign = ContentAlignment.MiddleRight, Bounds = new Rectangle(284, 0, 80, 24) }; Controls.Add(value);
            slider = new TrackBar { Minimum = min, Maximum = max, Value = min, AutoSize = false, TickStyle = TickStyle.None, Bounds = new Rectangle(-7, 23, 380, 30), SmallChange = 1, LargeChange = 2 };
            slider.ValueChanged += delegate { this.value.Text = slider.Value + " px"; if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); };
            Controls.Add(slider); value.Text = min + " px";
        }
    }

    public class SettingsWindow : Form
    {
        readonly Overlay overlay;
        readonly Preview preview;
        readonly ComboBox shape = new ComboBox(), color = new ComboBox(), monitor = new ComboBox(), language = new ComboBox(), interval = new ComboBox();
        readonly SliderRow length = new SliderRow("长度 / 半径", 2, 32), gap = new SliderRow("中心间隔", 0, 20), thickness = new SliderRow("线条粗细", 1, 8);
        readonly CheckBox outline = new CheckBox(), dot = new CheckBox(), automatic = new CheckBox();
        readonly TextBox hex = new TextBox();
        readonly Button pickColor = new Button(), chooseRegion = new Button();
        readonly Label status = new Label(), recognitionStatus = new Label(), regionStatus = new Label();
        readonly System.Windows.Forms.Timer statusTimer;
        bool loading, choosing;
        Screen[] screens;

        public SettingsWindow(Overlay owner)
        {
            overlay = owner;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "屏幕准星 · 调整"; Icon = owner.Icon; ClientSize = new Size(720, 700);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 252);
            Controls.Add(new Label { Text = "屏幕准星", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Bounds = new Rectangle(24, 16, 400, 42) });
            Controls.Add(new Label { Text = "即时预览 · 自定义颜色 · Apex 武器样式", Bounds = new Rectangle(25, 61, 650, 26), ForeColor = Color.DimGray });
            preview = new Preview(owner.Config) { Bounds = new Rectangle(24, 102, 248, 250) }; Controls.Add(preview);
            Controls.Add(new Label { Text = "手动 / 普通武器样式预览", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Bounds = new Rectangle(24, 361, 248, 25) });
            Controls.Add(new Label { Text = "普通样式", Bounds = new Rectangle(300, 102, 88, 26) });
            Combo(shape, new Rectangle(395, 99, 301, 30));
            shape.Items.AddRange(new object[] { "十字", "圆点", "圆环", "霰弹枪：圆环＋中心点", "狙击枪：无下方竖线" });
            Combo(color, new Rectangle(300, 140, 122, 30)); color.Items.AddRange(Settings.ColorNames); color.Items.Add("自定义");
            hex.SetBounds(432, 140, 116, 30); hex.MaxLength = 7; Controls.Add(hex);
            pickColor.Text = "选颜色…"; pickColor.SetBounds(560, 139, 136, 31); Controls.Add(pickColor);
            length.Location = new Point(307, 182); gap.Location = new Point(307, 240); thickness.Location = new Point(307, 298);
            Controls.Add(length); Controls.Add(gap); Controls.Add(thickness);
            outline.Text = "黑色描边"; outline.SetBounds(306, 360, 150, 30);
            dot.Text = "中心点"; dot.SetBounds(478, 360, 150, 30); Controls.Add(outline); Controls.Add(dot);

            var autoPanel = new Panel { Bounds = new Rectangle(24, 407, 672, 190), BackColor = Color.FromArgb(234, 239, 247) }; Controls.Add(autoPanel);
            automatic.Text = "Apex 自动识别武器"; automatic.SetBounds(14, 10, 230, 28); autoPanel.Controls.Add(automatic);
            chooseRegion.Text = "框选两个枪名…"; chooseRegion.SetBounds(476, 9, 180, 31); autoPanel.Controls.Add(chooseRegion);
            autoPanel.Controls.Add(new Label { Text = "游戏语言", Bounds = new Rectangle(16, 52, 77, 25) });
            Combo(language, new Rectangle(95, 48, 180, 30), autoPanel); language.Items.AddRange(new object[] { "简体中文", "English" });
            autoPanel.Controls.Add(new Label { Text = "检测间隔", Bounds = new Rectangle(316, 52, 82, 25) });
            Combo(interval, new Rectangle(402, 48, 254, 30), autoPanel); interval.Items.AddRange(new object[] { "0.5 秒（切换较快）", "1 秒（默认）", "2 秒（减少识别次数）" });
            regionStatus.SetBounds(16, 88, 640, 25); regionStatus.ForeColor = Color.DimGray; autoPanel.Controls.Add(regionStatus);
            recognitionStatus.SetBounds(16, 116, 640, 48); recognitionStatus.ForeColor = Color.FromArgb(35, 65, 94); autoPanel.Controls.Add(recognitionStatus);
            autoPanel.Controls.Add(new Label { Text = "霰弹枪 → 圆环＋中心点；狙击枪 → 无下方竖线；其余 → 普通样式", Bounds = new Rectangle(16, 165, 640, 22), Font = new Font(Font.FontFamily, 9F) });

            Controls.Add(new Label { Text = "显示器", Bounds = new Rectangle(24, 619, 66, 26) });
            Combo(monitor, new Rectangle(94, 615, 338, 30));
            var reset = new Button { Text = "重置外观", Bounds = new Rectangle(449, 614, 119, 32) };
            reset.Click += delegate
            {
                Settings s = overlay.Config; s.Length = 8; s.Gap = 4; s.Thickness = 2; s.Shape = 0; s.ColorIndex = 0; s.ColorHex = ""; s.Outline = true; s.CenterDot = false;
                Reload(); overlay.Changed();
            }; Controls.Add(reset);
            var done = new Button { Text = "完成", Bounds = new Rectangle(579, 614, 117, 32) }; done.Click += delegate { Close(); }; Controls.Add(done);
            status.SetBounds(24, 665, 672, 26); status.Font = new Font(Font.FontFamily, 8.5F); status.ForeColor = Color.DimGray; Controls.Add(status);
            shape.SelectedIndexChanged += UpdateSettings; monitor.SelectedIndexChanged += UpdateSettings;
            language.SelectedIndexChanged += UpdateSettings; interval.SelectedIndexChanged += UpdateSettings; automatic.CheckedChanged += UpdateSettings;
            length.ValueChanged += UpdateSettings; gap.ValueChanged += UpdateSettings; thickness.ValueChanged += UpdateSettings;
            outline.CheckedChanged += UpdateSettings; dot.CheckedChanged += UpdateSettings;
            color.SelectedIndexChanged += delegate
            {
                if (loading) return;
                if (color.SelectedIndex < Settings.Colors.Length) { overlay.Config.ColorIndex = color.SelectedIndex; overlay.Config.ColorHex = ""; }
                else overlay.Config.ColorHex = Settings.ToHex(overlay.Config.ForegroundColor);
                RefreshColor(); overlay.Changed(); preview.Invalidate();
            };
            hex.TextChanged += delegate
            {
                if (loading) return;
                Color custom;
                bool valid = Settings.TryParseColor(hex.Text, out custom);
                hex.BackColor = valid ? Color.White : Color.FromArgb(255, 233, 233);
                if (!valid) return;
                overlay.Config.ColorHex = Settings.ToHex(custom);
                loading = true; color.SelectedIndex = 6; loading = false;
                pickColor.BackColor = custom; pickColor.ForeColor = custom.GetBrightness() < 0.5 ? Color.White : Color.Black;
                overlay.Changed(); preview.Invalidate();
            };
            pickColor.Click += delegate
            {
                using (var dialog = new ColorDialog { Color = overlay.Config.ForegroundColor, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        overlay.Config.ColorHex = Settings.ToHex(dialog.Color); RefreshColor(); overlay.Changed(); preview.Invalidate();
                    }
            };
            chooseRegion.Click += ChooseRegion;
            statusTimer = new System.Windows.Forms.Timer { Interval = 350 };
            statusTimer.Tick += delegate { recognitionStatus.Text = overlay.Recognition.Status; };
            statusTimer.Start(); Reload();
        }

        void Combo(ComboBox control, Rectangle bounds, Control parent = null)
        { control.Bounds = bounds; control.DropDownStyle = ComboBoxStyle.DropDownList; (parent ?? this).Controls.Add(control); }

        void RefreshColor()
        {
            bool previous = loading; loading = true;
            Color selected = overlay.Config.ForegroundColor;
            color.SelectedIndex = overlay.Config.ColorHex.Length == 0 ? overlay.Config.ColorIndex : 6;
            hex.Text = Settings.ToHex(selected); hex.BackColor = Color.White;
            pickColor.BackColor = selected; pickColor.ForeColor = selected.GetBrightness() < 0.5 ? Color.White : Color.Black;
            loading = previous;
        }
        public void Reload()
        {
            loading = true; Settings s = overlay.Config;
            shape.SelectedIndex = s.Shape; length.Value = s.Length; gap.Value = s.Gap; thickness.Value = s.Thickness;
            outline.Checked = s.Outline; dot.Checked = s.CenterDot;
            automatic.Checked = s.AutoDetect; language.SelectedIndex = s.OcrLanguage == "en-US" ? 1 : 0;
            interval.SelectedIndex = s.ScanInterval == 500 ? 0 : s.ScanInterval == 2000 ? 2 : 1;
            RefreshColor(); screens = Screen.AllScreens; monitor.Items.Clear();
            foreach (Screen screen in screens) monitor.Items.Add(screen.DeviceName + (screen.Primary ? "（主屏）" : "") + " " + screen.Bounds.Width + "×" + screen.Bounds.Height);
            monitor.SelectedIndex = Array.FindIndex(screens, delegate(Screen screen) { return screen.DeviceName == overlay.SelectedScreen().DeviceName; });
            status.Text = overlay.HotkeyErrors.Count == 0 ? "Ctrl+Alt+F8 显示/隐藏    ·    F9 调整    ·    F10 换屏    ·    F11 退出" : "快捷键被占用：" + string.Join("、", overlay.HotkeyErrors.ToArray()) + "；请使用托盘菜单。";
            regionStatus.Text = s.CaptureRegionSet ? "比较两个枪名的文字高亮 · 仅 Apex 前台读取 · 图片不保存、不上传" : "请重新框选两个枪名（亮的和暗的都包含），只让高亮武器决定准星。";
            recognitionStatus.Text = overlay.Recognition.Status;
            loading = false; EnableFields(); preview.Invalidate();
        }
        void EnableFields()
        { length.Enabled = shape.SelectedIndex != 1; gap.Enabled = shape.SelectedIndex == 0 || shape.SelectedIndex == 4; dot.Enabled = shape.SelectedIndex != 1 && shape.SelectedIndex != 3; }
        void UpdateSettings(object sender, EventArgs args)
        {
            if (loading) return; Settings s = overlay.Config;
            s.Shape = shape.SelectedIndex; s.Length = length.Value; s.Gap = gap.Value; s.Thickness = thickness.Value;
            s.Outline = outline.Checked; s.CenterDot = dot.Checked;
            s.AutoDetect = automatic.Checked; s.OcrLanguage = language.SelectedIndex == 1 ? "en-US" : "zh-Hans-CN";
            s.ScanInterval = interval.SelectedIndex == 0 ? 500 : interval.SelectedIndex == 2 ? 2000 : 1000;
            if (monitor.SelectedIndex >= 0) s.Monitor = screens[monitor.SelectedIndex].DeviceName;
            EnableFields(); overlay.Changed(); preview.Invalidate();
        }

        async void ChooseRegion(object sender, EventArgs args)
        {
            if (choosing) return; choosing = true;
            bool priorAuto = overlay.Config.AutoDetect;
            overlay.Config.AutoDetect = false; overlay.Recognition.Reset();
            Hide(); overlay.Notify("框选两个枪名", "请在 20 秒内切回 Apex，把亮的和暗的两个枪名一起框入。Esc 取消。");
            try
            {
                DateTime end = DateTime.UtcNow.AddSeconds(20);
                while (!IsDisposed && DateTime.UtcNow < end)
                {
                    await Task.Delay(250);
                    IntPtr handle; Rectangle bounds;
                    if (!ApexWindow.Foreground(out handle, out bounds)) continue;
                    using (var picker = new RegionPicker(bounds))
                        if (picker.ShowDialog() == DialogResult.OK)
                        {
                            Rectangle r = picker.Selection; Settings s = overlay.Config;
                            s.CaptureX = (double)r.X / bounds.Width; s.CaptureY = (double)r.Y / bounds.Height;
                            s.CaptureWidth = (double)r.Width / bounds.Width; s.CaptureHeight = (double)r.Height / bounds.Height;
                            s.CaptureRegionSet = true; s.CaptureLayoutVersion = 2; priorAuto = true;
                        }
                    return;
                }
                if (!IsDisposed) MessageBox.Show("没有检测到前台 Apex。请先打开训练场，再点击框选并切回游戏。", "框选枪名", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                overlay.Config.AutoDetect = priorAuto;
                if (!IsDisposed && !overlay.IsDisposed) { overlay.Changed(); Reload(); Show(); Activate(); }
                choosing = false;
            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { statusTimer.Stop(); statusTimer.Dispose(); base.OnFormClosed(e); }
    }
}
