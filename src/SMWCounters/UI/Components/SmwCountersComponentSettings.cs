using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Model;
using LiveSplit.Model.Input;
using LiveSplit.Options;

namespace LiveSplit.UI.Components;

public enum HAlignment { Left, Center, Right }

public class SmwCountersComponentSettings : UserControl
{
    public CompositeHook Hook { get; }

    private readonly HashSet<string> enabled = new() { "deaths", "exits" };

    // Per-counter "Discard on death" values. Ids absent from the map use
    // DefaultBankOnSave — an explicit map (unlike the pre-v0.6 BankDisabled
    // set) can tell "user turned it on" apart from "never seen", which is what
    // lets moons default OFF while everything else defaults ON.
    private readonly Dictionary<string, bool> bankOnSave = new();

    // Toggle-carrying counter ids, captured by BuildUi. Serialization writes
    // one explicit entry per id so future default changes can't reinterpret
    // existing layouts.
    private readonly List<string> toggleIds = new();

    public KeyOrButton ResetKey { get; set; }
    public KeyOrButton IncrementKey { get; set; }
    public KeyOrButton DecrementKey { get; set; }
    // Id of the counter the adjust hotkeys nudge.
    public string HotkeyTarget { get; set; } = "oneups";
    public int RowHeight { get; set; } = 45;
    public HAlignment Alignment { get; set; } = HAlignment.Center;
    // Digits every value cell reserves room for before it starts widening.
    public int ReserveDigits
    {
        get => reserveDigits;
        set => reserveDigits = ValueWidth.ClampDigits(value);   // the one clamp
    }
    private int reserveDigits = ValueWidth.DefaultDigits;
    // On (default): counters only tally during a live run. Off: they keep
    // counting with the timer stopped, for challenge runs that never start it.
    public bool OnlyCountWhileTimerRunning { get; set; } = true;

    // Content width. LiveSplit hosts this control in a fixed-height dialog
    // whose vertical scrollbar eats ~17px of client width, so anything wider
    // than the host minus that bar sprouts a horizontal scrollbar too.
    private const int DialogWidth = 460;
    // Initial left edge of the "Discard on death" checkbox column; the real
    // position follows the Reset button's width (PlaceAfterReset).
    private const int BankToggleX = 252;
    private Label lblBankHeader;
    // 96-DPI-to-current scale applied by ScaleLayoutToDpi (1 when nothing
    // was scaled), used for gaps computed after that pass.
    private float dpiFactor = 1f;
    public bool ResetOnSplitsReset { get; set; } = true;
    public bool DebugLog { get; set; } = false;
    public bool ShowStatusDot { get; set; } = true;

    // Shipped hotkey defaults (decided 2026-10-25): numpad keys that neither
    // RetroArch (F1–F9, F11, F12, numpad + and −, a dozen letters) nor
    // LiveSplit (numpad 1–6, 8, 9) bind by default, and that nobody types in
    // a chat window while LiveSplit's global hook is listening. The previous
    // Reset default, F2, is RetroArch's save-state key, so a save state
    // zeroed every counter. Only layouts that never saved a key see these.
    private static KeyOrButton DefaultResetKey => new KeyOrButton(Keys.Decimal);
    private static KeyOrButton DefaultIncrementKey => new KeyOrButton(Keys.Multiply);
    private static KeyOrButton DefaultDecrementKey => new KeyOrButton(Keys.Divide);

    public SmwCountersComponentSettings(bool allowGamepads)
    {
        Hook = new CompositeHook(allowGamepads);
        ResetKey = DefaultResetKey;
        IncrementKey = DefaultIncrementKey;
        DecrementKey = DefaultDecrementKey;
        Size = new Size(420, 240);
    }

    private readonly List<CounterRow> rows = new();
    private TextBox txtReset;
    private TextBox txtIncrement;
    private TextBox txtDecrement;
    private ComboBox cboHotkeyTarget;
    private TrackBar trkHeight;
    private NumericUpDown numReserveDigits;
    private RadioButton rdoLeft;
    private RadioButton rdoCenter;
    private RadioButton rdoRight;
    private CheckBox chkResetOnSplitsReset;
    private CheckBox chkOnlyCountWhileTimerRunning;
    private CheckBox chkShowStatusDot;
    private CheckBox chkDebugLog;
    private Label lblManyCounters;
    private Label lblStatus;
    private readonly ToolTip bankToolTip = new();

