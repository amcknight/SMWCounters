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
    // Counters with banking turned OFF (plain-tally). Absent id => banking ON.
    private readonly HashSet<string> bankDisabled = new();

    public KeyOrButton ResetKey { get; set; }
    public int RowHeight { get; set; } = 45;
    public HAlignment Alignment { get; set; } = HAlignment.Center;
    public bool ResetOnSplitsReset { get; set; } = true;

    public SmwCountersComponentSettings(bool allowGamepads)
    {
        Hook = new CompositeHook(allowGamepads);
        ResetKey = new KeyOrButton(Keys.F2);
        Size = new Size(420, 240);
    }

    private readonly List<CounterRow> rows = new();
    private TextBox txtReset;
    private TrackBar trkHeight;
    private RadioButton rdoLeft;
    private RadioButton rdoCenter;
    private RadioButton rdoRight;
    private CheckBox chkResetOnSplitsReset;
    private Label lblStatus;

    private sealed class CounterRow
    {
        public string Id;
        public CheckBox Enable;
        public TextBox ValueBox;
        public Button ResetValue;
        public Action OnResetValue;
        public Func<int> GetValue;
        public Action<int> SetValue;
        public Control CounterSpecific;
        public Action RefreshExtras;
    }

    // Component calls this once at construction with the list of known counters.
    public void BuildUi(IReadOnlyList<(string Id, string DefaultLabel, Control Extras, Action ResetValue, Func<int> GetValue, Action<int> SetValue, Action RefreshExtras)> counters)
    {
        Controls.Clear();
        rows.Clear();

        int y = 10;

        foreach ((string id, string defaultLabel, Control extras, Action resetValue, Func<int> getValue, Action<int> setValue, Action refreshExtras) in counters)
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
            };
            Controls.Add(row.Enable);

            row.ValueBox = new TextBox
            {
                Text = getValue().ToString(),
                Location = new Point(140, y),
                Width = 45,
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
        y += 30;

        chkResetOnSplitsReset = new CheckBox
        {
            Text = "Reset counter values when splits reset",
            Location = new Point(10, y),
            AutoSize = true,
            Checked = ResetOnSplitsReset,
        };
        chkResetOnSplitsReset.CheckedChanged += (_, __) => ResetOnSplitsReset = chkResetOnSplitsReset.Checked;
        Controls.Add(chkResetOnSplitsReset);
        y += 28;

        lblStatus = new Label
        {
            Text = "(not polled yet)",
            Location = new Point(10, y),
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
        };
        Controls.Add(lblStatus);
        y += 24;

        Size = new Size(480, y + 10);

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
        if (row.CounterSpecific != null) { row.CounterSpecific.Enabled = on; }
    }

    // Re-syncs visible row widgets from the data model after SetSettings is called.
    public void RefreshFromModel()
    {
        foreach (CounterRow row in rows)
        {
            row.Enable.Checked = IsEnabled(row.Id);
            row.ValueBox.Text = row.GetValue().ToString();
            SyncRowEnabled(row);
            row.RefreshExtras?.Invoke();
        }
        if (txtReset != null) { txtReset.Text = FormatKey(ResetKey); }
        if (trkHeight != null) { trkHeight.Value = Math.Max(trkHeight.Minimum, Math.Min(trkHeight.Maximum, RowHeight)); }
        if (rdoLeft != null)
        {
            rdoLeft.Checked = Alignment == HAlignment.Left;
            rdoCenter.Checked = Alignment == HAlignment.Center;
            rdoRight.Checked = Alignment == HAlignment.Right;
        }
        if (chkResetOnSplitsReset != null) { chkResetOnSplitsReset.Checked = ResetOnSplitsReset; }
        RegisterHotKeys();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
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

    public IEnumerable<string> EnabledIds => enabled;

    public bool IsBankOnSave(string counterId) => !bankDisabled.Contains(counterId);

    public void SetBankOnSave(string counterId, bool value)
    {
        if (value) { bankDisabled.Remove(counterId); }
        else { bankDisabled.Add(counterId); }
    }

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
        ResetOnSplitsReset = SettingsHelper.ParseBool(e["ResetOnSplitsReset"], true);

        enabled.Clear();
        XmlElement enabledNode = e["EnabledCounters"];
        if (enabledNode != null)
        {
            foreach (XmlElement c in enabledNode.GetElementsByTagName("Counter"))
            {
                if (!string.IsNullOrEmpty(c.InnerText)) { enabled.Add(c.InnerText); }
            }
        }

        bankDisabled.Clear();
        XmlElement bankNode = e["BankDisabled"];
        if (bankNode != null)
        {
            foreach (XmlElement c in bankNode.GetElementsByTagName("Counter"))
            {
                if (!string.IsNullOrEmpty(c.InnerText)) { bankDisabled.Add(c.InnerText); }
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
        hash ^= SettingsHelper.CreateSetting(document, parent, "ResetOnSplitsReset", ResetOnSplitsReset);

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

            XmlElement bankNode = document.CreateElement("BankDisabled");
            foreach (string id in bankDisabled)
            {
                XmlElement c = document.CreateElement("Counter");
                c.InnerText = id;
                bankNode.AppendChild(c);
            }
            parent.AppendChild(bankNode);
        }

        foreach (string id in enabled) { hash ^= id.GetHashCode(); }
        foreach (string id in bankDisabled) { hash ^= id.GetHashCode(); }
        return hash;
    }
}
