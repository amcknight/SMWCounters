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
    private readonly Dictionary<string, string> labels = new();

    public KeyOrButton ResetKey { get; set; }
    public int RowHeight { get; set; } = 50;
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
    private ComboBox cboAlignment;
    private CheckBox chkResetOnSplitsReset;
    private Label lblStatus;

    private sealed class CounterRow
    {
        public string Id;
        public CheckBox Enable;
        public TextBox Label;
        public Button ResetValue;
        public Action OnResetValue; // set by the component
        public Control CounterSpecific; // optional extra control (e.g. moon dedupe radio)
    }

    // Component calls this once at construction with the list of known counters.
    public void BuildUi(IReadOnlyList<(string Id, string DefaultLabel, Control Extras, Action ResetValue)> counters)
    {
        Controls.Clear();
        rows.Clear();

        int y = 10;

        foreach ((string id, string defaultLabel, Control extras, Action resetValue) in counters)
        {
            var row = new CounterRow { Id = id, OnResetValue = resetValue };

            row.Enable = new CheckBox
            {
                Text = defaultLabel,
                Location = new Point(10, y),
                AutoSize = true,
                Checked = IsEnabled(id),
            };
            row.Enable.CheckedChanged += (_, __) => SetEnabled(id, row.Enable.Checked);
            Controls.Add(row.Enable);

            row.Label = new TextBox
            {
                Text = GetLabelOverride(id) ?? "",
                Location = new Point(120, y - 2),
                Width = 120,
            };
            row.Label.TextChanged += (_, __) => SetLabelOverride(id, row.Label.Text);
            Controls.Add(row.Label);

            row.ResetValue = new Button
            {
                Text = "Reset value",
                Location = new Point(250, y - 4),
                AutoSize = true,
            };
            row.ResetValue.Click += (_, __) => row.OnResetValue?.Invoke();
            Controls.Add(row.ResetValue);

            if (extras != null)
            {
                y += 22;
                extras.Location = new Point(30, y);
                Controls.Add(extras);
                row.CounterSpecific = extras;
                y += extras.Height + 8;
            }
            else
            {
                y += 28;
            }

            rows.Add(row);
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
        // TrackBar paints its slider/ticks beyond its nominal Height, so the
        // next row needs extra clearance to avoid clipping the control below.
        y += 48;

        Controls.Add(new Label
        {
            Text = "Alignment:",
            Location = new Point(10, y + 4),
            AutoSize = true,
        });
        cboAlignment = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, y),
            Width = 100,
        };
        cboAlignment.Items.AddRange(new object[] { "Left", "Center", "Right" });
        cboAlignment.SelectedIndex = (int)Alignment;
        cboAlignment.SelectedIndexChanged += (_, __) => Alignment = (HAlignment)cboAlignment.SelectedIndex;
        Controls.Add(cboAlignment);
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
            Text = "Emulator: (not polled yet)",
            Location = new Point(10, y),
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
        };
        Controls.Add(lblStatus);
        y += 24;

        Size = new Size(360, y + 10);

        RegisterHotKeys();
    }

    // Re-syncs visible row widgets from the data model after SetSettings is called.
    public void RefreshFromModel()
    {
        foreach (CounterRow row in rows)
        {
            row.Enable.Checked = IsEnabled(row.Id);
            row.Label.Text = GetLabelOverride(row.Id) ?? "";
        }
        if (txtReset != null) { txtReset.Text = FormatKey(ResetKey); }
        if (trkHeight != null) { trkHeight.Value = Math.Max(trkHeight.Minimum, Math.Min(trkHeight.Maximum, RowHeight)); }
        if (cboAlignment != null) { cboAlignment.SelectedIndex = (int)Alignment; }
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
        if (lblStatus != null) { lblStatus.Text = "Emulator: " + text; }
    }

    public bool IsEnabled(string counterId) => enabled.Contains(counterId);

    public void SetEnabled(string counterId, bool value)
    {
        if (value) { enabled.Add(counterId); }
        else { enabled.Remove(counterId); }
    }

    public IEnumerable<string> EnabledIds => enabled;

    public string GetLabelOverride(string counterId) =>
        labels.TryGetValue(counterId, out string s) ? s : null;

    public void SetLabelOverride(string counterId, string label)
    {
        if (string.IsNullOrEmpty(label)) { labels.Remove(counterId); }
        else { labels[counterId] = label; }
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
        RowHeight = SettingsHelper.ParseInt(e["RowHeight"], 50);
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

        labels.Clear();
        XmlElement labelsNode = e["CounterLabels"];
        if (labelsNode != null)
        {
            foreach (XmlElement l in labelsNode.GetElementsByTagName("Label"))
            {
                string id = l.GetAttribute("id");
                if (!string.IsNullOrEmpty(id))
                {
                    labels[id] = l.InnerText ?? "";
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

            XmlElement labelsNode = document.CreateElement("CounterLabels");
            foreach (KeyValuePair<string, string> kv in labels)
            {
                XmlElement l = document.CreateElement("Label");
                l.SetAttribute("id", kv.Key);
                l.InnerText = kv.Value;
                labelsNode.AppendChild(l);
            }
            parent.AppendChild(labelsNode);
        }

        foreach (string id in enabled) { hash ^= id.GetHashCode(); }
        foreach (KeyValuePair<string, string> kv in labels)
        {
            hash ^= kv.Key.GetHashCode() ^ (kv.Value ?? "").GetHashCode();
        }
        return hash;
    }
}
