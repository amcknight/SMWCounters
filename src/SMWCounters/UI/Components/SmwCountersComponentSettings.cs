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
    public int RowHeight { get; set; } = 45;
    public HAlignment Alignment { get; set; } = HAlignment.Center;
    // Digits every value cell reserves room for before it starts widening.
    public int ReserveDigits { get; set; } = ValueWidth.DefaultDigits;
    // On (default): counters only tally during a live run. Off: they keep
    // counting with the timer stopped, for challenge runs that never start it.
    public bool OnlyCountWhileTimerRunning { get; set; } = true;

    // Content width. LiveSplit hosts this control in a fixed-height dialog
    // whose vertical scrollbar eats ~17px of client width, so anything wider
    // than the host minus that bar sprouts a horizontal scrollbar too.
    private const int DialogWidth = 460;
    public bool ResetOnSplitsReset { get; set; } = true;
    public bool DebugLog { get; set; } = false;
    public bool ShowStatusDot { get; set; } = true;

    public SmwCountersComponentSettings(bool allowGamepads)
    {
        Hook = new CompositeHook(allowGamepads);
        ResetKey = new KeyOrButton(Keys.F2);
        Size = new Size(420, 240);
    }

    private readonly List<CounterRow> rows = new();
    private TextBox txtReset;
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
                    Location = new Point(252, y + 2),
                    AutoSize = true,
                    Checked = IsBankOnSave(id),
                };
                row.BankToggle.CheckedChanged += (_, __) => SetBankOnSave(id, row.BankToggle.Checked);
                bankToolTip.SetToolTip(row.BankToggle,
                    "Discard on death: unbanked " + defaultLabel + " (shown gold) are discarded if you die " +
                    "before a checkpoint or exit. Both histories are always tracked; this only picks which one is shown.");
                Controls.Add(row.BankToggle);
            }

            if (extras != null)
            {
                extras.Location = new Point(275, y);
                Controls.Add(extras);
                row.CounterSpecific = extras;
            }

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
            Location = new Point(120, y),
            Width = 220,
        };
        txtReset.Enter += (_, __) => CaptureKey(txtReset, k => ResetKey = k);
        Controls.Add(txtReset);
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
            Value = ValueWidth.ClampDigits(ReserveDigits),
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

        RegisterHotKeys();
    }

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
        if (trkHeight != null) { trkHeight.Value = Math.Max(trkHeight.Minimum, Math.Min(trkHeight.Maximum, RowHeight)); }
        if (numReserveDigits != null) { numReserveDigits.Value = ValueWidth.ClampDigits(ReserveDigits); }
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
    internal static bool DefaultBankOnSave(string counterId) => counterId != "moons";

    public XmlNode GetSettings(XmlDocument document)
    {
        XmlElement parent = document.CreateElement("Settings");
        CreateSettingsNode(document, parent);
        return parent;
    }

    public void SetSettings(XmlNode node)
    {
        var e = (XmlElement)node;

        XmlElement rst = e["ResetKey"];
        ResetKey = rst != null && !string.IsNullOrEmpty(rst.InnerText) ? new KeyOrButton(rst.InnerText) : null;
        RowHeight = SettingsHelper.ParseInt(e["RowHeight"], 45);
        Alignment = Enum.TryParse(e["Alignment"]?.InnerText, out HAlignment align) ? align : HAlignment.Center;
        ReserveDigits = ValueWidth.ClampDigits(SettingsHelper.ParseInt(e["ReserveDigits"], ValueWidth.DefaultDigits));
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

    public int GetSettingsHashCode() => CreateSettingsNode(null, null);

    private int CreateSettingsNode(XmlDocument document, XmlElement parent)
    {
        int hash = SettingsHelper.CreateSetting(document, parent, "Version", "1");
        hash ^= SettingsHelper.CreateSetting(document, parent, "ResetKey", ResetKey);
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
