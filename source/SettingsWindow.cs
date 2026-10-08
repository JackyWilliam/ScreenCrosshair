using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenCrosshair
{
    public class SettingsWindow : ChromeForm
    {
        readonly Overlay overlay;
        readonly Preview preview;
        readonly ComboBox profile = new DarkComboBox(), presets = new DarkComboBox(), shape = new DarkComboBox(), color = new DarkComboBox(), monitor = new DarkComboBox(), language = new DarkComboBox(), interval = new DarkComboBox();
        readonly SliderRow length = new SliderRow("长度 / 半径", 2, 32), gap = new SliderRow("中心间隔", 0, 20), thickness = new SliderRow("线条粗细", .1, 8);
        readonly CheckBox outline = new DarkCheckBox(), dot = new DarkCheckBox(), automatic = new DarkCheckBox();
        readonly CheckBox[] parts = new CheckBox[9];
        readonly int[] partFlags = { CrosshairParts.Top, CrosshairParts.Bottom, CrosshairParts.Left, CrosshairParts.Right, CrosshairParts.Ring,
            CrosshairParts.TopLeft, CrosshairParts.TopRight, CrosshairParts.BottomLeft, CrosshairParts.BottomRight };
        readonly TextBox hex = Theme.Input(new Rectangle(481, 84, 104, 28));
        readonly Button pickColor, chooseRegion, deletePreset, applyPreset;
        readonly Label recognitionStatus = new Label(), regionStatus = new Label(), previewTitle = new Label();
        readonly System.Windows.Forms.Timer statusTimer;
        bool loading, choosing;
        Screen[] screens;
        WeaponKind EditingKind { get { return (WeaponKind)Math.Max(0, profile.SelectedIndex); } }
        CrosshairStyle EditingStyle { get { return overlay.Config.Profile(EditingKind); } }

        public SettingsWindow(Overlay owner)
        {
            overlay = owner; Text = "屏幕准星"; Icon = owner.Icon; ClientSize = new Size(900, 892);
            var canvas = new Panel { Size = new Size(876, 838), BackColor = Theme.Background }; Body.Controls.Add(canvas);
            var heading = Theme.Label("准星设置", new Rectangle(26, 15, 540, 38));
            heading.Font = new Font(Font.FontFamily, 20F, FontStyle.Bold); canvas.Controls.Add(heading);
            canvas.Controls.Add(Theme.Label("三套独立配置  /  即时生效  /  自动保存", new Rectangle(28, 58, 600, 26), true));
            canvas.Controls.Add(Theme.Label("正在调整", new Rectangle(28, 103, 78, 28), true));
            profile.Name = "EditingProfile"; Theme.Combo(profile, new Rectangle(108, 100, 208, 32), canvas);
            profile.Items.AddRange(new object[] { "普通 / 手动", "霰弹枪", "狙击枪" }); profile.SelectedIndex = 0;
            canvas.Controls.Add(Theme.Label("自定义预设", new Rectangle(342, 103, 91, 28), true));
            presets.Name = "CustomPresets"; Theme.Combo(presets, new Rectangle(438, 100, 196, 32), canvas);
            applyPreset = Theme.Button("套用", new Rectangle(644, 100, 60, 32)); canvas.Controls.Add(applyPreset);
            var save = Theme.Button("保存…", new Rectangle(714, 100, 76, 32)); save.Name = "SavePreset"; canvas.Controls.Add(save);
            deletePreset = Theme.Button("删除", new Rectangle(800, 100, 60, 32)); canvas.Controls.Add(deletePreset);
            presets.SelectedIndexChanged += delegate { applyPreset.Enabled = deletePreset.Enabled = presets.SelectedItem is CustomPreset; };
            applyPreset.Click += delegate { ApplySelectedPreset(); };
            save.Click += delegate { SavePresetDialog(); };
            deletePreset.Click += delegate { DeletePresetDialog(); };

            var previewCard = Card(canvas, new Rectangle(28, 154, 276, 430));
            previewCard.Controls.Add(Theme.Label("实时预览", new Rectangle(18, 12, 200, 27)));
            preview = new Preview(owner.Config) { Bounds = new Rectangle(18, 50, 240, 314) }; previewCard.Controls.Add(preview);
            previewTitle.Bounds = new Rectangle(18, 375, 240, 23); previewTitle.TextAlign = ContentAlignment.MiddleCenter; previewTitle.ForeColor = Theme.Muted; previewCard.Controls.Add(previewTitle);
            previewCard.Controls.Add(Theme.Label("按屏幕实际像素显示", new Rectangle(62, 399, 180, 22), true));

            var appearance = Card(canvas, new Rectangle(322, 154, 538, 430));
            appearance.Controls.Add(Theme.Label("准星外观", new Rectangle(20, 12, 200, 27)));
            Theme.Combo(shape, new Rectangle(282, 10, 236, 32), appearance);
            shape.Name = "CrosshairShape";
            shape.Items.AddRange(new object[] { "十字", "圆点", "圆环", "圆环＋中心点", "十字（无下方竖线）", "自定义组合", "三叉（奔驰）", "X 形" });
            appearance.Controls.Add(Theme.Label("颜色", new Rectangle(20, 57, 48, 28), true));
            Theme.Combo(color, new Rectangle(74, 54, 131, 32), appearance); color.Items.AddRange(Settings.ColorNames); color.Items.Add("自定义");
            hex.Name = "ColorHex"; hex.SetBounds(217, 55, 119, 28); hex.MaxLength = 7; hex.AccessibleName = "自定义颜色 HEX"; appearance.Controls.Add(hex);
            pickColor = Theme.Button("调色…", new Rectangle(348, 54, 170, 32)); appearance.Controls.Add(pickColor);
            length.Location = new Point(20, 99); gap.Location = new Point(20, 165); thickness.Location = new Point(20, 231);
            appearance.Controls.Add(length); appearance.Controls.Add(gap); appearance.Controls.Add(thickness);
            outline.Text = "黑色描边"; outline.SetBounds(20, 301, 145, 27); appearance.Controls.Add(outline);
            dot.Text = "中心点"; dot.SetBounds(178, 301, 125, 27); appearance.Controls.Add(dot);

            appearance.Controls.Add(Theme.Label("独立部件 · 选择“自定义组合”后可调整", new Rectangle(20, 335, 494, 23), true));
            string[] partNames = { "上竖线", "下竖线", "左横线", "右横线", "圆环", "左上斜线", "右上斜线", "左下斜线", "右下斜线" };
            for (int i = 0; i < parts.Length; i++)
            {
                var check = new DarkCheckBox { Name = "Part" + partFlags[i], Text = partNames[i], Bounds = new Rectangle(20 + (i < 5 ? i * 100 : (i - 5) * 125), i < 5 ? 361 : 393, i < 5 ? 98 : 120, 26) };
                parts[i] = check; appearance.Controls.Add(check); check.CheckedChanged += UpdateSettings;
            }

            var autoPanel = Card(canvas, new Rectangle(28, 602, 832, 159));
            automatic.Text = "Apex 自动识别武器"; automatic.SetBounds(18, 12, 258, 28); autoPanel.Controls.Add(automatic);
            var history = Theme.Button("状态记录", new Rectangle(558, 10, 104, 32)); history.Click += delegate { ShowRecognitionHistory(); }; autoPanel.Controls.Add(history);
            chooseRegion = Theme.Button("框选两个枪名…", new Rectangle(674, 10, 140, 32)); autoPanel.Controls.Add(chooseRegion);
            autoPanel.Controls.Add(Theme.Label("游戏语言", new Rectangle(18, 54, 78, 28), true));
            Theme.Combo(language, new Rectangle(99, 51, 160, 32), autoPanel); language.Items.AddRange(new object[] { "简体中文", "English" });
            autoPanel.Controls.Add(Theme.Label("检测间隔", new Rectangle(282, 54, 78, 28), true));
            Theme.Combo(interval, new Rectangle(365, 51, 208, 32), autoPanel); interval.Items.AddRange(new object[] { "0.5 秒 · 较快", "1 秒 · 默认", "2 秒 · 省资源" });
            autoPanel.Controls.Add(Theme.Label("切枪后自动套用对应配置", new Rectangle(598, 54, 216, 28), true));
            recognitionStatus.SetBounds(18, 90, 796, 28); recognitionStatus.ForeColor = Theme.Accent; autoPanel.Controls.Add(recognitionStatus);
            regionStatus.SetBounds(18, 122, 796, 24); regionStatus.ForeColor = Theme.Muted; regionStatus.Font = new Font(Font.FontFamily, 8.5F); autoPanel.Controls.Add(regionStatus);

            canvas.Controls.Add(Theme.Label("显示器", new Rectangle(28, 781, 66, 28), true));
            Theme.Combo(monitor, new Rectangle(100, 778, 446, 32), canvas);
            var reset = Theme.Button("重置当前", new Rectangle(606, 777, 120, 34)); reset.Click += delegate { overlay.Config.ResetProfile(EditingKind); Reload(); overlay.Changed(false); }; canvas.Controls.Add(reset);
            var done = Theme.Button("完成", new Rectangle(738, 777, 122, 34), true); done.Click += delegate { Close(); }; canvas.Controls.Add(done);
            string shortcuts = owner.HotkeyErrors.Count == 0 ? "Ctrl + Alt  ·  F8 显示 / 隐藏    F9 设置    F10 换屏    F11 退出"
                : "快捷键被占用：" + string.Join("、", owner.HotkeyErrors.ToArray()) + "；请使用托盘菜单。";
            canvas.Controls.Add(Theme.Label(shortcuts, new Rectangle(28, 814, 825, 23), true));
            // The editor selection is independent of whichever profile recognition is displaying in the game.
            profile.SelectedIndexChanged += delegate { if (!loading) Reload(); };
            shape.SelectedIndexChanged += delegate(object sender, EventArgs args)
            {
                if (loading) return;
                if (shape.SelectedIndex == 5 && EditingStyle.Shape != 5)
                {
                    // Start custom editing from the visible template, including the three-spoke angles and forced dot.
                    CrosshairStyle current = EditingStyle; current.CustomParts = current.PartsForShape(current.Shape);
                    current.DiagonalAngle = current.AngleForShape(current.Shape);
                    loading = true; dot.Checked = current.CenterDot || current.Shape == 1 || current.Shape == 3; RefreshParts(current.CustomParts); loading = false;
                }
                UpdateSettings(sender, args);
            };
            monitor.SelectedIndexChanged += UpdateSettings;
            language.SelectedIndexChanged += UpdateSettings; interval.SelectedIndexChanged += UpdateSettings; automatic.CheckedChanged += UpdateSettings;
            length.ValueChanged += UpdateSettings; gap.ValueChanged += UpdateSettings; thickness.ValueChanged += UpdateSettings;
            outline.CheckedChanged += UpdateSettings; dot.CheckedChanged += UpdateSettings;
            color.SelectedIndexChanged += delegate
            {
                if (loading || color.SelectedIndex < 0) return;
                CrosshairStyle current = EditingStyle;
                if (color.SelectedIndex < Settings.Colors.Length) { current.ColorIndex = color.SelectedIndex; current.ColorHex = ""; }
                else current.ColorHex = Settings.ToHex(current.ForegroundColor);
                RefreshColor(); AppearanceChanged();
            };
            hex.TextChanged += delegate
            {
                if (loading) return;
                Color custom; bool valid = Settings.TryParseColor(hex.Text, out custom); hex.ForeColor = valid ? Theme.Text : Theme.Error;
                if (!valid) return;
                EditingStyle.ColorHex = Settings.ToHex(custom); loading = true; color.SelectedIndex = 6; loading = false;
                Swatch(custom); AppearanceChanged();
            };
            hex.Leave += delegate { RefreshColor(); };
            pickColor.Click += delegate
            {
                using (var dialog = new ColorDialog { Color = EditingStyle.ForegroundColor, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { EditingStyle.ColorHex = Settings.ToHex(dialog.Color); RefreshColor(); AppearanceChanged(); }
            };
            chooseRegion.Click += ChooseRegion;
            statusTimer = new System.Windows.Forms.Timer { Interval = 350 };
            statusTimer.Tick += delegate { recognitionStatus.Text = overlay.Recognition.Status; };
            statusTimer.Start(); Reload(); ReloadPresets(null);
        }
        static Panel Card(Control parent, Rectangle bounds)
        {
            var card = new Panel { Bounds = bounds, BackColor = Theme.Card }; parent.Controls.Add(card); return card;
        }
        void AppearanceChanged() { overlay.Changed(false); preview.Invalidate(); }
        void Swatch(Color selected)
        { pickColor.BackColor = selected; pickColor.ForeColor = selected.GetBrightness() < .5 ? Color.White : Theme.Background; pickColor.FlatAppearance.MouseOverBackColor = selected; }
        void RefreshColor()
        {
            bool previous = loading; loading = true; CrosshairStyle current = EditingStyle;
            color.SelectedIndex = current.ColorHex.Length == 0 ? current.ColorIndex : 6;
            hex.Text = Settings.ToHex(current.ForegroundColor); hex.ForeColor = Theme.Text; Swatch(current.ForegroundColor); loading = previous;
        }
        void ReloadPresets(CustomPreset selected)
        {
            presets.Items.Clear(); presets.Items.Add("选择已保存的预设");
            foreach (CustomPreset preset in overlay.Config.CustomPresets) presets.Items.Add(preset);
            presets.SelectedItem = selected == null ? presets.Items[0] : selected;
        }
        public void ApplySelectedPreset()
        {
            var preset = presets.SelectedItem as CustomPreset; if (preset == null) return;
            EditingStyle.Apply(preset.Style); Reload(); AppearanceChanged();
        }
        void SavePresetDialog()
        {
            using (var dialog = new PresetDialog(overlay.Config, presets.SelectedItem as CustomPreset) { Icon = Icon })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    CustomPreset saved = overlay.Config.SavePreset(dialog.PresetName, EditingStyle);
                    ReloadPresets(saved); overlay.Changed(false);
                }
        }
        void DeletePresetDialog()
        {
            var preset = presets.SelectedItem as CustomPreset; if (preset == null) return;
            using (var dialog = new ChromeForm { Text = "删除自定义预设", ClientSize = new Size(440, 212), StartPosition = FormStartPosition.CenterParent, Icon = Icon })
            {
                dialog.Body.Controls.Add(Theme.Label("删除“" + preset.Name + "”？", new Rectangle(22, 15, 393, 52)));
                dialog.Body.Controls.Add(Theme.Label("已套用的准星外观会保留。", new Rectangle(22, 67, 393, 25), true));
                var cancel = Theme.Button("取消", new Rectangle(216, 111, 92, 32)); cancel.DialogResult = DialogResult.Cancel;
                var remove = Theme.Button("删除", new Rectangle(320, 111, 96, 32)); remove.ForeColor = Theme.Error; remove.DialogResult = DialogResult.OK;
                dialog.Body.Controls.Add(cancel); dialog.Body.Controls.Add(remove); dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) == DialogResult.OK) { overlay.Config.CustomPresets.Remove(preset); ReloadPresets(null); overlay.Changed(false); }
            }
        }
        public void Reload()
        {
            loading = true; Settings s = overlay.Config; CrosshairStyle current = EditingStyle;
            preview.Config = current; previewTitle.Text = profile.Text + " · 配置预览";
            shape.SelectedIndex = current.Shape; length.Value = current.Length; gap.Value = current.Gap; thickness.Value = current.Thickness;
            outline.Checked = current.Outline; dot.Checked = current.CenterDot;
            automatic.Checked = s.AutoDetect; language.SelectedIndex = s.OcrLanguage == "en-US" ? 1 : 0;
            interval.SelectedIndex = s.ScanInterval == 500 ? 0 : s.ScanInterval == 2000 ? 2 : 1;
            RefreshColor(); screens = Screen.AllScreens; monitor.Items.Clear();
            for (int i = 0; i < screens.Length; i++) monitor.Items.Add("显示器 " + (i + 1) + (screens[i].Primary ? " · 主屏" : "") + "  /  " + screens[i].Bounds.Width + " × " + screens[i].Bounds.Height);
            monitor.SelectedIndex = Array.FindIndex(screens, delegate(Screen screen) { return screen.DeviceName == overlay.SelectedScreen().DeviceName; });
            regionStatus.Text = s.CaptureRegionSet ? "已框选双枪名区域  ·  仅 Apex 前台识别  ·  图片不保存、不上传" : "请框选两个完整枪名，亮的和暗的都要包含。关闭自动识别时使用普通配置。";
            recognitionStatus.Text = overlay.Recognition.Status;
            loading = false; EnableFields(); preview.Invalidate();
        }
        void EnableFields()
        {
            length.Enabled = shape.SelectedIndex != 1; gap.Enabled = shape.SelectedIndex == 0 || shape.SelectedIndex >= 4;
            dot.Enabled = shape.SelectedIndex != 1 && shape.SelectedIndex != 3;
            bool previous = loading; loading = true;
            RefreshParts(EditingStyle.PartsForShape(EditingStyle.Shape));
            foreach (CheckBox part in parts) part.Enabled = shape.SelectedIndex == 5;
            loading = previous;
        }
        void RefreshParts(int value)
        { for (int i = 0; i < parts.Length; i++) parts[i].Checked = (value & partFlags[i]) != 0; }
        void UpdateSettings(object sender, EventArgs args)
        {
            if (loading) return; Settings s = overlay.Config; CrosshairStyle current = EditingStyle;
            current.Shape = shape.SelectedIndex; current.Length = length.Value; current.Gap = gap.Value; current.Thickness = thickness.Value;
            if (current.Shape == 5) { current.CustomParts = 0; for (int i = 0; i < parts.Length; i++) if (parts[i].Checked) current.CustomParts |= partFlags[i]; }
            current.Outline = outline.Checked; current.CenterDot = dot.Checked;
            s.AutoDetect = automatic.Checked; s.OcrLanguage = language.SelectedIndex == 1 ? "en-US" : "zh-Hans-CN";
            s.ScanInterval = interval.SelectedIndex == 0 ? 500 : interval.SelectedIndex == 2 ? 2000 : 1000;
            if (monitor.SelectedIndex >= 0) s.Monitor = screens[monitor.SelectedIndex].DeviceName;
            EnableFields(); overlay.Changed(sender == automatic || sender == language || sender == interval); preview.Invalidate();
        }
        void ShowRecognitionHistory()
        {
            // Opening settings moves focus away from Apex; retain the preceding reading for diagnosis.
            using (var dialog = new ChromeForm { Text = "识别状态记录", ClientSize = new Size(780, 490), StartPosition = FormStartPosition.CenterParent, Icon = Icon })
            {
                dialog.Body.Padding = new Padding(18);
                dialog.Body.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Text,
                    Text = "仅保留本次运行最近 30 次状态变化，不写入文件。\r\n切到设置 / 聊天会暂停，切回 Apex 自动继续。\r\n\r\n" + overlay.Recognition.HistoryText + "\r\n\r\n最近一次检测：\r\n" + (overlay.Recognition.LastResult ?? "尚未检测") + "\r\nOCR 读到的文字：\r\n" + (overlay.Recognition.LastRecognizedText ?? "尚未检测") });
                dialog.ShowDialog(this);
            }
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

    public sealed class PresetDialog : ChromeForm
    {
        readonly TextBox name;
        public string PresetName { get { return name.Text.Trim(); } }
        public PresetDialog(Settings settings, CustomPreset selected)
        {
            Text = "保存自定义预设"; ClientSize = new Size(440, 250); StartPosition = FormStartPosition.CenterParent;
            Body.Controls.Add(Theme.Label("给当前外观起个名字", new Rectangle(22, 12, 390, 26)));
            name = Theme.Input(new Rectangle(22, 49, 392, 30)); name.Name = "PresetName"; name.MaxLength = 40; name.Text = selected == null ? "" : selected.Name; Body.Controls.Add(name);
            var hint = Theme.Label("保存后可套用到普通、霰弹枪或狙击枪。", new Rectangle(22, 85, 392, 45), true); Body.Controls.Add(hint);
            var cancel = Theme.Button("取消", new Rectangle(216, 147, 92, 32)); cancel.DialogResult = DialogResult.Cancel; Body.Controls.Add(cancel);
            var save = Theme.Button("保存", new Rectangle(320, 147, 94, 32), true); Body.Controls.Add(save);
            EventHandler validate = delegate
            {
                bool exists = settings.CustomPresets.Exists(p => string.Equals(p.Name, PresetName, StringComparison.OrdinalIgnoreCase));
                save.Text = exists ? "更新" : "保存"; save.Enabled = PresetName.Length > 0 && !Array.Exists(PresetName.ToCharArray(), char.IsControl);
                hint.Text = exists ? "同名预设已存在，点击“更新”替换它的外观。" : "保存后可套用到普通、霰弹枪或狙击枪。";
            };
            name.TextChanged += validate; validate(null, EventArgs.Empty);
            save.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = save; CancelButton = cancel; Shown += delegate { name.Focus(); name.SelectAll(); };
        }
    }
}
