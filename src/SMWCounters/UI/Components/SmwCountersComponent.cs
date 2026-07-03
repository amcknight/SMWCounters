using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Model;
using LiveSplit.Model.Input;
using LiveSplit.SmwCounters.Counters;
using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.UI.Components;

public class SmwCountersComponent : IComponent
{
    private const float CellGap = 14f;

    private readonly LiveSplitState state;
    private readonly Timer pollTimer;
    private readonly SnesEmu emu = new();

    // Shared "always detached" memory used to flush per-counter edge state
    // when polling is gated off (timer not running). Re-uses each counter's
    // existing on-detach branch so we don't need a new ISmwCounter method.
    private static readonly InertMemory inert = new();

    private sealed class InertMemory : ISnesMemory
    {
        public bool IsAttached => false;
        public bool ReadWramByte(int snesOffset, out byte value) { value = 0; return false; }
    }

    // All known counters, registered at construction. The Settings hold the
    // user's enabled subset.
    private readonly IReadOnlyList<ISmwCounter> counters;

    private readonly Dictionary<string, SimpleLabel> labelCells = new();
    private readonly Dictionary<string, SimpleLabel> valueCells = new();
    private readonly GraphicsCache cache = new();

    public SmwCountersComponentSettings Settings { get; }

    public string ComponentName => "SMW Counters";

    public float VerticalHeight { get; private set; } = 10f;
    public float MinimumHeight { get; private set; }
    public float HorizontalWidth { get; private set; }
    public float MinimumWidth => 80f;

    public float PaddingTop { get; private set; }
    public float PaddingBottom { get; private set; }
    public float PaddingLeft => 7f;
    public float PaddingRight => 7f;

    public IDictionary<string, Action> ContextMenuControls => null;

    public SmwCountersComponent(LiveSplitState state)
    {
        this.state = state;

        // Build the registry of known counters.
        var moon = new MoonCounter();
        counters = new ISmwCounter[]
        {
            new DeathCounter(),
            new ExitCounter(),
            moon,
            new JumpCounter(),
            new PowerupCounter(),
        };

        foreach (ISmwCounter c in counters)
        {
            labelCells[c.Id] = new SimpleLabel();
            valueCells[c.Id] = new SimpleLabel();
        }

        bool allowGamepads = state.Settings.HotkeyProfiles.First().Value.AllowGamepadsAsHotkeys;
        Settings = new SmwCountersComponentSettings(allowGamepads);
        Settings.Hook.KeyOrButtonPressed += Hook_KeyOrButtonPressed;

        // Wire up per-counter rows. Counter-specific extras live here so the
        // settings UserControl doesn't know about individual counter types.
        var rows = new List<(string Id, string DefaultLabel, Control Extras, Action ResetValue, Func<int> GetValue, Action<int> SetValue, Action RefreshExtras)>();
        foreach (ISmwCounter c in counters)
        {
            ISmwCounter counter = c; // capture per-iteration
            (Control extras, Action refreshExtras) = BuildExtras(counter);
            rows.Add((counter.Id, counter.DefaultLabel, extras, () => counter.Reset(),
                      () => counter.Value, v => counter.SetValue(v), refreshExtras));
        }
        Settings.BuildUi(rows);

        pollTimer = new Timer { Interval = 15 };
        pollTimer.Tick += (_, __) => Poll();
        pollTimer.Enabled = true;

        state.OnReset += State_OnReset;
    }

    private void State_OnReset(object sender, TimerPhase phase)
    {
        if (!Settings.ResetOnSplitsReset) { return; }
        foreach (ISmwCounter c in counters)
        {
            if (Settings.IsEnabled(c.Id)) { c.Reset(); }
        }
    }

