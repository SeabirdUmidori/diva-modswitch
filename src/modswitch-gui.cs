// modswitch-gui - the window.  Every action goes through the engine in modswitch.cs, including its
// "only one line per file, verified afterwards" guarantee, so there is no second implementation
// of the writing and the table can never drift from what is on disk.
//
// Languages: 中文 / English / 日本語, defaulting to the OS UI language and falling back to English.
// Every string lives in modswitch-loc.cs; switching language re-texts the live window in place.
//
// build:  build.bat            (or the csc line inside it)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

static class ModSwitchGui
{
    internal static ListView lv;
    internal static TextBox log;
    internal static StatusStrip status;
    static TextBox pathBox;
    static Label head, langLbl;
    static ComboBox langBox;
    static Dictionary<string, Button> btns = new Dictionary<string, Button>();
    static Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
    static Panel header, buttons, topBar;
    static SplitContainer split;
    static FlowLayoutPanel buttonFlow;
    static int rows;
    static bool ready;                    // the console box: a replayed state change is not a user edit
    static bool settingLang;              // guard the language combo's own events
    const int CheckZone = 22;             // the glyph at the left of a row: click it, don't drag from it

    [STAThread]
    static void Main()
    {
        Loc.Detect();
        ModSwitch.Sink = delegate(string s) { Append(s); };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Form f = Build();
        if (!ModSwitch.FindGame()) Append(Loc.F("game.none", Loc.F("btn.pick")));
        ApplyLanguage();
        Reload();
        RunAudit();
        Application.Run(f);
    }

    // ------------------------------------------------------------------ construction
    internal static Form Build()
    {
        Form f = new Form();
        f.Size = new Size(1040, 690);
        f.StartPosition = FormStartPosition.CenterScreen;
        // Smaller than this and the docked strips (top bar + hint + wrapped buttons + status) leave no
        // room for the two panes, so the window is floored at what the layout actually needs.
        f.MinimumSize = new Size(820, 520);

        // ---- top bar: [选择游戏目录] [可编辑的路径........] [应用此路径] [语言 ▾]
        header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = 34;
        header.Padding = new Padding(8, 6, 8, 4);

        Button pick = MkButton("btn.pick", DockStyle.Left);
        pick.Click += delegate(object s, EventArgs e) { PickGame(); };

        Button apply = MkButton("btn.apply", DockStyle.Right);
        apply.Click += delegate(object s, EventArgs e) { ApplyPath(); };

        langBox = new ComboBox();
        langBox.DropDownStyle = ComboBoxStyle.DropDownList;
        langBox.Dock = DockStyle.Right;
        langBox.Width = 150;
        langBox.SelectedIndexChanged += OnLangChanged;

        langLbl = new Label();
        langLbl.AutoSize = true;
        langLbl.Dock = DockStyle.Right;
        langLbl.Padding = new Padding(10, 5, 2, 0);

        pathBox = new TextBox();
        pathBox.Dock = DockStyle.Fill;
        pathBox.Margin = new Padding(6, 0, 6, 0);
        pathBox.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyPath();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        Panel langZone = new Panel();
        langZone.Dock = DockStyle.Right;
        langZone.Width = 214;
        langZone.Controls.Add(langBox);
        langZone.Controls.Add(langLbl);

        header.Controls.Add(pathBox);
        header.Controls.Add(apply);
        header.Controls.Add(langZone);
        header.Controls.Add(pick);
        pathBox.BringToFront();           // Fill takes whatever the docked buttons leave

        head = new Label();
        head.AutoSize = false;
        head.Dock = DockStyle.Fill;
        head.Padding = new Padding(2, 2, 8, 0);
        head.ForeColor = SystemColors.GrayText;

        // The hint wraps, so its bar has to grow with the window instead of cutting the second line off:
        // FitHint() measures the wrapped height on every resize and sets this panel's height to match.
        topBar = new Panel();
        topBar.Dock = DockStyle.Top;
        topBar.Height = header.Height + 24;
        topBar.Controls.Add(head);
        topBar.Controls.Add(header);

        // ---- button strip.  Height is measured, not AutoSize: AutoSize asks a flow panel for its
        // preferred size WITHOUT a width constraint, so it always answers "one row", and every button
        // that does not fit on that row lands outside the strip - invisible instead of wrapping.
        buttonFlow = new FlowLayoutPanel();
        buttonFlow.Dock = DockStyle.Top;
        buttonFlow.AutoSize = false;
        buttonFlow.WrapContents = true;
        buttonFlow.Padding = new Padding(6, 3, 6, 3);
        MkAction("btn.addMod", delegate(object s, EventArgs e) { PickArchive(); });
        MkAction("btn.delMod", delegate(object s, EventArgs e) { DeleteSelected(); });
        MkAction("btn.refresh", delegate(object s, EventArgs e) { Reload(); });
        MkAction("btn.audit", delegate(object s, EventArgs e) { RunAudit(); });
        MkAction("btn.up", delegate(object s, EventArgs e) { MoveSelected(-1); });
        MkAction("btn.down", delegate(object s, EventArgs e) { MoveSelected(1); });
        // One button, not two: its text says what pressing it will do, taken from whether the loader is
        // actually in the game folder.  Two always-present buttons asked "which one is the current state?"
        MkAction("btn.loader", delegate(object s, EventArgs e)
        {
            if (ModSwitch.LoaderPresent) RemoveLoader(); else InstallLoader();
        });
        MkCheck("btn.console", OnConsoleToggled);
        MkAction("btn.undo", delegate(object s, EventArgs e) { ModSwitch.Undo(); Reload(); });
        MkAction("btn.help", delegate(object s, EventArgs e) { Help(); });

        buttons = new Panel();
        buttons.Dock = DockStyle.Top;
        buttons.AutoSize = false;             // FitButtons() sets it from a measurement taken at the real width
        buttons.BackColor = SystemColors.ControlLight;
        buttons.Controls.Add(buttonFlow);

        split = new SplitContainer();
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Horizontal;
        split.FixedPanel = FixedPanel.None;         // both panes grow with the window, not just the log
        split.Panel1MinSize = 120;
        split.Panel2MinSize = 90;

        lv = new ListView();
        lv.Dock = DockStyle.Fill;
        lv.View = View.Details;
        lv.CheckBoxes = false;                  // the state is column 0's glyph; a native box would sit on the selection click
        lv.FullRowSelect = true;
        lv.GridLines = true;
        lv.HideSelection = false;
        lv.MultiSelect = true;                      // Ctrl / Shift + click to pick several, then delete them at once
        lv.AllowDrop = true;                        // drag a row to change its load order
        lv.Columns.Add("", 40, HorizontalAlignment.Left);
        lv.Columns.Add("", 300, HorizontalAlignment.Left);
        lv.Columns.Add("", 200, HorizontalAlignment.Left);
        lv.Columns.Add("", 140, HorizontalAlignment.Left);
        lv.Columns.Add("", 76, HorizontalAlignment.Center);
        lv.Columns.Add("", 330, HorizontalAlignment.Left);
        lv.MouseDown += OnMouseDown;                // a press only records the row; the drag starts on movement
        lv.MouseMove += OnMouseMove;
        lv.MouseUp += OnMouseUp;
        lv.DragEnter += OnDragEnter;
        lv.DragOver += OnDragOver;
        lv.DragLeave += OnDragLeave;
        lv.DragDrop += OnDragDrop;
        lv.KeyDown += OnKey;
        // AllowDrop is ambient, so this makes the whole window a target - and since the drag data only
        // ever holds a ListViewItem or a file group, routing it all to the same handler keeps the list's
        // own row-reorder drag untouched.  Dropping a mod zip should not depend on hitting the table.
        f.AllowDrop = true;
        f.DragEnter += OnDragEnter;
        f.DragOver += OnDragOver;
        f.DragLeave += OnDragLeave;
        f.DragDrop += OnDragDrop;
        BuildKeepTimer();
        BuildMarkLine();
        split.Panel1.Controls.Add(lv);

        log = new TextBox();
        log.Dock = DockStyle.Fill;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Both;
        log.WordWrap = false;
        log.BackColor = Color.FromArgb(24, 24, 24);
        log.ForeColor = Color.Gainsboro;
        log.Font = new Font("Consolas", 9f);
        split.Panel2.Controls.Add(log);

        status = new StatusStrip();
        ToolStripStatusLabel lbl = new ToolStripStatusLabel();
        lbl.Spring = true;
        lbl.TextAlign = ContentAlignment.MiddleLeft;
        status.Items.Add(lbl);

        f.Controls.Add(split);
        f.Controls.Add(buttons);
        f.Controls.Add(topBar);
        // Two things docking cannot express on its own: the hint line wraps, so its bar has to be as tall
        // as the text needs at this width (otherwise a narrower window silently loses the second line);
        // and a SplitContainer's distance is meaningless until the window has a real height, so it is set
        // once the form is up and then scaled proportionally by FixedPanel = None on every resize.
        head.TextChanged += delegate { FitStrips(); };
        f.Resize += delegate { FitStrips(); };
        // The console tick box is synced from the file before the window is ever up; the native control
        // can replay that state as a CheckedChanged once its handle exists, and a replayed event is
        // not a request to write.
        f.Shown += delegate
        {
            ready = true;
            FitHint();
            FirstFit();
        };
        return f;
    }