    private sealed class CounterRow
    {
        public string Id;
        public CheckBox Enable;
        public TextBox ValueBox;
        public Button ResetValue;
        public CheckBox BankToggle;
        public Action OnResetValue;
        public Func<int> GetValue;
        public Action<int> SetValue;
        public Control CounterSpecific;
        public Action RefreshExtras;
    }

    // Component calls this once at construction with the list of known counters.
    public void BuildUi(IReadOnlyList<(string Id, string DefaultLabel, bool HasBankToggle, Control Extras, Action ResetValue, Func<int> GetValue, Action<int> SetValue, Action RefreshExtras)> counters)
    {
        Controls.Clear();
        rows.Clear();
        toggleIds.Clear();

        int y = 10;

        // Column header for the bare per-row checkboxes below; without it the
        // column reads as empty space (seen 2026-10-25). PlaceAfterReset
        // lines it up with the column once the rows exist.
        lblBankHeader = new Label
        {
            Text = "Discard on death",
            Location = new Point(BankToggleX - 2, y),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        Controls.Add(lblBankHeader);
        y += 18;

        foreach ((string id, string defaultLabel, bool hasBankToggle, Control extras, Action resetValue, Func<int> getValue, Action<int> setValue, Action refreshExtras) in counters)
        {
            var row = new CounterRow
            {
                Id = id,
                OnResetValue = resetValue,
                GetValue = getValue,
                SetValue = setValue,
                RefreshExtras = refreshExtras,
            };

            row.Enable = new CheckBox
            {
                Text = defaultLabel,
                Location = new Point(10, y + 2),
                AutoSize = true,
                Checked = IsEnabled(id),
            };
            row.Enable.CheckedChanged += (_, __) =>
            {
                SetEnabled(id, row.Enable.Checked);
                SyncRowEnabled(row);
                SyncManyCountersHint();
            };
            Controls.Add(row.Enable);

            row.ValueBox = new TextBox
            {
                Text = getValue().ToString(),
                Location = new Point(140, y),
                Width = 36,
                TextAlign = HorizontalAlignment.Right,
            };
            row.ValueBox.Leave += (_, __) => CommitValue(row);
            row.ValueBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitValue(row); }
            };
            Controls.Add(row.ValueBox);

            row.ResetValue = new Button
            {
                Text = "Reset",
                Location = new Point(195, y - 2),
                AutoSize = true,
            };
            row.ResetValue.Click += (_, __) => { row.OnResetValue?.Invoke(); row.ValueBox.Text = row.GetValue().ToString(); };
            Controls.Add(row.ResetValue);

            if (hasBankToggle)
            {
                toggleIds.Add(id);
                row.BankToggle = new CheckBox
                {
                    Location = new Point(BankToggleX, y + 2),
                    AutoSize = true,
                    Checked = IsBankOnSave(id),
                };
                row.BankToggle.CheckedChanged += (_, __) => SetBankOnSave(id, row.BankToggle.Checked);
                bankToolTip.SetToolTip(row.BankToggle,
                    "Discard on death: unbanked " + defaultLabel + " (shown gold) are discarded if you die " +
                    "before a checkpoint or exit. Both histories are always tracked; this only picks which one is shown.");
                Controls.Add(row.BankToggle);
                // Added after the Reset button, so it would sit behind it in
                // z-order if the two ever overlapped.
                row.BankToggle.BringToFront();
            }

            if (extras != null)
            {
                extras.Location = new Point(275, y);
                Controls.Add(extras);
                row.CounterSpecific = extras;
            }

            // The Reset button is auto-sized and grow-only, so its width is
            // never less than WinForms' 75 px default and grows further with
            // the host's font (LiveSplit hands this control Segoe UI 9 when
            // it parents it). Any fixed x for the controls to its right ends
            // up underneath it — the bank checkbox column was covered that
            // way until 2026-10-25 — so they follow its actual right edge and
            // move again whenever it resizes.
            PlaceAfterReset(row);
            row.ResetValue.SizeChanged += (_, __) => PlaceAfterReset(row);

            SyncRowEnabled(row);
            rows.Add(row);
            y += 30;
        }