    private (Control control, Action refresh) BuildExtras(ISmwCounter counter)
    {
        if (counter is MoonCounter moon)
        {
            var panel = new Panel { Width = 400, Height = 24, Padding = new Padding(0) };
            var rdoAll = new RadioButton
            {
                Text = "All",
                AutoSize = true,
                Checked = moon.DedupeMode == MoonDedupeMode.All,
                Location = new Point(0, 4),
            };
            var rdoLevel = new RadioButton
            {
                Text = "Per level",
                AutoSize = true,
                Checked = moon.DedupeMode == MoonDedupeMode.PerLevel,
                Location = new Point(48, 4),
            };
            var rdoRoom = new RadioButton
            {
                Text = "Per room",
                AutoSize = true,
                Checked = moon.DedupeMode == MoonDedupeMode.PerRoom,
                Location = new Point(130, 4),
            };
            rdoAll.CheckedChanged   += (_, __) => { if (rdoAll.Checked)   { moon.DedupeMode = MoonDedupeMode.All; } };
            rdoLevel.CheckedChanged += (_, __) => { if (rdoLevel.Checked) { moon.DedupeMode = MoonDedupeMode.PerLevel; } };
            rdoRoom.CheckedChanged  += (_, __) => { if (rdoRoom.Checked)  { moon.DedupeMode = MoonDedupeMode.PerRoom; } };
            panel.Controls.Add(rdoAll);
            panel.Controls.Add(rdoLevel);
            panel.Controls.Add(rdoRoom);
            Action refresh = () =>
            {
                rdoAll.Checked = moon.DedupeMode == MoonDedupeMode.All;
                rdoLevel.Checked = moon.DedupeMode == MoonDedupeMode.PerLevel;
                rdoRoom.Checked = moon.DedupeMode == MoonDedupeMode.PerRoom;
            };
            return (panel, refresh);
        }
        if (counter is BankedCounter)
        {
            var chk = new CheckBox
            {
                Text = "Bank on save",
                AutoSize = true,
                Checked = Settings.IsBankOnSave(counter.Id),
                Location = new Point(0, 4),
            };
            chk.CheckedChanged += (_, __) => Settings.SetBankOnSave(counter.Id, chk.Checked);
            var panel = new Panel { Width = 160, Height = 24, Padding = new Padding(0) };
            panel.Controls.Add(chk);
            Action refresh = () => chk.Checked = Settings.IsBankOnSave(counter.Id);
            return (panel, refresh);
        }
        return (null, null);
    }

    private void Hook_KeyOrButtonPressed(object sender, KeyOrButton e)
    {
        if (e == Settings.ResetKey)
        {
            foreach (ISmwCounter c in counters)
            {
                if (Settings.IsEnabled(c.Id)) { c.Reset(); }
            }
        }
    }

    private void Poll()
    {
        // Only count during a live run. NotRunning covers title screen / file
        // select / overworld-before-start (where SMW demos and casual play
        // would otherwise pollute the counter); Ended covers post-run idle.
        // Paused counts as active so a pause/resume preserves edge continuity.
        bool timerActive = state.CurrentPhase == TimerPhase.Running
            || state.CurrentPhase == TimerPhase.Paused;

        if (!timerActive)
        {
            // Flush each counter's previous-byte state so that resuming after
            // a gap doesn't bridge a stale sample to a fresh one and produce
            // a spurious edge.
            foreach (ISmwCounter c in counters)
            {
                if (Settings.IsEnabled(c.Id)) { c.Poll(inert); }
            }
            Settings.SetStatus("Paused · timer not running");
            return;
        }

        if (!emu.TryAttach())
        {
            Settings.SetStatus(emu.LastError ?? "No emulator found");
            return;
        }
        Settings.SetStatus("Counting · " + emu.Describe());
        foreach (ISmwCounter c in counters)
        {
            if (c is BankedCounter bc) { bc.Banked = Settings.IsBankOnSave(c.Id); }
            if (Settings.IsEnabled(c.Id)) { c.Poll(emu); }
        }
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        try { Settings.Hook?.Poll(); } catch { }

        cache.Restart();
        foreach (ISmwCounter c in counters)
        {
            if (!Settings.IsEnabled(c.Id)) { continue; }
            string value = c.Value.ToString();
            valueCells[c.Id].Text = value;
            cache[c.Id + ".label"] = c.DefaultIcon != null ? "<icon>" : c.DefaultLabel;
            cache[c.Id + ".value"] = value;
            cache[c.Id + ".alert"] = c.ValueIsAlert;
        }

        if (invalidator != null && cache.HasChanged)
        {
            invalidator.Invalidate(0, 0, width, height);
        }
    }

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        Font layoutFont = state.LayoutSettings.TextFont;
        Color textColor = state.LayoutSettings.TextColor;

        var font = new Font(layoutFont.FontFamily, Settings.RowHeight * 0.5f, layoutFont.Style, GraphicsUnit.Pixel);
        float textHeight = g.MeasureString("A", font).Height;
        VerticalHeight = Settings.RowHeight;
        PaddingTop = Math.Max(0, (VerticalHeight - (0.75f * textHeight)) / 2f);
        PaddingBottom = PaddingTop;

        // Icons are scaled to fit the row height while preserving their native
        // aspect ratio, so a 16x24 sprite renders taller-than-wide.
        int iconHeight = (int)Math.Round(0.85f * Settings.RowHeight);