    static bool splitFitted;

    /// The table gets ~62% of what is left after the strips; both panes keep their minimum heights.
    static void FirstFit()
    {
        if (splitFitted || split == null) return;
        int room = split.Height - split.SplitterWidth;
        if (room < split.Panel1MinSize + split.Panel2MinSize + 20) return;     // not laid out yet
        splitFitted = true;
        int want = Math.Max(split.Panel1MinSize,
                     Math.Min((int)(room * 0.62), room - split.Panel2MinSize));
        split.SplitterDistance = want;
    }

    /// Grow the hint bar to the wrapped height of its own text at the current width.
    static void FitHint()
    {
        if (head == null || topBar == null || header == null) return;
        int w = Math.Max(160, head.Width - head.Padding.Horizontal);
        Size need = TextRenderer.MeasureText(head.Text, head.Font, new Size(w, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        int h = header.Height + Math.Max(20, need.Height + 4);
        if (topBar.Height != h) topBar.Height = h;
    }

    /// The button strip's height, measured at the width it actually has.  AutoSize cannot do this: it
    /// asks a FlowLayoutPanel for its preferred size with no width constraint, and the answer is always
    /// "one row" - so the overflow rows sit outside a strip that thinks it is finished.
    static void FitButtons()
    {
        if (buttonFlow == null || buttons == null) return;
        int w = Math.Max(160, buttons.ClientSize.Width);
        int h = Math.Max(30, buttonFlow.GetPreferredSize(new Size(w, 0)).Height);
        if (buttonFlow.Height != h) buttonFlow.Height = h;
        if (buttons.Height != h) buttons.Height = h;
    }

    /// Both strips depend on the window width, and the button texts change with the language and with
    /// the loader state, so this runs from resize, from ApplyLanguage and from Reload.
    static void FitStrips() { FitHint(); FitButtons(); }

    static Button MkButton(string key, DockStyle dock)
    {
        Button b = new Button();
        b.AutoSize = true;
        b.Dock = dock;
        b.Margin = new Padding(3, 0, 3, 0);
        b.Padding = new Padding(8, 2, 8, 2);
        b.Tag = key;
        btns[key] = b;
        return b;
    }

    static void MkAction(string key, EventHandler handler)
    {
        Button b = MkButton(key, DockStyle.None);
        b.Margin = new Padding(3, 4, 3, 3);
        b.Click += handler;
        buttonFlow.Controls.Add(b);
    }

    /// A checkbox in the button strip whose tick IS the setting: toggling writes immediately, and every
    /// Reload re-ticks it from disk, so it can never drift from what config.toml actually says.
    static void MkCheck(string key, EventHandler onCheck)
    {
        CheckBox c = new CheckBox();
        c.AutoSize = true;
        c.Margin = new Padding(8, 11, 6, 3);
        c.Tag = key;
        c.Text = Loc.F(key);
        c.CheckedChanged += onCheck;
        checks[key] = c;
        buttonFlow.Controls.Add(c);
    }

    // ---------- the loader's debug console, as a tick box
    static bool settingCheck;

    static void OnConsoleToggled(object sender, EventArgs e)
    {
        if (settingCheck || !ready) return;                 // a checkbox ticking itself is not an edit
        bool want = consoleBox.Checked;
        if ((ModSwitch.RootValue("console") == "true") == want) return;
        ModSwitch.ConsoleSwitch(want ? "on" : "off");
        Reload();
    }

    static CheckBox consoleBox { get { return checks["btn.console"]; } }

    /// Push the settings that are shown as checkboxes back out of the files on disk.
    static void SyncChecks()
    {
        settingCheck = true;
        try
        {
            consoleBox.Checked = ModSwitch.RootValue("console") == "true";
            consoleBox.Enabled = File.Exists(ModSwitch.RootCfg);
        }
        finally { settingCheck = false; }
    }

    // ------------------------------------------------------------------ language
    internal static void ApplyLanguage()
    {
        settingLang = true;
        try
        {
            lv.FindForm().Text = Loc.F("title");
            string[] cols = { "col.row", "col.folder", "col.display", "col.author", "col.priority", "col.state" };
            for (int i = 0; i < cols.Length && i < lv.Columns.Count; i++) lv.Columns[i].Text = Loc.F(cols[i]);
            foreach (KeyValuePair<string, Button> kv in btns) kv.Value.Text = Loc.F(kv.Key);
            foreach (KeyValuePair<string, CheckBox> kv in checks) kv.Value.Text = Loc.F(kv.Key);
            SyncLoaderButton();                     // after the generic pass: its text is the state's, not the key's
            FitStrips();                            // re-texted buttons wrap into a different number of rows
            langLbl.Text = Loc.F("btn.lang");

            langBox.Items.Clear();
            foreach (string opt in Loc.Options) langBox.Items.Add(Loc.Label(opt));
            int sel = Array.IndexOf(Loc.Options, Loc.FromSystem ? "auto" : Loc.Lang);
            langBox.SelectedIndex = sel < 0 ? 0 : sel;
        }
        finally { settingLang = false; }
    }

    static void OnLangChanged(object sender, EventArgs e)
    {
        if (settingLang) return;
        string opt = Loc.Options[Math.Max(0, Math.Min(Loc.Options.Length - 1, langBox.SelectedIndex))];
        Loc.Choose(opt);
        ApplyLanguage();
        Reload();
        Append("=== " + Loc.F("btn.lang") + ": " + Loc.Label(opt) + (Loc.FromSystem ? " (" + Loc.F("lang.auto") + ")" : ""));
    }

    // ------------------------------------------------------------------ rows
    internal static void Reload() { Reload(null); }

    /// Rebuild the table.  `select` is the mod to leave highlighted afterwards; when it is null the
    /// currently selected row keeps its selection, so a reorder never loses your place.
    /// (A toggle does NOT come through here: Clear() would drop a multi-selection, so it re-cells in place.)
    internal static void Reload(string select)
    {
        string keep = select;
        if (keep == null && lv.SelectedItems.Count > 0) keep = (string)lv.SelectedItems[0].Tag;
        if (keep == null && lv.FocusedItem != null && lv.FocusedItem.Tag != null) keep = (string)lv.FocusedItem.Tag;
        {
            List<string> seq = ModSwitch.Order();
            List<string> prio = ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority")
                                ?? new List<string>();
            lv.BeginUpdate();
            lv.Items.Clear();
            for (int i = 0; i < seq.Count; i++)
            {
                string name = seq[i];
                string cfg = ModSwitch.Raw(ModSwitch.Cfg(name));
                bool on = ModSwitch.Enabled(name);
                int at = prio.IndexOf(name);
                var notes = new List<string>();
                var bad = ModSwitch.IncludeProblems(Path.Combine(ModSwitch.ModsDir, name), cfg, notes);
                string how = bad.Count > 0 ? string.Join(" ; ", bad.ToArray())
                             : (notes.Count > 0 ? string.Join(" ; ", notes.ToArray()) : Loc.F("state.ok"));
                // 优先级 = its position in the loader's array, or why it has none.
                string slot = at < 0 ? (on ? Loc.F("slot.notIn") : Loc.F("slot.off")) : (at + 1).ToString();
                ListViewItem row = new ListViewItem(Glyph(on) + (i + 1));
                string label = ModSwitch.Label(name) ?? "";
                int slash = label.IndexOf("  /  ");
                row.SubItems.Add(name);
                row.SubItems.Add(slash >= 0 ? label.Substring(0, slash) : label);
                row.SubItems.Add(slash >= 0 ? label.Substring(slash + 5) : "");
                row.SubItems.Add(slot);
                row.SubItems.Add(how);
                row.Tag = name;
                if (bad.Count > 0) row.BackColor = Red;
                else if (on && at < 0) row.BackColor = Color.FromArgb(255, 246, 214);
                else if (!on) row.ForeColor = SystemColors.GrayText;
                lv.Items.Add(row);
            }
            lv.EndUpdate();
            rows = lv.Items.Count;
            Select(keep);
        }

        if (pathBox.Text != ModSwitch.GameDir) pathBox.Text = ModSwitch.GameDir;
        head.Text = Loc.F("game.is", ModSwitch.GameDir) + "      " + Loc.F("head.hint");
        SyncChecks();
        SyncLoaderButton();
        FitStrips();                // the loader button just changed its text, which can change the rows
        status.Items[0].Text = Loc.F("status.counts", rows, CountOn(), ModSwitch.HomeDir + "\\modswitch-backup");
    }

    /// The on/off state, drawn as text in column 0 instead of the native checkbox column: that band is
    /// exactly where a Ctrl / Shift selection click lands, and the control replays check-state changes
    /// there, which must not be mistaken for a request to switch a mod.
    static string Glyph(bool on) { return (on ? "\u2611 " : "\u2610 "); }

    static readonly Color Red = Color.FromArgb(255, 228, 228);      // a row whose package will not load

    /// How many mods are on - read straight from the files, not from any widget state.
    static int CountOn()
    {
        int n = 0;
        foreach (ListViewItem r in lv.Items) if (ModSwitch.Enabled((string)r.Tag)) n++;
        return n;
    }

    /// Highlight one mod.  A drag has to be re-asserted after the drop: the native control changes the
    /// selection again once our handler returns, which wipes out a synchronous set.
    static void Select(string name)
    {
        if (name == null) return;
        foreach (ListViewItem r in lv.Items)
            if ((string)r.Tag == name)
            {
                r.Selected = true;
                r.Focused = true;
                r.EnsureVisible();            // a reorder on row 18 must not scroll you away from it
                return;
            }
    }

    // ------------------------------------------------------------------ game folder
    static void ApplyPath()
    {
        string err = ModSwitch.UseGame(pathBox.Text);
        if (err != null)
        {
            Append("!! " + err);
            MessageBox.Show(err + Loc.F("dlg.badHint"), Loc.F("title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Reload();
        RunAudit();
    }

    static void PickGame()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = Loc.F("btn.pick");
            d.ShowNewFolderButton = false;
            try { if (Directory.Exists(ModSwitch.GameDir)) d.SelectedPath = ModSwitch.GameDir; }
            catch (Exception) { }
            if (d.ShowDialog() != DialogResult.OK) return;
            pathBox.Text = d.SelectedPath;
            ApplyPath();
        }
    }

    // ------------------------------------------------------------------ actions
    /// The only path that turns a mod on or off from the table.  It refreshes cells in place on
    /// purpose: rebuilding with Items.Clear() would throw away a multi-selection every time you toggle
    /// one mod, which is exactly what a table of toggles must not do.
    static void Toggle(string name, bool want)
    {
        Append("--- " + Loc.F(want ? "w.on" : "w.off", name));
        ModSwitch.SetEnabled(name, want);
        Renumber();
    }

    // The state is a glyph in column 0, not the native ListView checkbox column.  A checkbox there sits
    // exactly where a Ctrl / Shift selection click lands, and the control replays ItemChecked with
    // False/True pairs whenever the selection churns - so a selection click could be read as "switch
    // this mod", and the reload behind it would drop the selection.  Nothing toggles by itself now:
    // only an explicit press on this glyph, or Space on the caret row, reaches Toggle().
    //
    // A press is not a drag either.  Calling DoDragDrop from MouseDown runs the OLE loop for every
    // click, which both flashes the insertion line on a plain click and swallows the button message the
    // list-view needs for Ctrl / Shift selection.  So a press is only remembered here and MouseMove
    // starts the drag once the pointer leaves the system drag box - what a native control does.
    static ListViewItem pressRow;         // the row the left button went down on
    static Point pressAt;                 // where it went down, in the list's client coordinates
    static Keys pressMods;                // and the modifiers it went down with - see OnMouseMove

    /// What a left press at (x, y) with these modifiers means.  Split out of OnMouseDown so the rules can
    /// be asserted without sending any real input.
    internal static void RowDown(int x, int y, Keys mods)
    {
        pressRow = null;
        pressMods = mods;
        ListViewItem it = lv.GetItemAt(x, y);
        if (it == null || it.Tag == null) return;
        if (x <= CheckZone)
        {
            // Ctrl / Shift on the glyph is still a selection click, not an order to switch the mod off
            if ((mods & (Keys.Control | Keys.Shift | Keys.Alt)) == Keys.None)
                Toggle((string)it.Tag, !ModSwitch.Enabled((string)it.Tag));
            return;                                     // and a drag never starts from the glyph
        }
        pressRow = it;
        pressAt = new Point(x, y);
    }

    static void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) RowDown(e.X, e.Y, Control.ModifierKeys);
    }

    static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (pressRow == null || e.Button != MouseButtons.Left) return;
        Size d = SystemInformation.DragSize;
        if (new Rectangle(pressAt.X - d.Width / 2, pressAt.Y - d.Height / 2, d.Width, d.Height).Contains(e.Location))
            return;                                       // still inside the click zone: nothing has happened yet
        ListViewItem row = pressRow;
        pressRow = null;                                  // one drag per press, and MouseUp can clear it too
        // The modifiers of the PRESS decide, never the ones still held now: Shift is released long
        // before the pointer drifts four pixels, and reading the live keyboard state there turns
        // range-selects into drags.  Ctrl / Shift / Alt belong to the native selection, and a drag moves
        // exactly one row, so one started from inside a multi-selection is refused rather than taken.
        if ((pressMods & (Keys.Control | Keys.Shift | Keys.Alt)) != Keys.None) return;
        if (row.Selected && lv.SelectedIndices.Count > 1) return;
        string name = (string)row.Tag;
        lv.Focus();
        lv.DoDragDrop(row, DragDropEffects.Move);
        ShowMark(-1);
        HoldSelection(name);                              // the drag loop answers back; keep re-asserting
    }

