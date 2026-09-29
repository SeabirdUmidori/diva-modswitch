// modswitch-test - the headless acceptance run.  Not compiled into the shipped exe: build.bat does
// not list it.  It drives the REAL install / delete / toggle / layout code against a fake game folder
// built under _selftest\ next to the test exe, so its Home (order sidecar, backups, trash) can never
// touch a real install, and prints ok/BAD per assertion.  Exit code 0 means everything passed.
//   build:build.bat test      (or the csc line inside it)
//   run:   dist\modswitch-test.exe
// A real 7z/rar is only checked when MODSWITCH_SAMPLE points at one, so the run stays reproducible
// without shipping an archive.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class FeatureTest
{
    static string ROOT = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_selftest");
    static string T = Path.Combine(ROOT, "g");            // the fake game folder
    static string Z = Path.Combine(ROOT, "z");            // the archives to install from
    static string MODS = Path.Combine(T, "mods");
    static int fails = 0;

    [STAThread]
    static int Main()
    {
        // set before anything reads it: this exe's own folder is Home, so the sidecar, the backups
        // and the trash all land under _selftest instead of next to a real install.
        Environment.SetEnvironmentVariable("MODSWITCH_GAME", T);
        BuildFixtures();
        ModSwitch.Sink = delegate(string s) { Console.WriteLine("   | " + s); };
        Loc.Detect();
        Loc.Choose("zh");        // the assertions below read Chinese button captions; pin the language
        // ---- 0. all three language rows of a key must use the SAME {n} placeholders: a row that needs
        //         more of them than the call site passes is a FormatException waiting to happen.
        int mism = 0;
        foreach (string key in Loc.Keys)
        {
            string seen = null;
            for (int i = 0; i < 3; i++)
            {
                var set = new SortedSet<string>();
                foreach (Match m in Regex.Matches(Loc.At(key, i), @"\{\d+\}")) set.Add(m.Value);
                string[] arr = new string[set.Count];
                set.CopyTo(arr, 0);
                string joined = string.Join(",", arr);
                if (seen != null && seen != joined)
                { Console.WriteLine("  BAD  占位符不一致 " + key + ": [" + seen + "] vs [" + joined + "]"); mism++; }
                seen = joined;
            }
        }
        Check("三语占位符集合一致", mism == 0);
        if (!ModSwitch.FindGame()) return 9;
        Form f = ModSwitchGui.Build();
        ModSwitchGui.ApplyLanguage();
        f.Opacity = 0; f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000);
        f.Show(); Application.DoEvents();
        ModSwitchGui.Reload(); Application.DoEvents();

        // ---- 1. a zip that wraps everything in one folder: the wrapper must be stripped
        Probe(Path.Combine(Z, "wrapped.zip"));
        Call("InstallArchives", new object[] { new string[] { Path.Combine(Z, "wrapped.zip") } });
        Dump();
        string a = Path.Combine(MODS, "Wrapped Mod");
        Check("wrapper 被去掉，config 落在 mods/Wrapped Mod/", File.Exists(Path.Combine(a, "config.toml")));
        Check("rom 内容跟着进来", File.Exists(Path.Combine(a, "rom", "objset", "mod_obj_db.bin")));
        Check("没有多带一层目录", !Directory.Exists(Path.Combine(a, "dist")));

        // ---- 2. a zip with config.toml at its root
        Call("InstallArchives", new object[] { new string[] { Path.Combine(Z, "flat.zip") } });
        string b = Path.Combine(MODS, "Flat Mod");
        Check("根层包也装上了", File.Exists(Path.Combine(b, "config.toml")) && File.Exists(Path.Combine(b, "rom", "x.bin")));

        // ---- 3. zip-slip
        Call("InstallArchives", new object[] { new string[] { Path.Combine(Z, "slip.zip") } });
        string escape = Path.GetFullPath(Path.Combine(MODS, "..", "PWNED.txt"));
        Check("越界条目没被写出去 (" + escape + ")", !File.Exists(escape));
        Check("越界那条被跳过而不是整包失败", File.Exists(Path.Combine(MODS, "Slip Mod", "config.toml")));

        // ---- 4. not a zip / no config.toml: nothing installs, nothing throws
        Call("InstallArchives", new object[] { new string[] { Path.Combine(Z, "notes.txt"), Path.Combine(Z, "nocfg.zip") } });
        Check("非 zip / 无 config 的没有生成目录", !Directory.Exists(Path.Combine(MODS, "notes"))
               && !Directory.Exists(Path.Combine(MODS, "nocfg")));

        // ---- 5. the new mods are installed AND loaded (priority + enabled agree)
        List<string> prio = ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority");
        Check("新装的 mod 进了 priority", prio.Contains("Wrapped Mod") && prio.Contains("Flat Mod"));
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("列表行数 = 4", ModSwitchGui.lv.Items.Count == 4);

        // ---- 6. console checkbox reflects the file, and ticking it writes
        CheckBox box = (CheckBox)typeof(ModSwitchGui).GetProperty("consoleBox", BindingFlags.NonPublic | BindingFlags.Static)
                        .GetValue(null, null);
        Console.WriteLine("勾选框文字: " + box.Text + "  初始 Checked=" + box.Checked);
        Check("初始未勾（根配置 console=false）", !box.Checked);
        box.Checked = true;                                   // what the user's click does
        Application.DoEvents();
        Check("勾上 -> 文件写 console = true", Read(Path.Combine(T, "config.toml")).Contains("console = true"));
        Check("重读后仍是勾着", box.Checked);
        box.Checked = false;
        Application.DoEvents();
        Check("取消 -> 文件写 console = false", Read(Path.Combine(T, "config.toml")).Contains("console = false"));
        Check("其它行没被牵连", Read(Path.Combine(T, "config.toml")).Contains("mods = \"mods\""));

        // ---- 7. delete moves the folder aside instead of erasing it
        foreach (ListViewItem r in ModSwitchGui.lv.Items)
            if ((string)r.Tag == "Slip Mod") { r.Selected = true; r.Focused = true; }
        AnswerDialog();                                       // the confirm box is modal: click "Yes" for it
        Call("DeleteSelected", null);
        Application.DoEvents();
        Check("mods 里没了", !Directory.Exists(Path.Combine(MODS, "Slip Mod")));
        string trashRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modswitch-deleted");
        Check("回收目录里有原件", Directory.Exists(trashRoot) && Find(trashRoot, "config.toml") != null);
        prio = ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority");
        Check("priority 里也移除了", !prio.Contains("Slip Mod") && prio.Count == 3);
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("列表剩 3 行", ModSwitchGui.lv.Items.Count == 3);
        Check("删除没有动其它 mod 的文件", File.Exists(Path.Combine(MODS, "Existing", "config.toml"))
               && Read(Path.Combine(MODS, "Existing", "config.toml")).Contains("include = [\".\"]"));

        // ---- 8. Ctrl multi-selection deletes several mods with one confirmation
        ModSwitchGui.Reload(); Application.DoEvents();
        foreach (ListViewItem r in ModSwitchGui.lv.Items)
        {
            string n = (string)r.Tag;
            if (n == "Wrapped Mod" || n == "Flat Mod") { r.Selected = true; r.Focused = n == "Flat Mod"; }
        }
        Check("多选真的成立（ListView 开了 MultiSelect）", ModSwitchGui.lv.SelectedIndices.Count == 2);
        AnswerDialog();
        Call("DeleteSelected", null);
        Application.DoEvents();
        Check("两个都从 mods 里没了", !Directory.Exists(Path.Combine(MODS, "Wrapped Mod"))
               && !Directory.Exists(Path.Combine(MODS, "Flat Mod")));
        Check("两个原件都在回收目录", TrashHas("Wrapped Mod") && TrashHas("Flat Mod"));
        prio = ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority");
        Check("priority 只剩 1 项", prio.Count == 1 && prio[0] == "Existing");
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("列表剩 1 行", ModSwitchGui.lv.Items.Count == 1);

        // ---- 9. the single loader button always says what pressing it will do
        var btns = (Dictionary<string, Button>)typeof(ModSwitchGui)
                    .GetField("btns", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Button loaderBtn = btns["btn.loader"];
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("未安装 -> 按钮写「安装」", loaderBtn.Text.Contains("安装") && !loaderBtn.Text.Contains("移除"));
        File.WriteAllText(Path.Combine(T, "dinput8.dll"), "stub");
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("已安装 -> 同一个按钮改写「移除」", loaderBtn.Text.Contains("移除") && !loaderBtn.Text.Contains("安装"));
        File.Delete(Path.Combine(T, "dinput8.dll"));
        ModSwitchGui.Reload(); Application.DoEvents();
        Check("组件没了 -> 又回到「安装」", loaderBtn.Text.Contains("安装"));

        // ---- 10. a press without movement must stay a click: no reorder, no insertion line, no drag loop
        MethodInfo md = typeof(ModSwitchGui).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo mm = typeof(ModSwitchGui).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo mu = typeof(ModSwitchGui).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo pr = typeof(ModSwitchGui).GetField("pressRow", BindingFlags.NonPublic | BindingFlags.Static);
        string before = ModSwitch.Raw(ModSwitch.RootCfg);
        int selBefore = ModSwitchGui.lv.SelectedIndices.Count;
        Rectangle b0 = ModSwitchGui.lv.Items[0].Bounds;
        Point mid = new Point(b0.Left + 60, b0.Top + b0.Height / 2);
        md.Invoke(null, new object[] { ModSwitchGui.lv, new MouseEventArgs(MouseButtons.Left, 1, mid.X, mid.Y, 0) });
        Application.DoEvents();
        Check("按下只记住行，没有改选择", (ListViewItem)pr.GetValue(null) != null);
        Check("按下不改选择数", ModSwitchGui.lv.SelectedIndices.Count == selBefore);
        // one pixel away is still inside the system drag box: the drag loop must NOT start
        mm.Invoke(null, new object[] { ModSwitchGui.lv, new MouseEventArgs(MouseButtons.Left, 1, mid.X + 1, mid.Y + 1, 0) });
        Application.DoEvents();
        Check("微动不算拖：插入预览线没露出来", !ModSwitchGui.markLine.Visible);
        mu.Invoke(null, new object[] { ModSwitchGui.lv, new MouseEventArgs(MouseButtons.Left, 1, mid.X + 1, mid.Y + 1, 0) });
        Application.DoEvents();
        Check("抬手后不残留按下状态", pr.GetValue(null) == null);
        Check("只点不拖：根配置一字节没变", ModSwitch.Raw(ModSwitch.RootCfg) == before);

        // ---- 11. the state glyph is the only thing a click can flip; Ctrl / Shift flip nothing; and
        //          a toggle must never rebuild the rows, so a multi-selection survives it
        Call("InstallArchives", new object[] { new string[] { Path.Combine(Z, "flat.zip") } });
        ModSwitchGui.Reload(); Application.DoEvents();
        string cfgE = Path.Combine(MODS, "Existing", "config.toml");
        byte[] original = File.ReadAllBytes(cfgE);
        int y0 = ModSwitchGui.lv.GetItemRect(0).Top + ModSwitchGui.lv.GetItemRect(0).Height / 2;
        ModSwitchGui.lv.Items[0].Selected = true;
        ModSwitchGui.lv.Items[1].Selected = true;
        Check("两行已选中", ModSwitchGui.lv.SelectedIndices.Count == 2);
        ModSwitchGui.RowDown(10, y0, Keys.Control);           // Ctrl on the glyph: must only be a selection click
        Application.DoEvents();
        Check("Ctrl 点 glyph 不翻状态（磁盘一字节没动）", Equal(Bytes(cfgE), original));
        ModSwitchGui.RowDown(10, y0, Keys.None);               // plain click on the glyph: the only way to flip
        Application.DoEvents();
        Check("点 glyph 才关：enabled = false 落盘", Read(cfgE).Contains("enabled = false"));
        Check("关完行首是 ☐", ModSwitchGui.lv.Items[0].SubItems[0].Text.StartsWith("\u2610"));
        Check("开关没有清掉多选", ModSwitchGui.lv.SelectedIndices.Count == 2);
        ModSwitchGui.RowDown(10, y0, Keys.None);
        Application.DoEvents();
        Check("再点开回来，逐字节还原", Equal(Bytes(cfgE), original));
        Check("开回来行首是 ☑", ModSwitchGui.lv.Items[0].SubItems[0].Text.StartsWith("\u2611"));
        ModSwitchGui.RowDown(120, y0, Keys.None);              // on the row text: a drag candidate, never a flip
        Application.DoEvents();
        Check("点行文字不翻状态，只记成待拖动", Equal(Bytes(cfgE), original) && (ListViewItem)pr.GetValue(null) != null);
        mu.Invoke(null, new object[] { ModSwitchGui.lv, new MouseEventArgs(MouseButtons.Left, 1, 120, y0, 0) });

        // ---- 12. nothing may be hidden when the window is small.  Judge each button against the
        //          STRIP's own screen rectangle on purpose: the flow panel's client box says nothing
        //          about whether its parent grew to fit the wrapped rows, so measuring against that box
        //          alone passes even while the strip is visibly clipped.
        FieldInfo fiFlow = typeof(ModSwitchGui).GetField("buttonFlow", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo fiStrip = typeof(ModSwitchGui).GetField("buttons", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo fiTopBar = typeof(ModSwitchGui).GetField("topBar", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo fiHead = typeof(ModSwitchGui).GetField("head", BindingFlags.NonPublic | BindingFlags.Static);
        FlowLayoutPanel flow = (FlowLayoutPanel)fiFlow.GetValue(null);
        Panel strip = (Panel)fiStrip.GetValue(null);
        Label hint = (Label)fiHead.GetValue(null);
        int lineH = TextRenderer.MeasureText("国", hint.Font, new Size(400, 0), TextFormatFlags.NoPrefix).Height;
        int[] sizes = new int[] { 1600, 1040, 820 };
        foreach (int w in sizes)
        {
            // the last case is the floor itself: the smallest window the form will accept
            f.Size = w == 820 ? f.MinimumSize : new Size(w, 560);
            Application.DoEvents();
            Rectangle stripOn = strip.RectangleToScreen(strip.ClientRectangle);
            int outside = 0;
            foreach (Control c in flow.Controls)
            {
                Rectangle on = c.RectangleToScreen(c.ClientRectangle);
                if (on.Bottom > stripOn.Bottom || on.Right > stripOn.Right) outside++;
            }
            Check("宽 " + w + "：按钮没有一个跑到条外 (" + outside + ")", outside == 0);
            int need = flow.GetPreferredSize(new Size(flow.ClientSize.Width, 0)).Height;
            Check("宽 " + w + "：条高装得下按当前宽度折出来的行 (" + strip.ClientSize.Height + " 对 " + need + ")",
                  strip.ClientSize.Height >= need);
            Check("宽 " + w + "：提示条至少有 " + (w < 1000 ? "两" : "一") + " 行的高度 (" + hint.Height + ")",
                  hint.Height >= (w < 1000 ? 2 : 1) * lineH);
            Check("宽 " + w + "：表和日志窗都还在", ModSwitchGui.lv.Height >= 90 && ModSwitchGui.log.Height >= 70);
        }
        Check("窄窗时按钮条确实换成多行",
              flow.GetPreferredSize(new Size(flow.ClientSize.Width, 0)).Height > 40);
        f.Size = new Size(1600, 690);
        Application.DoEvents();
        Check("拉宽又收成一行 (" + strip.ClientSize.Height + "px)",
              flow.GetPreferredSize(new Size(flow.ClientSize.Width, 0)).Height < 50
              && strip.ClientSize.Height < 50);
        FieldInfo fiPath = typeof(ModSwitchGui).GetField("pathBox", BindingFlags.NonPublic | BindingFlags.Static);
        TextBox path = (TextBox)fiPath.GetValue(null);
        Check("路径框没被挤没 (" + path.Width + "px)", path.Width >= 200);

        // ---- 13. containers are chosen by their own header, and 7z/rar install through the tree path
        Check("zip 头", "zip".Equals(ModSwitchGui.KindOf(Magic("pk", new byte[] { 0x50, 0x4B, 0x03, 0x04 }))));
        Check("7z 头", "7z".Equals(ModSwitchGui.KindOf(Magic("s7", new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))));
        Check("rar4 头", "rar".Equals(ModSwitchGui.KindOf(Magic("r4", new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00, 0x00 }))));
        Check("rar5 头", "rar".Equals(ModSwitchGui.KindOf(Magic("r5", new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }))));
        Check("认不出的头返回 null", ModSwitchGui.KindOf(Magic("no", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })) == null);

        string tree = Path.Combine(Z, "tree");                       // an unpacked 7z/rar looks like this
        string packRoot = Path.Combine(tree, "Some Wrapper");
        Directory.CreateDirectory(Path.Combine(packRoot, "rom", "objset"));
        File.WriteAllText(Path.Combine(packRoot, "config.toml"), "enabled = true\r\nname = \"Tree Mod\"\r\ninclude = [\".\"]\r\n");
        File.WriteAllText(Path.Combine(packRoot, "rom", "objset", "mod_obj_db.bin"), "bytes");
        MethodInfo fromTree = typeof(ModSwitchGui).GetMethod("InstallFromTree", BindingFlags.NonPublic | BindingFlags.Static);
        string made = (string)fromTree.Invoke(null, new object[] { tree, "fallback" });
        Check("解出来的目录树也按\"含 config 的那层\"装，壳层被去掉",
              made == Path.Combine(MODS, "Tree Mod")
              && File.Exists(Path.Combine(MODS, "Tree Mod", "rom", "objset", "mod_obj_db.bin")));
        Check("目录树装完进了 priority", (ModSwitch.RefreshOrder() == 0
               && ModSwitch.ArrayValues(ModSwitch.Raw(ModSwitch.RootCfg), "priority").Contains("Tree Mod")));

        // ---- 14. the console entry point is the SAME engine, not a second writer: drive it in process
        MethodInfo cli = typeof(ModSwitch).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        string cfgCli = Path.Combine(MODS, "Existing", "config.toml");
        byte[] cliBase = Bytes(cfgCli);
        int rcOff = (int)cli.Invoke(null, new object[] { new string[] { "--game", T, "off", "Existing" } });
        Check("cli off 返回 0 并真的改了文件", rcOff == 0 && Read(cfgCli).Contains("enabled = false")
               && !Equal(Bytes(cfgCli), cliBase));
        string changedLine = "";
        string[] was = Encoding.UTF8.GetString(cliBase).Split('\n');
        string[] now = Encoding.UTF8.GetString(File.ReadAllBytes(cfgCli)).Split('\n');
        if (was.Length == now.Length)
            for (int i = 0; i < was.Length; i++)
                if (was[i].Trim() != now[i].Trim()) changedLine = was[i].Trim();
        Check("cli off 之后只有 enabled 那一行不同", was.Length == now.Length && changedLine.StartsWith("enabled"));
        int rcOn = (int)cli.Invoke(null, new object[] { new string[] { "--game", T, "on", "Existing" } });
        Check("cli on 逐字节还原", rcOn == 0 && Equal(Bytes(cfgCli), cliBase));
        int rcDry = (int)cli.Invoke(null, new object[] { new string[] { "--game", T, "--dry", "off", "Existing" } });
        Check("--dry 什么都不写", rcDry == 0 && Equal(Bytes(cfgCli), cliBase));

        // ---- 16. the loader compiled into this exe is the fallback, never an overwrite
        byte[] bundled = ModSwitch.BundledLoader();
        Check("这个 build 里真的内嵌了 dinput8.dll", bundled != null && bundled.Length > 100000);
        Console.WriteLine("   内嵌副本: " + (bundled == null ? "无" : ModSwitch.Fingerprint(bundled)));
        string liveDll = Path.Combine(T, "dinput8.dll");
        string payloadCopy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loader", "dinput8.dll");
        try { Directory.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loader"), true); } catch (Exception) { }
        File.Delete(liveDll);
        int rcLoad = ModSwitch.InstallLoader();
        Check("loader\\ 和 dll 都没有时：从内嵌副本装上", rcLoad == 0 && File.Exists(liveDll)
               && Equal(File.ReadAllBytes(liveDll), bundled));
        Check("同时把副本落到 loader\\ 里（下次就是它优先）", File.Exists(payloadCopy)
               && Equal(File.ReadAllBytes(payloadCopy), bundled));
        File.WriteAllBytes(liveDll, new byte[7]);             // pretend the user has a different loader
        try { Directory.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loader"), true); } catch (Exception) { }
        int rcKeep = ModSwitch.InstallLoader();
        Check("游戏目录已有 dll 就绝不覆盖", rcKeep == 0 && new FileInfo(liveDll).Length == 7);
        File.WriteAllBytes(liveDll, bundled);                  // restore for anything that follows

        // a real 7z/rar can only be checked against a real archive, so it runs when one is pointed at
        string sample = Environment.GetEnvironmentVariable("MODSWITCH_SAMPLE") ?? "";
        if (sample != "" && File.Exists(sample))
        {
            string kind = ModSwitchGui.KindOf(sample);
            ModSwitchGui.Reload(); Application.DoEvents();     // count the tree install too, or the delta lies
            int rowsBefore = ModSwitchGui.lv.Items.Count;
            Call("InstallArchives", new object[] { new string[] { sample } });
            ModSwitchGui.Reload(); Application.DoEvents();
            int after = ModSwitchGui.lv.Items.Count;
            Console.WriteLine("   sample " + Path.GetFileName(sample) + " 识别为 " + kind + "：" + rowsBefore + " 行 -> " + after + " 行");
            Check("真压缩包走完了解包与安装（或按规则拒绝）",
                  after > rowsBefore || ModSwitchGui.log.Text.Contains(Loc.F("in.noCfg")));
            Check("解包暂存目录用完就删了", !Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modswitch-unpack")));
        }
        else Console.WriteLine("   (no MODSWITCH_SAMPLE given - the real-archive step is skipped)");

        Dump();
        f.Close();
        Console.WriteLine(fails == 0 ? "PASS 安装/删除/控制台勾选框" : "FAIL " + fails);
        return fails == 0 ? 0 : 1;
    }

    /// the mod folder anywhere under modswitch-deleted\<stamp>\
    static bool TrashHas(string name)
    {
        string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modswitch-deleted");
        if (!Directory.Exists(baseDir)) return false;
        foreach (string d in Directory.GetDirectories(baseDir, "*", SearchOption.AllDirectories))
            if (Path.GetFileName(d) == name && File.Exists(Path.Combine(d, "config.toml"))) return true;
        return false;
    }

    // ---- answering the modal MessageBox the real code shows
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    const int BM_CLICK = 0x00F5, IDYES = 6, IDNO = 7;

    /// modswitch-gui asks for confirmation on the dangerous paths, so the test answers from a second
    /// thread: MessageBox.Show runs its own modal loop, which will dispatch this click.
    static void AnswerDialog()
    {
        System.Threading.Thread t = new System.Threading.Thread(delegate()
        {
            for (int i = 0; i < 200; i++)
            {
                System.Threading.Thread.Sleep(25);
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    if (!IsWindowVisible(h)) return true;
                    var cls = new System.Text.StringBuilder(64);
                    GetClassName(h, cls, cls.Capacity);
                    if (cls.ToString() != "#32770") return true;
                    EnumChildWindows(h, delegate(IntPtr c, IntPtr l2)
                    {
                        int id = GetDlgCtrlID(c);
                        if (id == IDYES || id == IDNO) PostMessage(c, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    static string Find(string root, string file)
    {
        foreach (string d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(d, file))) return d;
        return null;
    }
    static string Read(string p) { return File.ReadAllText(p); }
    /// a file that starts with these bytes - enough for the header sniffing to make a decision
    static string Magic(string name, byte[] head)
    {
        byte[] body = new byte[head.Length + 16];
        for (int i = 0; i < head.Length; i++) body[i] = head[i];
        string p = Path.Combine(Z, name + ".bin");
        File.WriteAllBytes(p, body);
        return p;
    }
    static byte[] Bytes(string p) { return File.ReadAllBytes(p); }
    static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    static void Dump() { Console.WriteLine("---- GUI log ----\r\n" + ModSwitchGui.log.Text + "---- end log ----"); }

    /// Print exactly what the installer sees, with the strings in brackets so a hidden character shows up.
    static void Probe(string zip)
    {
        MethodInfo dp = typeof(ModSwitchGui).GetMethod("DirPrefix", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo dep = typeof(ModSwitchGui).GetMethod("Depth", BindingFlags.NonPublic | BindingFlags.Static);
        using (ZipArchive za = ZipFile.OpenRead(zip))
            foreach (ZipArchiveEntry en in za.Entries)
                Console.WriteLine("entry [" + en.FullName + "] name [" + en.Name + "] prefix ["
                    + (string)dp.Invoke(null, new object[] { en.FullName }) + "] depth "
                    + dep.Invoke(null, new object[] { en.FullName }));
    }
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "  ok   " : "  BAD  ") + what); if (!ok) fails++; }
    static void Call(string m, object[] args)
    {
        typeof(ModSwitchGui).GetMethod(m, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        Application.DoEvents();
    }

    static void BuildFixtures()
    {
        if (Directory.Exists(T)) Directory.Delete(T, true);
        if (Directory.Exists(Z)) Directory.Delete(Z, true);
        Directory.CreateDirectory(T); Directory.CreateDirectory(Z);
        File.WriteAllText(Path.Combine(T, "DivaMegaMix.exe"), "stub");
        File.WriteAllText(Path.Combine(T, "config.toml"), "enabled = true\r\nconsole = false\r\nmods = \"mods\"\r\npriority = [\"Existing\"]\r\n");
        Directory.CreateDirectory(Path.Combine(MODS, "Existing", "rom"));
        File.WriteAllText(Path.Combine(MODS, "Existing", "config.toml"), "enabled = true\r\nname = \"Existing\"\r\ninclude = [\".\"]\r\n");

        // wrapped.zip  : dist/config.toml + dist/rom/objset/mod_obj_db.bin
        string d = Path.Combine(Z, "src", "dist");
        Directory.CreateDirectory(Path.Combine(d, "rom", "objset"));
        File.WriteAllText(Path.Combine(d, "config.toml"), "enabled = true\r\nname = \"Wrapped Mod\"\r\ninclude = [\".\"]\r\n");
        File.WriteAllText(Path.Combine(d, "rom", "objset", "mod_obj_db.bin"), "bytes");
        ZipFile.CreateFromDirectory(Path.Combine(Z, "src"), Path.Combine(Z, "wrapped.zip"));

        // flat.zip     : config.toml + rom/x.bin at the archive root
        string e = Path.Combine(Z, "flat");
        Directory.CreateDirectory(Path.Combine(e, "rom"));
        File.WriteAllText(Path.Combine(e, "config.toml"), "enabled = true\r\nname = \"Flat Mod\"\r\ninclude = [\".\"]\r\n");
        File.WriteAllText(Path.Combine(e, "rom", "x.bin"), "bytes");
        ZipFile.CreateFromDirectory(e, Path.Combine(Z, "flat.zip"));

        // slip.zip     : one good entry + one that tries to climb out of mods/
        string g = Path.Combine(Z, "slip");
        Directory.CreateDirectory(Path.Combine(g, "rom"));
        File.WriteAllText(Path.Combine(g, "config.toml"), "enabled = true\r\nname = \"Slip Mod\"\r\ninclude = [\".\"]\r\n");
        File.WriteAllText(Path.Combine(g, "rom", "ok.bin"), "bytes");
        using (ZipArchive za = ZipFile.Open(Path.Combine(Z, "slip.zip"), ZipArchiveMode.Create))
        {
            foreach (string fn in Directory.GetFiles(g, "*", SearchOption.AllDirectories))
                za.CreateEntryFromFile(fn, fn.Substring(g.Length + 1).Replace('\\', '/'));
            za.CreateEntry("../../PWNED.txt");                 // the attack
        }

        // nocfg.zip + a non-zip file
        string h = Path.Combine(Z, "nocfg");
        Directory.CreateDirectory(h); File.WriteAllText(Path.Combine(h, "readme.txt"), "hi");
        ZipFile.CreateFromDirectory(h, Path.Combine(Z, "nocfg.zip"));
        File.WriteAllText(Path.Combine(Z, "notes.txt"), "not an archive");
    }
}