        // Measure each enabled counter's cell width: label-slot + " " + value.
        // Label slot is icon-aspect-scaled when the counter has an icon, else default-label text width.
        var enabled = counters.Where(c => Settings.IsEnabled(c.Id)).ToList();
        float totalWidth = 0f;
        var cellWidths = new Dictionary<string, (float labelW, float valueW)>();
        foreach (ISmwCounter c in enabled)
        {
            float labelW = c.DefaultIcon != null
                ? IconWidthFor(c.DefaultIcon, iconHeight)
                : g.MeasureString(c.DefaultLabel, font).Width;
            float valueW = g.MeasureString(c.Value.ToString("0"), font).Width;
            cellWidths[c.Id] = (labelW, valueW);
            if (totalWidth > 0) { totalWidth += CellGap; }
            totalWidth += labelW + 4 + valueW;
        }

        HorizontalWidth = totalWidth + 15;

        float x = Settings.Alignment switch
        {
            HAlignment.Center => Math.Max(5f, (width - totalWidth) / 2f),
            HAlignment.Right  => Math.Max(5f, width - totalWidth - 5f),
            _                 => 5f,
        };
        foreach (ISmwCounter c in enabled)
        {
            (float labelW, float valueW) = cellWidths[c.Id];

            if (c.DefaultIcon != null)
            {
                DrawIcon(g, c.DefaultIcon, x, height, labelW, iconHeight);
            }
            else
            {
                labelCells[c.Id].Text = c.DefaultLabel;
                ConfigureLabel(labelCells[c.Id], font, textColor, StringAlignment.Near, x, labelW, height);
                labelCells[c.Id].Draw(g);
            }
            x += labelW + 4;

            Color valueColor = c.ValueIsAlert ? state.LayoutSettings.BestSegmentColor : textColor;
            ConfigureLabel(valueCells[c.Id], font, valueColor, StringAlignment.Near, x, valueW, height);
            valueCells[c.Id].Draw(g);
            x += valueW + CellGap;
        }
    }

    private static float IconWidthFor(Image icon, int iconHeight)
        => (float)Math.Round((double)iconHeight * icon.Width / icon.Height);

    private static void DrawIcon(Graphics g, Image icon, float x, float height, float drawWidth, int iconHeight)
    {
        InterpolationMode prevInterp = g.InterpolationMode;
        PixelOffsetMode prevOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        float y = (height - iconHeight) / 2f;
        g.DrawImage(icon, x, y, drawWidth, iconHeight);
        g.InterpolationMode = prevInterp;
        g.PixelOffsetMode = prevOffset;
    }

    private void ConfigureLabel(SimpleLabel label, Font font, Color color, StringAlignment hAlign, float x, float width, float height)
    {
        label.HorizontalAlignment = hAlign;
        label.VerticalAlignment = StringAlignment.Center;
        label.X = x;
        label.Y = 0;
        label.Width = width;
        label.Height = height;
        label.Font = font;
        label.Brush = new SolidBrush(color);
        label.HasShadow = state.LayoutSettings.DropShadows;
        label.ShadowColor = state.LayoutSettings.ShadowsColor;
        label.OutlineColor = state.LayoutSettings.TextOutlineColor;
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        => DrawGeneral(g, state, HorizontalWidth, height, LayoutMode.Horizontal);

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        => DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Vertical);

    public Control GetSettingsControl(LayoutMode mode) => Settings;

    public XmlNode GetSettings(XmlDocument document)
    {
        var node = (XmlElement)Settings.GetSettings(document);

        XmlElement stateNode = document.CreateElement("CounterState");
        foreach (ISmwCounter c in counters)
        {
            XmlElement el = document.CreateElement(c.Id);
            c.SaveState(document, el);
            stateNode.AppendChild(el);
        }
        node.AppendChild(stateNode);
        return node;
    }

    public void SetSettings(XmlNode settings)
    {
        Settings.SetSettings(settings);

        XmlElement stateNode = ((XmlElement)settings)["CounterState"];
        if (stateNode != null)
        {
            foreach (ISmwCounter c in counters)
            {
                XmlElement el = stateNode[c.Id];
                if (el != null) { c.LoadState(el); }
            }
        }

        // Settings.SetSettings() above already called RefreshFromModel(), but
        // that ran before LoadState restored each counter's Value, so the
        // value boxes were populated from pre-load (typically zero) values.
        // Re-sync now that the real restored values are in place, so the
        // Leave/CommitValue path can't later commit a stale 0 over them.
        Settings.RefreshFromModel();
    }

    public int GetSettingsHashCode()
    {
        int hash = Settings.GetSettingsHashCode();
        foreach (ISmwCounter c in counters) { hash ^= c.Value.GetHashCode(); }
        return hash;
    }

    public void Dispose()
    {
        pollTimer?.Dispose();
        state.OnReset -= State_OnReset;
        Settings.Hook.KeyOrButtonPressed -= Hook_KeyOrButtonPressed;
        Settings.Hook.UnregisterAllHotkeys();
    }
}