        Controls.Add(new Label
        {
            Text = "Reset hotkey (global):",
            Location = new Point(10, y + 3),
            AutoSize = true,
        });
        txtReset = new TextBox
        {
            ReadOnly = true,
            Text = FormatKey(ResetKey),
            Location = new Point(130, y),
            Width = 210,
        };
        txtReset.Enter += (_, __) => CaptureKey(txtReset, k => ResetKey = k);
        Controls.Add(txtReset);
        y += 30;

        Controls.Add(new Label
        {
            Text = "+1 hotkey (global):",
            Location = new Point(10, y + 3),
            AutoSize = true,
        });
        txtIncrement = new TextBox
        {
            ReadOnly = true,
            Text = FormatKey(IncrementKey),
            Location = new Point(130, y),
            Width = 210,
        };
        txtIncrement.Enter += (_, __) => CaptureKey(txtIncrement, k => IncrementKey = k);
        Controls.Add(txtIncrement);
        y += 30;

        Controls.Add(new Label
        {
            Text = "−1 hotkey (global):",
            Location = new Point(10, y + 3),
            AutoSize = true,
        });
        txtDecrement = new TextBox
        {
            ReadOnly = true,
            Text = FormatKey(DecrementKey),
            Location = new Point(130, y),
            Width = 210,
        };
        txtDecrement.Enter += (_, __) => CaptureKey(txtDecrement, k => DecrementKey = k);
        Controls.Add(txtDecrement);
        y += 30;