    static void OnMouseUp(object sender, MouseEventArgs e) { pressRow = null; }

    static void OnDragEnter(object sender, DragEventArgs e)
    {
        if (ArchiveUnder(e) != null) { e.Effect = DragDropEffects.Copy; return; }
        e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
    }

    static void OnDragOver(object sender, DragEventArgs e)
    {
        string zip = ArchiveUnder(e);
        if (zip != null)                                  // dropping a file means "install this mod"
        {
            e.Effect = DragDropEffects.Copy;
            ShowMark(-1);
            status.Items[0].Text = Loc.F("in.drop", Path.GetFileName(zip));
            return;
        }
        Point on = lv.PointToClient(new Point(e.X, e.Y));
        // Over the log or the buttons there is no slot to preview: keep the pointer a legal Move target
        // but hide the line, otherwise it sits on the last row promising a move OnDragDrop refuses.
        if (on.Y < 0 || on.Y > lv.ClientSize.Height) { e.Effect = DragDropEffects.Move; ShowMark(-1); return; }
        int at = SlotAt(on.Y);
        e.Effect = DragDropEffects.Move;
        ShowMark(at);
        status.Items[0].Text = Loc.F("status.dropTo", Math.Min(at + 1, rows));
    }

    /// The archive being dragged over us, if any.  Only the extension is looked at here because this runs
    /// on every mouse-move of a drag; the real decision is the header check inside Install().
    static readonly string[] PackedExt = { ".zip", ".7z", ".rar" };

