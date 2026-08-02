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
using LiveSplit.SmwCounters.Diagnostics;
using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.UI.Components;

public class SmwCountersComponent : IComponent
{
    private const float CellGap = 14f;

    private readonly LiveSplitState state;
    private readonly Timer pollTimer;
    private readonly SnesConnection connection = new();
    private readonly DebugLogger debugLog = new();
    private readonly EndedDisplayFreeze endedFreeze = new();

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

    // GDI objects are cached across draws; DrawGeneral runs at LiveSplit's
    // redraw rate and per-draw Font/SolidBrush allocations churn GDI handles.
    // SimpleLabel.Brush is a plain property (never disposed by the label), so
    // sharing cached brushes across labels is safe.
    private Font rowFont;
    private readonly Dictionary<int, SolidBrush> brushCache = new();
    private readonly System.Windows.Forms.ToolTip extrasToolTip = new();

    public SmwCountersComponentSettings Settings { get; }

    public string ComponentName => "SMW Counters";

    public float VerticalHeight { get; private set; } = 10f;
    // Consulted by LiveSplit's horizontal layout mode (the counterpart of
    // MinimumWidth below); leaving it 0 lets the layout collapse the row.
    public float MinimumHeight => Settings.RowHeight;
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
            new CoinCounter(),
            new KillCounter(),
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
        ResetAll();
    }

    // Hidden counters keep counting (see Poll), so a reset has to clear them
    // too — otherwise enabling one later surfaces a tally from before the run.
    private void ResetAll()
    {
        foreach (ISmwCounter c in counters) { c.Reset(); }
    }

    private (Control control, Action refresh) BuildExtras(ISmwCounter counter)
    {
        if (counter is MoonCounter moon)
        {
            var chk = new CheckBox
            {
                Text = "One per level",
                AutoSize = true,
                Checked = moon.DedupeMode == MoonDedupeMode.PerLevel,
                Location = new Point(0, 4),
            };
            chk.CheckedChanged += (_, __) => moon.DedupeMode = chk.Checked ? MoonDedupeMode.PerLevel : MoonDedupeMode.All;
            var panel = new Panel { Width = 160, Height = 24, Padding = new Padding(0) };
            panel.Controls.Add(chk);
            Action refresh = () => chk.Checked = moon.DedupeMode == MoonDedupeMode.PerLevel;
            return (panel, refresh);
        }
        if (counter is BankedCounter { HasBankToggle: true })
        {
            var chk = new CheckBox
            {
                Text = "Discard on death",
                AutoSize = true,
                Checked = Settings.IsBankOnSave(counter.Id),
                Location = new Point(0, 4),
            };
            chk.CheckedChanged += (_, __) => Settings.SetBankOnSave(counter.Id, chk.Checked);
            extrasToolTip.SetToolTip(chk, "Unbanked collects (shown gold) are discarded if you die before a checkpoint or exit.");
            var panel = new Panel { Width = 160, Height = 24, Padding = new Padding(0) };
            panel.Controls.Add(chk);
            Action refresh = () => chk.Checked = Settings.IsBankOnSave(counter.Id);
            return (panel, refresh);
        }
        if (counter is KillCounter kill)
        {
            var rdoKills = new RadioButton
            {
                Text = "Kills",
                AutoSize = true,
                Checked = kill.Mode == KillCountMode.Kills,
                Location = new Point(0, 2),
            };
            var rdoDestruction = new RadioButton
            {
                Text = "Destruction",
                AutoSize = true,
                Checked = kill.Mode == KillCountMode.Destruction,
                Location = new Point(52, 2),
            };
            rdoKills.CheckedChanged += (_, __) =>
            {
                // Flipping Mode changes what the row's value box displays and
                // what a Leave-triggered commit would write into, so resync
                // the box now. The just-checked radio has focus, so
                // RefreshValueBoxes' "don't touch the focused box" guard
                // leaves the value box itself free to refresh.
                if (rdoKills.Checked) { kill.Mode = KillCountMode.Kills; Settings.RefreshValueBoxes(); }
            };
            rdoDestruction.CheckedChanged += (_, __) =>
            {
                if (rdoDestruction.Checked) { kill.Mode = KillCountMode.Destruction; Settings.RefreshValueBoxes(); }
            };
            extrasToolTip.SetToolTip(rdoKills, "Count creatures killed (evidence-based not-alive sprite list applies).");
            extrasToolTip.SetToolTip(rdoDestruction, "Count anything destroyed: kills plus poofed/swallowed/converted objects.");
            var panel = new Panel { Width = 160, Height = 24, Padding = new Padding(0) };
            panel.Controls.Add(rdoKills);
            panel.Controls.Add(rdoDestruction);
            Action refresh = () =>
            {
                rdoKills.Checked = kill.Mode == KillCountMode.Kills;
                rdoDestruction.Checked = kill.Mode == KillCountMode.Destruction;
            };
            return (panel, refresh);
        }
        return (null, null);
    }

    private void Hook_KeyOrButtonPressed(object sender, KeyOrButton e)
    {
        if (e == Settings.ResetKey) { ResetAll(); }
    }

    private void Poll()
    {
        // Always-on discovery: the connection ticks every poll regardless of
        // timer phase, so the status dot is already green when a run starts
        // (structural discovery takes seconds; gating it on the timer would
        // lose the first seconds of counting).
        connection.Tick();
        if (Settings.DebugLog) { debugLog.LogStatus(connection.Status, connection.WindowTitle); }

        // Counters still only count during a live run. NotRunning covers
        // title screen / file select / overworld-before-start (where SMW
        // demos and casual play would otherwise pollute the counter). Paused
        // counts as active so a pause/resume preserves edge continuity.
        // Ended counts as active for timer parity: the counters keep tallying
        // across a premature final split (display pinned by endedFreeze), so
        // undoing the split reveals the true totals the way the timer jumps
        // to where it would have been.
        bool timerActive = state.CurrentPhase == TimerPhase.Running
            || state.CurrentPhase == TimerPhase.Paused
            || state.CurrentPhase == TimerPhase.Ended;

        // Before this tick's polling, so the capture on the transition into
        // Ended sees the values as of the split.
        endedFreeze.OnPhase(state.CurrentPhase == TimerPhase.Ended, counters);

        if (!timerActive)
        {
            // Flush every counter's previous-byte state (enabled or not) so
            // that resuming after a gap doesn't bridge a stale sample to a
            // fresh one and produce a spurious edge.
            foreach (ISmwCounter c in counters)
            {
                c.Poll(inert);
            }
            debugLog.Idle();
            Settings.SetStatus("Paused · timer not running · " + connection.Describe());
            return;
        }

        // Poll counters against the live connection even when it is not
        // (yet) attached: each counter's !IsAttached branch flushes its edge
        // state, so a mid-run detach can't bridge stale samples on reattach.
        foreach (ISmwCounter c in counters)
        {
            if (c is BankedCounter { HasBankToggle: true } bc) { bc.Banked = Settings.IsBankOnSave(c.Id); }
            // Every counter polls the live connection, enabled or not: the
            // enabled set controls what the overlay *shows*, not what counts.
            // Turning a counter on mid-run then reveals the tally it has been
            // keeping all along instead of starting it from zero. (Polling all
            // of them also keeps edge state continuous, so there is no stale
            // sample for a re-enable to bridge.)
            c.Poll(connection);
        }

        if (!connection.IsAttached)
        {
            debugLog.Idle();
            Settings.SetStatus(connection.Describe());
            return;
        }

        if (Settings.DebugLog)
        {
            debugLog.Poll(connection, counters, id => Settings.IsEnabled(id),
                          state.CurrentPhase.ToString(), connection.Describe());
            Settings.SetStatus("Counting · " + connection.Describe() + " · debug log → " + debugLog.LogPath);
        }
        else
        {
            debugLog.Close();
            Settings.SetStatus("Counting · " + connection.Describe());
        }
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        try { Settings.Hook?.Poll(); } catch { }

        cache.Restart();
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        foreach (ISmwCounter c in counters)
        {
            if (!Settings.IsEnabled(c.Id)) { continue; }
            string value = endedFreeze.ValueFor(c).ToString();
            valueCells[c.Id].Text = value;
            cache[c.Id + ".label"] = c.DefaultIcon != null ? "<icon>" : c.DefaultLabel;
            cache[c.Id + ".value"] = value;
            cache[c.Id + ".alert"] = endedFreeze.AlertFor(c);
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

        Font font = GetRowFont(layoutFont);
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
            float valueW = g.MeasureString(endedFreeze.ValueFor(c).ToString("0"), font).Width;
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

        // Status pixel: a tiny connection-health indicator pinned to the
        // component's left edge, vertically centered, outside the row flow so
        // it stays put regardless of counter layout or alignment.
        if (Settings.ShowStatusDot)
        {
            const float dotSize = 5f;
            using (var dotBrush = new SolidBrush(connection.DotColor))
            {
                g.FillRectangle(dotBrush, 3f, (height - dotSize) / 2f, dotSize, dotSize);
            }
        }

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

            Color valueColor = endedFreeze.AlertFor(c) ? state.LayoutSettings.BestSegmentColor : textColor;
            ConfigureLabel(valueCells[c.Id], font, valueColor, StringAlignment.Near, x, valueW, height);
            valueCells[c.Id].Draw(g);
            x += valueW + CellGap;
        }
    }

    private Font GetRowFont(Font layoutFont)
    {
        float size = Settings.RowHeight * 0.5f;
        if (rowFont == null || rowFont.Size != size
            || rowFont.FontFamily.Name != layoutFont.FontFamily.Name
            || rowFont.Style != layoutFont.Style)
        {
            rowFont?.Dispose();
            rowFont = new Font(layoutFont.FontFamily, size, layoutFont.Style, GraphicsUnit.Pixel);
        }
        return rowFont;
    }

    private SolidBrush GetBrush(Color color)
    {
        if (!brushCache.TryGetValue(color.ToArgb(), out SolidBrush brush))
        {
            brush = new SolidBrush(color);
            brushCache[color.ToArgb()] = brush;
        }
        return brush;
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
        label.Brush = GetBrush(color);
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
        foreach (ISmwCounter c in counters)
        {
            // Salted, order-sensitive fold: raw XOR of counter values is
            // commutative and self-inverse, so two counters holding equal
            // values cancel out and the layout hash misses real changes.
            // StateHash (not Value) so persisted-but-hidden state — banked
            // `saved`, moon dedupe mode, the off-display kill tally — also
            // dirties the hash instead of silently dropping on save.
            hash = hash * 397 ^ c.StateHash;
        }
        return hash;
    }

    public void Dispose()
    {
        debugLog.Close();
        pollTimer?.Dispose();
        extrasToolTip?.Dispose();
        rowFont?.Dispose();
        foreach (SolidBrush brush in brushCache.Values) { brush.Dispose(); }
        brushCache.Clear();
        state.OnReset -= State_OnReset;
        Settings.Hook.KeyOrButtonPressed -= Hook_KeyOrButtonPressed;
        Settings.Hook.UnregisterAllHotkeys();
    }
}