        Controls.Add(new Label
        {
            Text = "Hotkeys adjust:",
            Location = new Point(10, y + 3),
            AutoSize = true,
        });
        // DisplayMember/ValueMember bind to properties; tuple elements are
        // fields, so the items are KeyValuePairs (Key = id, Value = label).
        var targets = new List<KeyValuePair<string, string>>();
        foreach (var c in counters) { targets.Add(new KeyValuePair<string, string>(c.Id, c.DefaultLabel)); }
        cboHotkeyTarget = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(130, y),
            Width = 210,
        };
        Controls.Add(cboHotkeyTarget);
        cboHotkeyTarget.DataSource = targets;
        cboHotkeyTarget.ValueMember = "Key";
        cboHotkeyTarget.DisplayMember = "Value";
        cboHotkeyTarget.SelectedValue = HotkeyTarget;
        cboHotkeyTarget.SelectedValueChanged += (_, __) =>
        {
            if (cboHotkeyTarget.SelectedValue is string id) { HotkeyTarget = id; }
        };
        y += 30;

        Controls.Add(new Label
        {
            Text = "Row height:",
            Location = new Point(10, y + 6),
            AutoSize = true,
        });
        trkHeight = new TrackBar
        {
            Minimum = 20,
            Maximum = 100,
            Value = RowHeight,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Width = 160,
            Height = 30,
            Location = new Point(100, y),
            AutoSize = false,
        };
        var lblHeightVal = new Label
        {
            Text = RowHeight + "px",
            Location = new Point(265, y + 6),
            AutoSize = true,
        };
        trkHeight.ValueChanged += (_, __) =>
        {
            RowHeight = trkHeight.Value;
            lblHeightVal.Text = RowHeight + "px";
        };
        Controls.Add(trkHeight);
        Controls.Add(lblHeightVal);
        y += 48;

        Controls.Add(new Label
        {
            Text = "Alignment:",
            Location = new Point(10, y + 4),
            AutoSize = true,
        });
        rdoLeft = new RadioButton { Text = "Left", AutoSize = true, Location = new Point(100, y + 2), Checked = Alignment == HAlignment.Left };
        rdoCenter = new RadioButton { Text = "Center", AutoSize = true, Location = new Point(150, y + 2), Checked = Alignment == HAlignment.Center };
        rdoRight = new RadioButton { Text = "Right", AutoSize = true, Location = new Point(215, y + 2), Checked = Alignment == HAlignment.Right };
        rdoLeft.CheckedChanged += (_, __) => { if (rdoLeft.Checked) { Alignment = HAlignment.Left; } };
        rdoCenter.CheckedChanged += (_, __) => { if (rdoCenter.Checked) { Alignment = HAlignment.Center; } };
        rdoRight.CheckedChanged += (_, __) => { if (rdoRight.Checked) { Alignment = HAlignment.Right; } };
        Controls.Add(rdoLeft);
        Controls.Add(rdoCenter);
        Controls.Add(rdoRight);

        // Same row as the alignment radios: both are layout-fit knobs.
        Controls.Add(new Label
        {
            Text = "Reserve digits:",
            Location = new Point(290, y + 4),
            AutoSize = true,
        });
        numReserveDigits = new NumericUpDown
        {
            Minimum = ValueWidth.MinDigits,
            Maximum = ValueWidth.MaxDigits,
            Value = ReserveDigits,
            Width = 42,
            Location = new Point(385, y + 1),
        };
        numReserveDigits.ValueChanged += (_, __) => ReserveDigits = (int)numReserveDigits.Value;
        Controls.Add(numReserveDigits);
        y += 30;

        chkResetOnSplitsReset = new CheckBox
        {
            Text = "Reset counters when splits reset",
            Location = new Point(10, y),
            AutoSize = true,
            Checked = ResetOnSplitsReset,
        };
        chkResetOnSplitsReset.CheckedChanged += (_, __) => ResetOnSplitsReset = chkResetOnSplitsReset.Checked;
        Controls.Add(chkResetOnSplitsReset);
        y += 28;

        chkOnlyCountWhileTimerRunning = new CheckBox
        {
            Text = "Only count when timer running",
            Location = new Point(10, y),
            AutoSize = true,
            Checked = OnlyCountWhileTimerRunning,
        };
        chkOnlyCountWhileTimerRunning.CheckedChanged += (_, __) => OnlyCountWhileTimerRunning = chkOnlyCountWhileTimerRunning.Checked;
        Controls.Add(chkOnlyCountWhileTimerRunning);
        y += 28;

        chkShowStatusDot = new CheckBox
        {
            Text = "Show connection status pixel",
            Location = new Point(10, y),
            AutoSize = true,
            Checked = ShowStatusDot,
        };
        chkShowStatusDot.CheckedChanged += (_, __) => ShowStatusDot = chkShowStatusDot.Checked;
        Controls.Add(chkShowStatusDot);
        y += 28;

        chkDebugLog = new CheckBox
        {
            Text = "Write debug events to counters-debug.log",
            Location = new Point(10, y),
            AutoSize = true,
            Checked = DebugLog,
        };
        chkDebugLog.CheckedChanged += (_, __) => DebugLog = chkDebugLog.Checked;
        Controls.Add(chkDebugLog);
        y += 28;

        lblManyCounters = new Label
        {
            Text = "Tip: if counters cut off, add a second SMW Counters component to the layout and split the counters between them.",
            Location = new Point(10, y),
            MaximumSize = new Size(DialogWidth - 20, 0),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        Controls.Add(lblManyCounters);
        SyncManyCountersHint();
        y += 34;

        // Wraps within the dialog (two lines reserved) so a long connection
        // description can't push the content wider than the host.
        lblStatus = new Label
        {
            Text = "(not polled yet)",
            Location = new Point(10, y),
            MaximumSize = new Size(DialogWidth - 20, 0),
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
        };
        Controls.Add(lblStatus);
        y += 40;

        var author = new LinkLabel
        {
            Text = "created by twitch.tv/mangort",
            AutoSize = true,
            LinkColor = SystemColors.GrayText,
            ActiveLinkColor = SystemColors.GrayText,
            VisitedLinkColor = SystemColors.GrayText,
            ForeColor = SystemColors.GrayText,
        };
        author.LinkArea = new LinkArea(author.Text.IndexOf("twitch.tv"), "twitch.tv/mangort".Length);
        author.LinkClicked += (_, __) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://twitch.tv/mangort") { UseShellExecute = true }); }
            catch { }
        };
        Controls.Add(author);
        author.Location = new Point(DialogWidth - author.PreferredWidth - 10, y);
        y += 20;

        Size = new Size(DialogWidth, y + 10);

        ScaleLayoutToDpi();
        foreach (CounterRow row in rows) { PlaceAfterReset(row); }

        RegisterHotKeys();
    }

    // Lays out the row's controls that sit right of the Reset button from the
    // button's real right edge: the bank checkbox, then the counter-specific
    // extras (Kills' radios), each separated by a small gap in current
    // pixels. The header label follows the checkbox column.
    private void PlaceAfterReset(CounterRow row)
    {
        int gap = Scaled(8, dpiFactor);
        int x = row.ResetValue.Right + gap;
        if (row.BankToggle != null)
        {
            row.BankToggle.Left = x;
            x = row.BankToggle.Right + gap;
            if (lblBankHeader != null) { lblBankHeader.Left = row.BankToggle.Left - Scaled(2, dpiFactor); }
        }
        if (row.CounterSpecific != null) { row.CounterSpecific.Left = x; }
    }

    // Every position in BuildUi is laid out in 96-DPI pixels, but control
    // fonts follow the process DPI, so on a 150% display the auto-sized Reset
    // buttons grew over the bank checkboxes and labels ran into their boxes
    // (seen 2026-10-25). Scale the finished layout once by the DPI the
    // process actually sees: 96 in a DPI-unaware host (Windows bitmap-scales
    // the whole window, nothing to do) and the real value in an aware one.
    // Control.Scale is not used because it also multiplies the sizes of
    // text-sized controls whose fonts already grew, which doubled the Reset
    // button's width. Positions scale for everything; sizes only for
    // controls whose size is not derived from their text.
    private void ScaleLayoutToDpi()
    {
        float factor;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { factor = g.DpiX / 96f; }
        if (Math.Abs(factor - 1f) < 0.01f) { return; }
        dpiFactor = factor;
        ScaleChildren(this, factor);
        Size = new Size(Scaled(Width, factor), Scaled(Height, factor));
    }

    // Children only: a container's own size is scaled exactly once, by its
    // parent's pass (or by ScaleLayoutToDpi for the dialog itself).
    private static void ScaleChildren(Control parent, float factor)
    {
        foreach (Control c in parent.Controls)
        {
            c.Location = new Point(Scaled(c.Left, factor), Scaled(c.Top, factor));
            bool textSized = c.AutoSize && (c is ButtonBase || c is Label);
            if (!textSized) { c.Size = new Size(Scaled(c.Width, factor), Scaled(c.Height, factor)); }
            if (c.MaximumSize.Width > 0) { c.MaximumSize = new Size(Scaled(c.MaximumSize.Width, factor), c.MaximumSize.Height); }
            ScaleChildren(c, factor);   // the Kills radio panel's children
        }
    }

    private static int Scaled(int value, float factor) => (int)Math.Round(value * factor);

    private void CommitValue(CounterRow row)
    {
        if (int.TryParse(row.ValueBox.Text, out int v)) { row.SetValue(v); }
        row.ValueBox.Text = row.GetValue().ToString();
    }

    private static void SyncRowEnabled(CounterRow row)
    {
        bool on = row.Enable.Checked;
        row.ValueBox.Enabled = on;
        row.ResetValue.Enabled = on;
        if (row.BankToggle != null) { row.BankToggle.Enabled = on; }
        if (row.CounterSpecific != null) { row.CounterSpecific.Enabled = on; }
    }

    // The overlay renders counters in one fixed-height row; many enabled
    // counters can outgrow the layout width (no wrap by design — mid-run
    // digit growth would change component height). Nudge toward a second
    // component instead.
    private void SyncManyCountersHint()
    {
        if (lblManyCounters != null) { lblManyCounters.Visible = enabled.Count > 3; }
    }

    // Re-syncs visible row widgets from the data model after SetSettings is called.
    public void RefreshFromModel()
    {
        foreach (CounterRow row in rows)
        {
            row.Enable.Checked = IsEnabled(row.Id);
            row.ValueBox.Text = row.GetValue().ToString();
            if (row.BankToggle != null) { row.BankToggle.Checked = IsBankOnSave(row.Id); }
            SyncRowEnabled(row);
            row.RefreshExtras?.Invoke();
        }
        SyncManyCountersHint();
        if (txtReset != null) { txtReset.Text = FormatKey(ResetKey); }
        if (txtIncrement != null) { txtIncrement.Text = FormatKey(IncrementKey); }
        if (txtDecrement != null) { txtDecrement.Text = FormatKey(DecrementKey); }
        if (cboHotkeyTarget != null && cboHotkeyTarget.DataSource is List<KeyValuePair<string, string>> targets)
        {
            // An unknown saved target (e.g. a counter since removed) falls back
            // to the default, so the combo and the hotkeys always agree.
            if (!targets.Exists(t => t.Key == HotkeyTarget)) { HotkeyTarget = "oneups"; }
            cboHotkeyTarget.SelectedValue = HotkeyTarget;
        }
        if (trkHeight != null) { trkHeight.Value = Math.Max(trkHeight.Minimum, Math.Min(trkHeight.Maximum, RowHeight)); }
        if (numReserveDigits != null) { numReserveDigits.Value = ReserveDigits; }
        if (rdoLeft != null)
        {
            rdoLeft.Checked = Alignment == HAlignment.Left;
            rdoCenter.Checked = Alignment == HAlignment.Center;
            rdoRight.Checked = Alignment == HAlignment.Right;
        }
        if (chkResetOnSplitsReset != null) { chkResetOnSplitsReset.Checked = ResetOnSplitsReset; }
        if (chkOnlyCountWhileTimerRunning != null) { chkOnlyCountWhileTimerRunning.Checked = OnlyCountWhileTimerRunning; }
        if (chkShowStatusDot != null) { chkShowStatusDot.Checked = ShowStatusDot; }
        if (chkDebugLog != null) { chkDebugLog.Checked = DebugLog; }
        RegisterHotKeys();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { RefreshValueBoxes(); }
    }

    // Internal so the component can force a resync after a counter-specific
    // control (e.g. the Kills/Destruction radio) changes which tally a row's
    // value box displays, without waiting for the box to lose focus first.
    internal void RefreshValueBoxes()
    {
        foreach (CounterRow row in rows)
        {
            if (!row.ValueBox.Focused) { row.ValueBox.Text = row.GetValue().ToString(); }
        }
    }

    // Refreshes one counter's value box even while it has focus: an adjust
    // hotkey changed the value under it, and the box's stale text would
    // otherwise be committed over the nudge when it loses focus.
    internal void RefreshValueBox(string counterId)
    {
        foreach (CounterRow row in rows)
        {
            if (row.Id == counterId) { row.ValueBox.Text = row.GetValue().ToString(); }
        }
    }

    private void CaptureKey(TextBox box, Action<KeyOrButton> setter)
    {
        string previous = box.Text;
        box.Text = "Set Hotkey...";

        KeyEventHandler keyDown = null;
        EventHandler leave = null;
        EventHandlerT<GamepadButton> gamepad = null;

        void unhook()
        {
            box.KeyDown -= keyDown;
            box.Leave -= leave;
            Hook.AnyGamepadButtonPressed -= gamepad;
        }

        keyDown = (s, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) { return; }

            var k = e.KeyCode == Keys.Escape ? null : new KeyOrButton(e.KeyCode | e.Modifiers);
            setter(k);
            unhook();
            box.Text = FormatKey(k);
            ActiveControl = null;
            RegisterHotKeys();
        };

        leave = (_, __) =>
        {
            unhook();
            if (box.Text == "Set Hotkey...") { box.Text = previous; }
        };

        gamepad = (_, btn) =>
        {
            var k = new KeyOrButton(btn);
            setter(k);
            unhook();
            void apply()
            {
                box.Text = FormatKey(k);
                ActiveControl = null;
                RegisterHotKeys();
            }
            if (InvokeRequired) { Invoke(apply); } else { apply(); }
        };

        box.KeyDown += keyDown;
        box.Leave += leave;
        Hook.AnyGamepadButtonPressed += gamepad;
    }

    private void RegisterHotKeys()
    {
        try
        {
            Hook.UnregisterAllHotkeys();
            if (ResetKey != null) { Hook.RegisterHotKey(ResetKey); }
            if (IncrementKey != null) { Hook.RegisterHotKey(IncrementKey); }
            if (DecrementKey != null) { Hook.RegisterHotKey(DecrementKey); }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private static string FormatKey(KeyOrButton key)
    {
        if (key == null) { return "None"; }
        string s = key.ToString();
        if (key.IsButton)
        {
            int i = s.LastIndexOf(' ');
            if (i != -1) { s = s[..i]; }
        }
        return s;
    }

    // Called by the component each poll to surface emulator attach state.
    public void SetStatus(string text)
    {
        if (lblStatus != null) { lblStatus.Text = text; }
    }

    public bool IsEnabled(string counterId) => enabled.Contains(counterId);

    public void SetEnabled(string counterId, bool value)
    {
        if (value) { enabled.Add(counterId); }
        else { enabled.Remove(counterId); }
    }

    public bool IsBankOnSave(string counterId)
        => bankOnSave.TryGetValue(counterId, out bool value) ? value : DefaultBankOnSave(counterId);

    public void SetBankOnSave(string counterId, bool value) => bankOnSave[counterId] = value;

    // Moons banking is opt-in (a moon lost to a death revert feels punitive by
    // default); every other toggle counter keeps the established default ON.
    // A 1-Up mushroom respawns after a death, so re-collecting it would double
    // count under the plain history; immediate counting is the true-100% view
    // and the toggle is the low% out.
    internal static bool DefaultBankOnSave(string counterId) => counterId != "moons" && counterId != "oneups";

    public XmlNode GetSettings(XmlDocument document)
    {
        XmlElement parent = document.CreateElement("Settings");
        CreateSettingsNode(document, parent);
        return parent;
    }

    public void SetSettings(XmlNode node)
    {
        var e = (XmlElement)node;

        ResetKey = ParseKey(e["ResetKey"], DefaultResetKey);
        IncrementKey = ParseKey(e["IncrementKey"], DefaultIncrementKey);
        DecrementKey = ParseKey(e["DecrementKey"], DefaultDecrementKey);
        HotkeyTarget = string.IsNullOrEmpty(e["HotkeyTarget"]?.InnerText) ? "oneups" : e["HotkeyTarget"].InnerText;
        RowHeight = SettingsHelper.ParseInt(e["RowHeight"], 45);
        Alignment = Enum.TryParse(e["Alignment"]?.InnerText, out HAlignment align) ? align : HAlignment.Center;
        ReserveDigits = SettingsHelper.ParseInt(e["ReserveDigits"], ValueWidth.DefaultDigits);
        ResetOnSplitsReset = SettingsHelper.ParseBool(e["ResetOnSplitsReset"], true);
        OnlyCountWhileTimerRunning = SettingsHelper.ParseBool(e["OnlyCountWhileTimerRunning"], true);
        DebugLog = SettingsHelper.ParseBool(e["DebugLog"], false);
        ShowStatusDot = SettingsHelper.ParseBool(e["ShowStatusDot"], true);

        enabled.Clear();
        XmlElement enabledNode = e["EnabledCounters"];
        if (enabledNode != null)
        {
            foreach (XmlElement c in enabledNode.GetElementsByTagName("Counter"))
            {
                if (!string.IsNullOrEmpty(c.InnerText)) { enabled.Add(c.InnerText); }
            }
        }

        bankOnSave.Clear();
        XmlElement bankMapNode = e["BankOnSave"];
        if (bankMapNode != null)
        {
            foreach (XmlElement c in bankMapNode.GetElementsByTagName("Counter"))
            {
                string id = c.GetAttribute("id");
                if (!string.IsNullOrEmpty(id) && bool.TryParse(c.InnerText, out bool on))
                {
                    bankOnSave[id] = on;
                }
            }
        }
        // Legacy pre-v0.6 layouts: BankDisabled membership meant "toggle off";
        // ids absent from both structures fall through to DefaultBankOnSave.
        XmlElement bankNode = e["BankDisabled"];
        if (bankNode != null)
        {
            foreach (XmlElement c in bankNode.GetElementsByTagName("Counter"))
            {
                if (!string.IsNullOrEmpty(c.InnerText) && !bankOnSave.ContainsKey(c.InnerText))
                {
                    bankOnSave[c.InnerText] = false;
                }
            }
        }

        RefreshFromModel();
    }

    // Absent element = a layout saved before the key existed, so it keeps the
    // default; present but empty = the user unbound it.
    private static KeyOrButton ParseKey(XmlElement el, KeyOrButton whenAbsent)
    {
        if (el == null) { return whenAbsent; }
        return string.IsNullOrEmpty(el.InnerText) ? null : new KeyOrButton(el.InnerText);
    }

    public int GetSettingsHashCode() => CreateSettingsNode(null, null);

    private int CreateSettingsNode(XmlDocument document, XmlElement parent)
    {
        int hash = SettingsHelper.CreateSetting(document, parent, "Version", "1");
        hash ^= SettingsHelper.CreateSetting(document, parent, "ResetKey", ResetKey);
        hash ^= SettingsHelper.CreateSetting(document, parent, "IncrementKey", IncrementKey);
        hash ^= SettingsHelper.CreateSetting(document, parent, "DecrementKey", DecrementKey);
        hash ^= SettingsHelper.CreateSetting(document, parent, "HotkeyTarget", HotkeyTarget);
        hash ^= SettingsHelper.CreateSetting(document, parent, "RowHeight", RowHeight);
        hash ^= SettingsHelper.CreateSetting(document, parent, "Alignment", Alignment.ToString());
        hash ^= SettingsHelper.CreateSetting(document, parent, "ReserveDigits", ReserveDigits);
        hash ^= SettingsHelper.CreateSetting(document, parent, "ResetOnSplitsReset", ResetOnSplitsReset);
        hash ^= SettingsHelper.CreateSetting(document, parent, "OnlyCountWhileTimerRunning", OnlyCountWhileTimerRunning);
        hash ^= SettingsHelper.CreateSetting(document, parent, "DebugLog", DebugLog);
        hash ^= SettingsHelper.CreateSetting(document, parent, "ShowStatusDot", ShowStatusDot);

        var bankPairs = new List<KeyValuePair<string, bool>>();
        foreach (string id in toggleIds)
        {
            bankPairs.Add(new KeyValuePair<string, bool>(id, IsBankOnSave(id)));
        }

        if (document != null && parent != null)
        {
            XmlElement enabledNode = document.CreateElement("EnabledCounters");
            foreach (string id in enabled)
            {
                XmlElement c = document.CreateElement("Counter");
                c.InnerText = id;
                enabledNode.AppendChild(c);
            }
            parent.AppendChild(enabledNode);

            XmlElement bankMapNode = document.CreateElement("BankOnSave");
            foreach (KeyValuePair<string, bool> kv in bankPairs)
            {
                XmlElement c = document.CreateElement("Counter");
                c.SetAttribute("id", kv.Key);
                c.InnerText = kv.Value.ToString();
                bankMapNode.AppendChild(c);
            }
            parent.AppendChild(bankMapNode);
        }

        return CombineSetHashes(hash, enabled, bankPairs);
    }

    // Fold the enabled set and the BankOnSave pairs into the settings hash.
    // Each structure folds commutatively (iteration order is unspecified) into
    // its own sub-hash with a value-dependent salt for the pairs, then the
    // sub-hashes combine order-sensitively so the structures can't cancel.
    internal static int CombineSetHashes(int hash, IEnumerable<string> enabledIds,
        IEnumerable<KeyValuePair<string, bool>> bankOnSave)
    {
        int enabledHash = 0;
        int bankHash = 0;
        foreach (string id in enabledIds) { enabledHash ^= id.GetHashCode(); }
        foreach (KeyValuePair<string, bool> kv in bankOnSave)
        {
            bankHash ^= kv.Key.GetHashCode() * (kv.Value ? 397 : 31);
        }
        hash = hash * 397 ^ enabledHash;
        return hash * 397 ^ bankHash;
    }
}