    static string ArchiveUnder(DragEventArgs e)
    {
        foreach (string f in DropFiles(e))
            if (Array.IndexOf(PackedExt, Path.GetExtension(f).ToLowerInvariant()) >= 0) return f;
        return null;
    }

    static string[] DropFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new string[0];
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        return files ?? new string[0];
    }

    static void OnDragLeave(object sender, EventArgs e) { ShowMark(-1); }

    // ---------- where the row will land
    // .NET Framework's ListView exposes no InsertionMarker, and sending comctl32's LVM_SETINSERTMARK
    // does set the state but draws nothing: the control's own OLE drop target owns the painting during
    // a drag.  So the line is a real 2px child control we move around - deterministic, and it survives
    // whatever the native target does with the mouse.
    internal static Panel markLine;

    static void BuildMarkLine()
    {
        markLine = new Panel();
        markLine.Height = 3;
        markLine.BackColor = SystemColors.Highlight;
        markLine.Visible = false;
        markLine.BringToFront();
        lv.Controls.Add(markLine);
    }

    /// Put the line at the boundary before row `at` (or hide it for -1).
    static void ShowMark(int at)
    {
        if (markLine == null || !lv.IsHandleCreated) return;
        if (at < 0 || lv.Items.Count == 0) { markLine.Visible = false; return; }
        int y;
        if (at >= lv.Items.Count) y = lv.GetItemRect(lv.Items.Count - 1).Bottom - 1;
        else y = lv.GetItemRect(at).Top - 1;
        if (at == 0) y = Math.Max(0, y);
        markLine.Left = 0;
        markLine.Width = lv.ClientSize.Width;
        markLine.Top = y;
        markLine.Visible = true;
        markLine.BringToFront();
    }

    internal static void OnDragDrop(object sender, DragEventArgs e)
    {
        ShowMark(-1);
        string zip = ArchiveUnder(e);
        if (zip != null) { InstallArchives((string[])e.Data.GetData(DataFormats.FileDrop)); return; }
        ListViewItem dragged = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
        if (dragged == null || dragged.Tag == null) return;
        Point on = lv.PointToClient(new Point(e.X, e.Y));
        // Releasing a row outside the table is not a reorder: let it go back where it was.
        if (on.Y < 0 || on.Y > lv.ClientSize.Height) return;
        int from = lv.Items.IndexOf(dragged);
        int to = SlotAt(on.Y);
        if (to < 0 || from < 0) return;
        if (to > from) to--;                            // the row itself moves out of the way first
        ApplyOrder((string)dragged.Tag, to);
    }

    // ---------- keeping the highlight after a drag
    // The native control changes the selection some time after our DragDrop handler returns, so the
    // re-assert has to be repeated a few times.  Only Select() runs here - never a rebuild - so there
    // is nothing to flicker (rebuilding the whole table here is what makes a reorder feel laggy).
    static Timer keepTimer;
    static string keepName;
    static int keepLeft;

    static void BuildKeepTimer()
    {
        keepTimer = new Timer();
        keepTimer.Interval = 40;
        keepTimer.Tick += delegate
        {
            if (--keepLeft <= 0) keepTimer.Stop();
            Select(keepName);
        };
    }

    static void HoldSelection(string name)
    {
        keepName = name;
        keepLeft = 5;                                   // ~200 ms of re-asserting, 40 ms apart
        Select(name);
        keepTimer.Start();
    }

    /// Move a mod to slot `to` on disk and reflect it in the table **without rebuilding it**: the same
    /// ListViewItem object is relocated, so the highlight rides with it.  Drag, the up/down buttons and
    /// Alt+arrows all come through here.  Rebuilding the rows loses the selection, and a deferred full
    /// reload is what makes a drag feel laggy - so the row object is moved and only the affected cells
    /// are rewritten.
    static void ApplyOrder(string name, int to)
    {
        int from = IndexOf(name);
        if (from < 0) return;
        ModSwitch.MoveTo(name, to);
        int target = Math.Max(0, Math.Min(lv.Items.Count - 1, to));
        if (target != from)
        {
            ListViewItem row = lv.Items[from];
            lv.BeginUpdate();
            lv.Items.RemoveAt(from);
            lv.Items.Insert(target, row);
            lv.EndUpdate();
        }
        Renumber();
        HoldSelection(name);
    }

    static int IndexOf(string name)
    {
        for (int i = 0; i < lv.Items.Count; i++) if ((string)lv.Items[i].Tag == name) return i;
        return -1;
    }

    static void MoveSelected(int delta)
    {
        if (lv.SelectedIndices.Count == 0) { Append("!! " + Loc.F("w.pickRow")); return; }
        // with a multi-selection the caret row is the one the arrow keys belong to
        ListViewItem row = lv.FocusedItem != null && lv.FocusedItem.Selected ? lv.FocusedItem : lv.SelectedItems[0];
        ApplyOrder((string)row.Tag, lv.Items.IndexOf(row) + delta);
    }

    /// Refresh the state glyph, the row number and the priority column in place (no Clear, so a
    /// multi-selection survives an enable/disable).
    static void Renumber()
    {
        List<string> prio = ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority") ?? new List<string>();
        for (int i = 0; i < lv.Items.Count; i++)
        {
            ListViewItem r = lv.Items[i];
            string name = (string)r.Tag;
            bool on = ModSwitch.Enabled(name);
            int at = prio.IndexOf(name);
            r.SubItems[0].Text = Glyph(on) + (i + 1);
            r.SubItems[4].Text = at < 0 ? (on ? Loc.F("slot.notIn") : Loc.F("slot.off")) : (at + 1).ToString();
            if (r.BackColor != Red) r.BackColor = on && at < 0 ? Color.FromArgb(255, 246, 214) : Color.Empty;
            r.ForeColor = on ? SystemColors.WindowText : SystemColors.GrayText;
        }
        status.Items[0].Text = Loc.F("status.counts", lv.Items.Count, CountOn(), ModSwitch.HomeDir + "\\modswitch-backup");
    }

    // which insertion slot the pointer is over, from the y the drag events report
    internal static int SlotAt(int y)
    {
        for (int i = 0; i < lv.Items.Count; i++)
        {
            Rectangle r = lv.GetItemRect(i);
            if (r.Height == 0) continue;               // scrolled out of view
            if (y < r.Top + r.Height / 2) return i;
        }
        return lv.Items.Count;
    }

    static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && lv.SelectedIndices.Count > 0)
        {
            MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
        if (lv.FocusedItem == null || lv.FocusedItem.Tag == null) return;
        string name = (string)lv.FocusedItem.Tag;
        Toggle(name, !ModSwitch.Enabled(name));
        e.Handled = true;
        e.SuppressKeyPress = true;            // Space would otherwise scroll the list
    }

    static void RunAudit()
    {
        Append("=== " + Loc.F("btn.audit") + " ===");
        ModSwitch.Audit();
        Reload();
    }

    /// The loader button's text says what pressing it will do, so it is read from the game folder itself,
    /// not from which button happens to be on the left.
    static void SyncLoaderButton()
    {
        Button b = btns["btn.loader"];
        b.Tag = ModSwitch.LoaderPresent ? "btn.remove" : "btn.install";
        b.Text = Loc.F((string)b.Tag);
    }

    static void InstallLoader()
    {
        ModSwitch.InstallLoader();
        Reload();
    }

    static void RemoveLoader()
    {
        if (MessageBox.Show(Loc.F("dlg.removeAsk", "dinput8.dll"), Loc.F("btn.remove"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        ModSwitch.RemoveLoader();
        Reload();
    }

    // ------------------------------------------------------------------ install / delete a mod
    static void PickArchive()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = Loc.F("in.pick");
            d.Filter = "mod 压缩包|" + "*.zip;*.7z;*.rar";     // built at runtime so no literal pipe is parsed
            d.Multiselect = true;
            if (d.ShowDialog() != DialogResult.OK) return;
            InstallArchives(d.FileNames);
        }
    }

    /// Install every mod package found in the given archives.  A package is "the folder inside the zip
    /// that holds config.toml"; that folder's contents become mods/<name>/, so a zip that wraps
    /// everything in one top-level directory does not carry that wrapper into mods/.
    static void InstallArchives(string[] files)
    {
        bool any = false;
        foreach (string file in files)
        {
            if (!File.Exists(file)) continue;
            if (Install(file) != null) any = true;      // Install() decides the container from its header
        }
        if (any) { ModSwitch.RefreshOrder(); Reload(); RunAudit(); }
    }

    /// Returns the mod folder that was written, or null when nothing was installed.
    static string Install(string path)
    {
        string kind = KindOf(path);
        if (kind == null) { Append("!! " + Path.GetFileName(path) + " - " + Loc.F("in.onlyZip")); return null; }
        if (kind == "zip") return InstallZip(path);

        // 7z and rar are decoded by the bsdtar that ships with Windows itself (libarchive: LZMA /
        // LZMA2, RAR3, RAR5; extraction was checked byte-for-byte against an already-installed mod).
        // That keeps this tool a single exe with nothing to install.  The price: libarchive cannot
        // decrypt, so an encrypted archive is refused and its own message is shown rather than guessed
        // at.
        // Scratch goes next to the tool, on the same drive as mods\ - the game folder is never touched
        // and a big pack does not have to fit on the system drive.
        string scratch = Path.Combine(ModSwitch.HomeDir, "modswitch-unpack",
                                      DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-"
                                      + Process.GetCurrentProcess().Id);
        try
        {
            string err = Unpack(path, scratch);
            if (err != null)
            {
                Append("!! " + Loc.F("in.bad", Path.GetFileName(path) + ": " + err));
                return null;
            }
            return InstallFromTree(scratch, Path.GetFileNameWithoutExtension(path));
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, true);
                string box = Path.GetDirectoryName(scratch);            // modswitch-unpack\
                if (Directory.GetDirectories(box).Length == 0 && Directory.GetFiles(box).Length == 0)
                    Directory.Delete(box);                              // leave nothing behind when it is empty
            }
            catch (Exception) { }
        }
    }

    /// The container is decided by its own first bytes, not by the extension: renaming a pack is common.
    internal static string KindOf(string path)
    {
        try
        {
            byte[] h = new byte[8];
            using (Stream s = File.OpenRead(path))
                if (s.Read(h, 0, 8) < 8) return null;
            if (h[0] == (byte)'P' && h[1] == (byte)'K' && h[2] == 3 && h[3] == 4) return "zip";
            if (h[0] == (byte)'7' && h[1] == (byte)'z' && h[2] == 0xBC && h[3] == 0xAF
                && h[4] == 0x27 && h[5] == 0x1C) return "7z";
            if (h[0] == (byte)'R' && h[1] == (byte)'a' && h[2] == (byte)'r' && h[3] == (byte)'!'
                && h[4] == 0x1A && h[5] == 0x07 && (h[6] == 0x00 || (h[6] == 0x01 && h[7] == 0x00))) return "rar";
        }
        catch (Exception) { }
        return null;
    }

    static string TarExe()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                @"System32\tar.exe");
        return File.Exists(p) ? p : null;
    }

    /// Extracts with tar.exe and returns null on success, otherwise the reason to show the user.
    static string Unpack(string archive, string dest)
    {
        string tar = TarExe();
        if (tar == null) return Loc.F("in.needTar");
        Directory.CreateDirectory(dest);
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(tar,
                "-xf \"" + archive + "\" -C \"" + dest + "\"");
            si.UseShellExecute = false;
            si.CreateNoWindow = true;
            si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string err = p.StandardError.ReadToEnd();       // small; reading it before exiting is what
                p.WaitForExit();                               // keeps a chatty tar from deadlocking us
                if (p.ExitCode != 0)
                    return (err + " rc=" + p.ExitCode).Trim();
            }
        }
        catch (Exception ex) { return ex.Message; }
        return null;
    }

    /// Same rule as the zip path: a package is the folder that holds config.toml, and the shallowest such
    /// folder wins, so the wrapper directory an upload site put around it is not carried into mods\.
    static string InstallFromTree(string root, string fallbackName)
    {
        var cfgs = new List<string>();
        foreach (string f in Directory.GetFiles(root, "config.toml", SearchOption.AllDirectories)) cfgs.Add(f);
        if (cfgs.Count == 0) { Append("!! " + Loc.F("in.noCfg") + "  (" + fallbackName + ")"); return null; }
        int shallowest = int.MaxValue;
        foreach (string f in cfgs) shallowest = Math.Min(shallowest, Depth(f.Substring(root.Length).TrimStart('\\', '/')));
        string last = null;
        foreach (string f in cfgs)
        {
            string rel = f.Substring(root.Length).TrimStart('\\', '/');
            if (Depth(rel) != shallowest) continue;
            last = InstallTreePackage(Path.GetDirectoryName(f), f, fallbackName,
                                      rel.IndexOf('/') < 0 ? "." : rel.Substring(0, rel.LastIndexOf('/'))) ?? last;
        }
        return last;
    }

    /// Nothing may land outside the mod folder it was meant for, whichever kind of archive it came from.
    static bool Inside(string dest, string target)
    {
        return dest.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar,
                               StringComparison.OrdinalIgnoreCase);
    }

    static string InstallTreePackage(string src, string cfgPath, string fallbackName, string label)
    {
        string name = ModSwitch.ModName(File.ReadAllText(cfgPath, Encoding.UTF8), fallbackName);
        if (name == "") name = fallbackName;
        string target = Path.Combine(ModSwitch.ModsDir, name);

        Append("--- " + Loc.F("in.ask", Path.GetFileName(src) == "" ? name : src, target, name));
        if (Directory.Exists(target))
            if (MessageBox.Show(Loc.F("in.exists", target), Loc.F("btn.addMod"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            { Append("    skipped"); return null; }

        string trash = Directory.Exists(target) ? ToTrash(target) : null;
        int written = 0;
        foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(src.Length).TrimStart('\\', '/');
            string dest = Path.GetFullPath(Path.Combine(target, rel));
            if (!Inside(dest, target)) { Append(Loc.F("in.slip", rel)); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(f, dest, true);
            written++;
        }
        Append("    " + Loc.F("in.done", name, written, label)
               + (trash == null ? "" : "   [old copy -> " + trash + "]"));
        return target;
    }

    /// zip body: the archive's own entries are matched against the package level.
    static string InstallZip(string zipPath)
    {
        try
        {
            using (ZipArchive a = ZipFile.OpenRead(zipPath))
            {
                var cfgs = new List<ZipArchiveEntry>();
                foreach (ZipArchiveEntry en in a.Entries)
                    if (en.Name.Equals("config.toml", StringComparison.OrdinalIgnoreCase)) cfgs.Add(en);
                if (cfgs.Count == 0) { Append("!! " + Loc.F("in.noCfg") + "  (" + Path.GetFileName(zipPath) + ")"); return null; }

                int shallowest = int.MaxValue;
                foreach (ZipArchiveEntry en in cfgs) shallowest = Math.Min(shallowest, Depth(en.FullName));
                string last = null;
                foreach (ZipArchiveEntry en in cfgs)
                {
                    if (Depth(en.FullName) != shallowest) continue;         // deeper configs belong to that package
                    last = InstallPackage(a, zipPath, en, Path.GetFileNameWithoutExtension(zipPath)) ?? last;
                }
                return last;
            }
        }
        catch (Exception ex) { Append(Loc.F("in.bad", ex.Message)); return null; }
    }

    static string InstallPackage(ZipArchive a, string zipPath, ZipArchiveEntry cfgEntry, string fallbackName)
    {
        string prefix = DirPrefix(cfgEntry.FullName);                      // "" or "SomeFolder/"
        string text = ReadEntry(cfgEntry);
        string name = ModSwitch.ModName(text, fallbackName);
        if (name == "") name = fallbackName;
        string target = Path.Combine(ModSwitch.ModsDir, name);

        Append("--- " + Loc.F("in.ask", Path.GetFileName(zipPath), target, name));
        if (Directory.Exists(target))
            if (MessageBox.Show(Loc.F("in.exists", target), Loc.F("btn.addMod"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            { Append("    skipped"); return null; }

        string trash = null;
        if (Directory.Exists(target)) trash = ToTrash(target);

        int written = 0;
        foreach (ZipArchiveEntry en in a.Entries)
        {
            // .NET hands entry names back with the platform separator ("dist\rom\x.bin"), not the "/" the
            // archive stores, so compare and cut on the normalized form or a wrapped package matches nothing.
            string full = en.FullName.Replace('\\', '/');
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = full.Substring(prefix.Length);
            if (rel == "") continue;
            string dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dest, target)) { Append(Loc.F("in.slip", full)); continue; }
            if (full.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            en.ExtractToFile(dest, true);
            written++;
        }
        Append("    " + Loc.F("in.done", name, written, prefix == "" ? "." : prefix)
               + (trash == null ? "" : "   [old copy -> " + trash + "]"));
        return target;
    }

    static int Depth(string entryFullName)
    {
        string d = DirPrefix(entryFullName);
        return d == "" ? 0 : d.TrimEnd('/').Split('/').Length;
    }

    static string DirPrefix(string entryFullName)
    {
        string s = entryFullName.Replace('\\', '/');
        int i = s.LastIndexOf('/');
        return i < 0 ? "" : s.Substring(0, i + 1);
    }

    static string ReadEntry(ZipArchiveEntry en)
    {
        using (Stream s = en.Open())
        using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8, true))
            return r.ReadToEnd();
    }

    /// Move a folder aside instead of erasing it.  Same drive, so this is a rename: instant and undoable.
    static string ToTrash(string folder)
    {
        string baseDir = Path.Combine(ModSwitch.HomeDir, "modswitch-deleted");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dest = Path.Combine(baseDir, stamp, Path.GetFileName(folder));
        int n = 1;
        while (Directory.Exists(dest)) dest = Path.Combine(baseDir, stamp, Path.GetFileName(folder) + "." + (++n));
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        Directory.Move(folder, dest);
        return dest;
    }

    static void DeleteSelected()
    {
        // row order, not SelectedItems' order: the log and the confirm list should read like the table
        var names = new List<string>();
        foreach (ListViewItem r in lv.Items) if (r.Selected && r.Tag != null) names.Add((string)r.Tag);
        if (names.Count == 0) { Append("!! " + Loc.F("del.pick")); return; }
        string trashRoot = Path.Combine(ModSwitch.HomeDir, "modswitch-deleted");
        string ask = names.Count == 1 ? Loc.F("del.ask", names[0], trashRoot)
                                      : Loc.F("del.askMany", names.Count, string.Join(", ", names.ToArray()), trashRoot);
        if (MessageBox.Show(ask, Loc.F("btn.delMod"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (Process.GetProcessesByName("DivaMegaMix").Length > 0) Append(Loc.F("del.running"));
        int moved = 0;
        foreach (string name in names)
        {
            string folder = Path.Combine(ModSwitch.ModsDir, name);
            try
            {
                if (!Directory.Exists(folder)) { Append("!! " + folder); continue; }
                Append(Loc.F("del.done", name, ToTrash(folder)));
                moved++;
            }
            catch (Exception ex) { Append(Loc.F("in.bad", name + ": " + ex.Message)); }   // a locked folder must not stop the rest
        }
        if (moved > 0) { ModSwitch.RefreshOrder(); Reload(); }
    }

    static void Help()
    {
        MessageBox.Show(Loc.F("help.body"), Loc.F("btn.help"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void Append(string s)
    {
        if (log == null || log.IsDisposed) { Console.WriteLine(s); return; }
        if (log.InvokeRequired) { log.BeginInvoke(new Action<string>(Append), s); return; }
        log.AppendText(s + "\r\n");
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }
}
