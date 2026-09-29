// modswitch - an enable/disable + reorder + install tool for Hatsune Miku Project DIVA Mega Mix+
// mods, and for the components the mod loader needs.  Single .exe, no dependencies beyond the
// .NET Framework that is already part of Windows 10/11: no installer, no Python, no Node.
//
// Why not DivaModManager: DMM 1.3.1.0's AddInclude() (UI/MainWindow.xaml.cs:524-533) runs
//   config["include"] = new string[1]{"."}
// unconditionally from both the enable and the disable path (MainWindow.xaml.cs:408-410, 476-478),
// even when the file parsed fine.  A pack that ships "one folder + one include directory per module"
// (a pack with 39 of them) therefore stops loading entirely the moment you click it, and the
// loader reports nothing: it builds each search root as "<mod folder>\<include entry>\rom" and simply
// drops the roots that do not exist.
//
// This tool only ever edits ONE line per file - `enabled` in a mod, `priority` in the loader config -
// and it proves that after writing by comparing every other line byte for byte.  Its own files
// (order list, backups, loader payload) live next to the exe, never in the game folder.
//
// build:  build.bat  (in-box csc, C# 5).  Two entry points share this engine on purpose:
//   -main:ModSwitchGui -> modswitch-gui.exe   the window
//   -main:ModSwitch    -> modswitch-cli.exe   the same commands for scripts and diagnostics
// Everything below that writes a file goes through ReplaceLine/SetEnabled/WriteOrder, so the
// window and the command line cannot disagree about what a toggle means.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Win32;

static class ModSwitch
{
    const string AppId = "1761390";                       // Hatsune Miku Project DIVA Mega Mix+
    const string LoaderDll = "dinput8.dll";               // DivaModLoader
    const string GameFolder = "Hatsune Miku Project DIVA Mega Mix Plus";   // Steam's installdir name
    static readonly Encoding L1 = Encoding.GetEncoding(28591);   // 1 byte per char, so edits stay byte exact
    static readonly byte[] NL = new byte[] { (byte)'\r', (byte)'\n' };

    static string Home = AppDir();
    static string OrderFile = Path.Combine(Home, "modswitch-order.json");
    static string GameFile = Path.Combine(Home, "modswitch-game.txt");   // the folder you picked, remembered
    static string BackupRoot = Path.Combine(Home, "modswitch-backup");
    static string PayloadDir = Path.Combine(Home, "loader");
    static string PayloadDll = Path.Combine(PayloadDir, LoaderDll);

    static string game = "", mods = "", root = "";
    static bool dryRun = false;
    // Installing/removing the loader components and flipping the debug console are deliberately NOT
    // undoable: they are restored by "安装加载组件" itself (the payload copy sits next to the exe), and
    // backing them up would only bury the mod-config backups that DO need an undo.
    [ThreadStatic] static bool noBackup;
    static void WithoutBackup(Action f) { noBackup = true; try { f(); } finally { noBackup = false; } }
    // Every message goes through this: the CLI writes it to the console, the GUI appends it to its log pane.
    internal static Action<string> Sink = Console.WriteLine;
    internal static string GameDir { get { return game; } }
    internal static string ModsDir { get { return mods; } }
    internal static string RootCfg { get { return root; } }
    internal static string HomeDir { get { return Home; } }

    /// Point at a game folder and remember it.  Returns null when it took, otherwise the reason it did not.
    internal static string UseGame(string path)
    {
        string full;
        try { full = Path.GetFullPath((path ?? "").Trim().Trim('"')); }
        catch (Exception) { return Loc.F("game.badPath", path); }
        if (full.EndsWith("\\")) full = full.TrimEnd('\\');
        if (!File.Exists(Path.Combine(full, "DivaMegaMix.exe")))
            return Loc.F("game.badExe", full);
        SetGame(full);
        if (!Directory.Exists(mods))
        {
            try { Directory.CreateDirectory(mods); Print(Loc.F("game.modsMade", mods)); }
            catch (Exception e) { return Loc.F("game.badMods", e.Message); }
        }
        try { File.WriteAllText(GameFile, full + "\n", new UTF8Encoding(false)); }
        catch (Exception e) { Print(Loc.F("game.noRemember", e.Message)); }
        Print(Loc.F("game.set", full));
        Print(Loc.F("game.modsIs", mods));
        Print(Loc.F("game.cfgIs", root) + (File.Exists(root) ? "" : "   " + Loc.F("game.cfgMissing", Loc.F("btn.install"))));
        return null;
    }

    static void SetGame(string full)
    {
        game = full;
        root = Path.Combine(game, "config.toml");
        mods = Path.Combine(game, TomlValue(Raw(root), "mods") ?? "mods");
        if (!Directory.Exists(mods)) mods = Path.Combine(game, "mods");
    }

    /// Take this path as the game folder if it really is one (DivaMegaMix.exe; mods/ gets created later).
    static bool Try(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception) { return false; }
        if (!File.Exists(Path.Combine(full, "DivaMegaMix.exe"))) return false;
        SetGame(full.TrimEnd('\\'));
        return true;
    }

    // Candidates are produced lazily: a remembered folder must short-circuit every probe after it.
    internal static bool FindGame()
    {
        if (game == "") game = Environment.GetEnvironmentVariable("MODSWITCH_GAME") ?? "";
        if (game != "" && Try(game)) return true;                                  // --game / MODSWITCH_GAME
        if (File.Exists(GameFile) && Try(Raw(GameFile).Trim()))
        {
            Print(Loc.F("game.usingSaved", game));
            return true;
        }
        var found = new List<string>();
        foreach (string c in PollDrives())
        {
            if (!Try(c)) continue;
            found.Add(game);
            game = "";                                                             // keep looking, then decide
        }
        game = "";
        foreach (string c in SteamLibraryGuesses())
        {
            if (!Try(c)) continue;
            found.Add(game);
            game = "";
        }
        game = "";
        if (found.Count == 0) return false;
        // the same install can be listed twice (a drive probe and the Steam registry agree on it)
        var uniq = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Try(uniq[0]);
        if (uniq.Count > 1)
        {
            Print(Loc.F("game.many"));
            foreach (var u in uniq) Print("  - " + u);
            Print(Loc.F("game.manyHint", Loc.F("btn.pick")));
        }
        else Print(Loc.F("game.auto", game));
        return true;
    }

    /// Every fixed drive's usual Steam install location for this game, most common layout first.
    static IEnumerable<string> PollDrives()
    {
        var outList = new List<string>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception) { return outList; }
        foreach (var d in drives)
        {
            string rootDir = null;
            try { if (!d.IsReady || d.DriveType == DriveType.Ram || d.DriveType == DriveType.CDRom) continue; rootDir = d.RootDirectory.FullName; }
            catch (Exception) { continue; }
            outList.Add(Path.Combine(rootDir, @"Program Files (x86)\Steam\steamapps\common\" + GameFolder));
            outList.Add(Path.Combine(rootDir, @"Program Files\Steam\steamapps\common\" + GameFolder));
            outList.Add(Path.Combine(rootDir, @"SteamLibrary\steamapps\common\" + GameFolder));
        }
        return outList;
    }

    /// Whatever the Steam registry and its libraryfolders.vdf claim - covers custom library folders.
    static IEnumerable<string> SteamLibraryGuesses()
    {
        var outList = new List<string>();
        foreach (var lib in SteamLibraries())
        {
            string dir = InstallDirOf(lib);
            if (dir == "" || dir.Contains("\0")) continue;
            outList.Add(Path.Combine(lib, "common", dir));
        }
        return outList;
    }

    static int Main(string[] args)
    {
        // `--dry` is a property of ONE command, not of the process: the writers read dryRun from
        // anywhere, so it has to be cleared on the way out or a later call in the same process would
        // silently refuse to write while reporting success.
        try { return Cli(args); }
        finally { dryRun = false; }
    }

    static int Cli(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch (Exception) { }   // author names are UTF-8
        Loc.Detect();                                     // same language rules as the GUI
        var rest = new List<string>(args);
        for (int i = 0; i < rest.Count; i++)
        {
            if (rest[i] == "--game" && i + 1 < rest.Count) { game = rest[i + 1]; rest.RemoveAt(i); rest.RemoveAt(i); i--; }
            else if (rest[i] == "--dry") { dryRun = true; rest.RemoveAt(i); }
        }
        if (rest.Count > 0 && rest[0].ToLower() == "game" && rest.Count > 1)
            return UseGame(string.Join(" ", rest.Skip(1).ToArray())) == null ? 0 : 1;
        if (!FindGame()) return Fail("no game folder found - 选一个:  modswitch game \"<游戏根目录>\"");
        if (!File.Exists(root)) Print("note: " + root + " is missing - 'install-loader' will create it");

        string cmd = rest.Count == 0 ? "menu" : rest[0].ToLower();
        var a = rest.Skip(1).ToList();
        if (cmd == "menu") return Menu();
        if (cmd == "ls" || cmd == "list") { List(); return 0; }
        if (cmd == "audit") return Audit();
        if (cmd == "undo") return Undo();
        if (cmd == "install-loader") return InstallLoader();
        if (cmd == "remove-loader") return RemoveLoader();
        if (cmd == "game")
        {
            Print("current : " + game);
            Print("saved   : " + (File.Exists(GameFile) ? Raw(GameFile).Trim() : "(none, autodetected)"));
            Print("用法    : modswitch game \"<游戏根目录>\"");
            return 0;
        }
        if (cmd == "console" && a.Count == 1) return ConsoleSwitch(a[0]);
        if (cmd == "up" || cmd == "down")
        {
            if (a.Count == 0) return Fail("usage: modswitch " + cmd + " <mod>");
            return Move(Resolve(string.Join(" ", a)), cmd == "up" ? -1 : 1);
        }
        if (cmd == "move")        // absolute slot, the same call the GUI's drag uses
        {
            if (a.Count < 2) return Fail("usage: modswitch move <mod> <第几行, 1 = 最高优先级>");
            int slot;
            if (!int.TryParse(a[a.Count - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                return Fail("最后一个参数要是行号: move <mod> <n>");
            string name = Resolve(string.Join(" ", a.Take(a.Count - 1).ToArray()));
            return name == null ? 2 : MoveTo(name, slot - 1);
        }
        if (cmd == "on" || cmd == "off" || cmd == "toggle")
        {
            if (a.Count == 0) return Fail("usage: modswitch " + cmd + " <mod>");
            string name = Resolve(string.Join(" ", a));
            if (name == null) return 2;
            bool want = cmd == "on" || !(Enabled(name));
            return SetEnabled(name, want);
        }
        if (cmd == "help" || cmd == "?") { Usage(); return 0; }
        return Fail("unknown command: " + cmd);
    }

    internal static void Usage()
    {
        Print("modswitch  -  enable/disable DIVA mods, install/remove the loader components");
        Print("  (nothing)              interactive menu");
        Print("  ls                     every mod: enabled, in load order, what the loader will do with it");
        Print("  on|off|toggle <mod>    flip one mod (folder name or a unique prefix of it)");
        Print("  up|down <mod>          move it in the load order (first entry = highest priority)");
        Print("  move <mod> <n>         put it at absolute slot n (what the GUI does when you drag a row)");
        Print("  audit                  missing loader components, include/dll entries that resolve to nothing");
        Print("  install-loader         put " + LoaderDll + " + a root config.toml with a priority array in the game folder");
        Print("  remove-loader          back up and remove exactly those two components");
        Print("  console on|off         the loader's debug console (it prints the ROM paths it registers)");
        Print("  undo                   restore the last backup set (configs + components)");
        Print("  game                   show the detected game folder        --game \"<path>\" override it");
        Print("  files it writes for itself are next to the exe: " + Home);
    }

    // ------------------------------------------------------------------ locating the game
    static string AppDir()
    {
        return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
    }

    // every steamapps folder of every Steam library on this machine
    static IEnumerable<string> SteamLibraries()
    {
        var libs = new List<string>();
        try
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam") ??
                       RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                           .OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                string steam = k == null ? null : k.GetValue("InstallPath") as string;
                if (steam != null) libs.Add(Path.Combine(steam, "steamapps"));
            }
        }
        catch (Exception) { /* no registry access: the other candidates still stand */ }
        foreach (string lib in libs.ToArray())
        {
            try
            {
                string vdf = Path.Combine(lib, "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(Raw(vdf), "\"path\"\\s*\\\"([^\\\"]+)\\\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (!libs.Contains(p)) libs.Add(p);
                }
            }
            catch (Exception) { }
        }
        return libs.Where(Directory.Exists).Distinct().ToList();
    }

    // the app's "installdir" inside that library folder
    static string InstallDirOf(string lib)
    {
        try
        {
            foreach (var f in Directory.GetFiles(lib, "appmanifest_" + AppId + ".acf"))
            {
                Match m = Regex.Match(Raw(f), "\"installdir\"\\s*\\\"([^\\\"]+)\\\"");
                if (m.Success) return m.Groups[1].Value.Replace("\\\\", "\\");
            }
        }
        catch (Exception) { }
        return "";
    }

    // ------------------------------------------------------------------ tiny TOML helpers (bytes in, bytes out)
    internal static string Raw(string path)
    {
        if (path == null || !File.Exists(path)) return "";
        return L1.GetString(File.ReadAllBytes(path));   // 1 char per byte: edits stay byte exact
    }

    // a top-level `key = value` scalar, for display only (we never re-serialise a file from this)
    static string TomlValue(string text, string key)
    {
        Match m = Regex.Match(text, @"^" + key + @"\s*=\s*(?:""([^""]*)""|(\S+))",
                              RegexOptions.Multiline);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    }

    internal static List<string> ArrayValues(string text, string key)
    {
        Match m = Regex.Match(text, @"^" + key + @"\s*=\s*\[(.*?)\]", RegexOptions.Multiline | RegexOptions.Singleline);
        var outList = new List<string>();
        if (!m.Success) return null;
        foreach (Match s in Regex.Matches(m.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            outList.Add(s.Groups[1].Value);
        return outList;
    }

    static int FirstTableHeader(string text)
    {
        Match m = Regex.Match(text, @"^\s*\[", RegexOptions.Multiline);
        return m.Success ? m.Index : int.MaxValue;
    }

    // Replace exactly one line; verify afterwards that no other line moved, or roll the file back.
    static bool ReplaceLine(string path, Regex rx, string replacement, string what)
    {
        string before = Raw(path);
        Match m = rx.Match(before);
        if (!m.Success) { Print("!! " + path + ": found no " + what); return false; }
        string after = before.Substring(0, m.Index) + replacement + before.Substring(m.Index + m.Length);
        string[] lb = before.Split('\n'), la = after.Split('\n');
        int diffs = 0;
        if (lb.Length == la.Length)
            for (int i = 0; i < lb.Length; i++) if (lb[i] != la[i]) diffs++;
        if (lb.Length != la.Length || diffs > 1)
        {
            Print(Loc.F("w.crlfBad", Path.GetFileName(path), lb.Length, la.Length));
            return false;
        }
        if (diffs == 0) { Print("   " + Loc.F("w.same2", what)); return true; }
        if (dryRun) { Print("dry-run: would write " + what + " in " + path); return true; }
        var bak = Backup(path);
        File.WriteAllBytes(path, L1.GetBytes(after));
        Print("   " + what + " -> " + replacement.TrimEnd('\r') + "   [" + path.Substring(game.Length).TrimStart('\\') + "]");
        Print("   " + Loc.F("w.backup", bak));
        return true;
    }

    // ------------------------------------------------------------------ mod state
    internal static List<string> Folders()
    {
        var l = new List<string>();
        if (!Directory.Exists(mods)) return l;             // a fresh install has no mods/ yet
        foreach (var d in Directory.GetDirectories(mods))
        {
            string n = Path.GetFileName(d);
            if (!n.StartsWith(".")) l.Add(n);
        }
        l.Sort(StringComparer.OrdinalIgnoreCase);
        return l;
    }

    internal static string Cfg(string name) { return Path.Combine(mods, name, "config.toml"); }

    internal static bool Enabled(string name)
    {
        string t = Raw(Cfg(name));
        if (t == "") return false;                       // no config.toml -> the loader skips the folder
        Match m = TopEnabled(t);
        return m.Success ? m.Groups[2].Value == "true" : true;   // absent means enabled
    }

    static Match TopEnabled(string text)
    {
        int table = FirstTableHeader(text);
        foreach (Match m in Regex.Matches(text, @"^(enabled\s*=\s*)(true|false)", RegexOptions.Multiline))
            if (m.Index < table) return m;
        return Regex.Match("\r\n", "(x)");               // never matches: "no top-level enabled line"
    }

    internal static string Label(string name)
    {
        string t = Raw(Cfg(name));
        if (t == "") return "(no config.toml)";
        string n = Utf(TomlValue(t, "name")), a = Utf(TomlValue(t, "author"));
        if (n == null && a == null) return "";
        return (n ?? "") + (a == null ? "" : "  /  " + a);
    }

    // Files are handled as Latin1 so that edits stay byte exact; display should still show the UTF-8
    // author names the packs write.
    static string Utf(string latin1View)
    {
        if (latin1View == null) return null;
        try { return new UTF8Encoding(false, true).GetString(L1.GetBytes(latin1View)); }
        catch (Exception) { return latin1View; }
    }

    // ------------------------------------------------------------------ commands
    internal static int List()
    {
        var prio = ArrayValues(Raw(root), "priority") ?? new List<string>();
        Print("");
        Print(String.Format("{0,-3} {1,-34} {2,-4} {3,-4} {4}", "#", "mod", "on", "order", "name / author"));
        int i = 0;
        foreach (var name in Order())
        {
            i++;
            bool on = Enabled(name);
            int at = prio.IndexOf(name);
            Print(String.Format("{0,-3} {1,-34} {2,-4} {3,-4} {4}", i, Cut(name, 34),
                                on ? "ON" : "off", at < 0 ? "-" : (at + 1).ToString(), Cut(Label(name), 40)));
        }
        Print("");
        Print("on = the mod's own enabled key.  order = position in the loader's priority array, - = not listed.");
        Print(prio.Count == 0
            ? "The root config has NO priority array: the loader falls back to alphabetical folder order."
            : "The first entry in the priority array wins where two mods ship the same record.");
        return 0;
    }

    static string Cut(string s, int n)
    {
        s = s ?? "";
        return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
    }

    internal static int Audit()
    {
        int findings = 0;
        Print(Loc.F("audit.head", game));
        // 1. the loader itself
        if (!File.Exists(Path.Combine(game, LoaderDll)))
        { Print("  !! " + Loc.F("audit.noDll", LoaderDll)); findings++; }
        if (!File.Exists(root))
        { Print("  !! " + Loc.F("audit.noCfg", root)); findings++; }
        else
        {
            var prio = ArrayValues(Raw(root), "priority");
            if (prio == null)
            {
                Print("  !! " + Loc.F("audit.noPrio"));
                findings++;
            }
            else
            {
                foreach (var ghost in prio.Where(x => !Directory.Exists(Path.Combine(mods, x))))
                { Print("  !! " + Loc.F("audit.ghost", ghost)); findings++; }
                foreach (var missing in Order().Where(n => Enabled(n) && !prio.Contains(n)))
                { Print("  !! " + Loc.F("audit.notIn", missing)); findings++; }
            }
        }
        // 2. every mod: does what it declares exist, and does it register anything?
        var notes = new List<string>();
        foreach (var name in Folders())
        {
            string dir = Path.Combine(mods, name), t = Raw(Cfg(name));
            foreach (var f in IncludeProblems(dir, t, notes)) { Print("  !! " + name + ": " + f); findings++; }
            var dlls = ArrayValues(t, "dll");
            if (dlls != null)
                foreach (var d in dlls.Where(d => !File.Exists(Path.Combine(dir, d))))
                { Print("  !! " + Loc.F("audit.noDll2", name, d)); findings++; }
            if (t == "") { Print("  !! " + Loc.F("audit.noCfg2", name)); findings++; }
            else if (TomlValue(t, "name") == null) { Print("  ?  " + Loc.F("audit.noName", name)); findings++; }
        }
        foreach (var note in notes) Print("  ?  " + note);
        Print(findings == 0
            ? "  " + Loc.F("audit.clean") + (notes.Count > 0 ? "   " + Loc.F("audit.notes", notes.Count) : "")
            : "\n" + Loc.F("audit.found", findings));
        return findings == 0 ? 0 : 1;
    }

    // what the loader will actually do with this folder, per ModLoader.cpp + Utilities.h.
    // Returns findings that mean "something will not load" in `bad`, and notes worth knowing in `info`.
    internal static List<string> IncludeProblems(string dir, string config, List<string> info)
    {
        var bad = new List<string>();
        var subs = Directory.GetDirectories(dir).Where(x => Directory.Exists(Path.Combine(x, "rom")))
                        .Select(Path.GetFileName).ToList();
        bool topRom = Directory.Exists(Path.Combine(dir, "rom"));
        if (!topRom && subs.Count == 0) return bad;         // a code-only mod (dll, no rom/) has nothing to register
        if (config == "")
        {
            if (subs.Count > 0) bad.Add(Loc.F("inc.noCfgSubs", subs.Count));
            return bad;
        }
        var inc = ArrayValues(config, "include");
        if (inc == null) { if (topRom || subs.Count > 0) bad.Add(Loc.F("inc.noArray")); return bad; }
        var live = inc.Where(x => Directory.Exists(Path.GetFullPath(Path.Combine(dir, x, "rom")))).ToList();
        var dead = inc.Except(live).ToList();
        var unreg = subs.Except(inc).ToList();
        if (live.Count == 0)
            bad.Add(Loc.F("inc.nothing", "[" + string.Join(", ", inc.ToArray()) + "]"));
        else if (unreg.Count > 0 && !topRom)
            bad.Add(Loc.F("inc.unreg", unreg.Count, string.Join(", ", unreg.Take(3).ToArray())));
        else if (unreg.Count > 0)
            info.Add(Path.GetFileName(dir) + ": " + Loc.F("inc.optional", unreg.Count, string.Join(", ", unreg.Take(3).ToArray())));
        if (dead.Count > 0)
            bad.Add(Loc.F("inc.dead", dead.Count, string.Join(", ", dead.Take(3).ToArray())));
        return bad;
    }

    internal static int SetEnabled(string name, bool want)
    {
        if (name == null) return 2;
        string path = Cfg(name);
        if (!File.Exists(path))
            return Fail(Loc.F("w.noCfg", name));
        if (Enabled(name) == want) { Print(Loc.F("w.already", name, want ? Loc.F("w.on2") : Loc.F("w.off2"))); return 0; }
        string t = Raw(path);
        Match m = TopEnabled(t);
        bool ok;
        if (m.Success)
            ok = ReplaceLine(path, new Regex(@"(?m)^enabled\s*=\s*(?:true|false)(?-m)"),
                             "enabled = " + (want ? "true" : "false"), "enabled");
        else
            ok = InsertTopLine(path, "enabled = " + (want ? "true" : "false"));
        if (!ok) return 1;
        return WriteOrder(Order());
    }

    // a mod without an `enabled` line: add one as the first line, leaving every other byte untouched
    static bool InsertTopLine(string path, string line)
    {
        if (dryRun) { Print("dry-run: would prepend " + line + " to " + path); return true; }
        var bak = Backup(path);
        var data = new List<byte>();
        data.AddRange(L1.GetBytes(line));
        data.AddRange(NL);
        data.AddRange(File.ReadAllBytes(path));
        File.WriteAllBytes(path, data.ToArray());
        Print("   " + line + " added as the first line   [" + path.Substring(game.Length).TrimStart('\\') + "]");
        Print("   " + Loc.F("w.backup", bak));
        return true;
    }

    internal static int Move(string name, int delta)
    {
        if (name == null) return 2;
        var seq = Order();
        int i = seq.IndexOf(name);
        if (i < 0) return Fail(Loc.F("w.noMod", name));
        int j = i + delta;
        if (j < 0) return Fail(Loc.F("w.first", name));
        if (j >= seq.Count) return Fail(Loc.F("w.last", name));
        seq.RemoveAt(i);
        seq.Insert(j, name);
        Print(Loc.F("w.slot", name, j + 1, seq.Count));
        return WriteOrder(seq);
    }

    /// Move a mod to an absolute slot (drag and drop); the list is 0-based and gets clamped.
    internal static int MoveTo(string name, int target)
    {
        if (name == null) return 2;
        var seq = Order();
        int i = seq.IndexOf(name);
        if (i < 0) return Fail(Loc.F("w.noMod", name));
        int j = Math.Max(0, Math.Min(seq.Count - 1, target));
        if (i == j) { Print(Loc.F("w.same", name, i + 1)); return 0; }
        seq.RemoveAt(i);
        seq.Insert(j, name);
        Print(Loc.F("w.slot", name, j + 1, seq.Count));
        return WriteOrder(seq);
    }

    /// Re-derive the loader's priority array from the current order + enabled states.
    /// Needed after a mod is installed or deleted, so the new folder really loads.
    internal static int RefreshOrder() { return WriteOrder(Order()); }

    /// A mod folder name from a config.toml's `name`, sanitised for the filesystem.
    internal static string ModName(string configText, string fallback)
    {
        string n = TomlValue(configText, "name");
        if (string.IsNullOrEmpty(n)) n = fallback;
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c.ToString(), "");
        return n.Trim().TrimEnd('.');
    }

    /// A top-level scalar from a config.toml body (used on text read out of an archive).
    internal static string ConfigValue(string text, string key) { return TomlValue(text, key); }

    /// A top-level scalar from the loader's own config, for display ("console", "mods", ...).
    internal static string RootValue(string key) { return TomlValue(Raw(root), key); }

    /// Is the loader component itself in the game folder?  The one button reads its label off this:
    /// absent -> "install", present -> "remove".
    internal static bool LoaderPresent { get { return game != "" && File.Exists(Path.Combine(game, LoaderDll)); } }

    internal static int ConsoleSwitch(string want)
    {
        int rc = 0;
        WithoutBackup(delegate { rc = ConsoleSwitchCore(want); });
        return rc;
    }

    static int ConsoleSwitchCore(string want)
    {
        if (want != "on" && want != "off") return Fail("usage: modswitch console on|off");
        if (!File.Exists(root)) return Fail(Loc.F("comp.noCfg", root));
        string line = "console = " + (want == "on" ? "true" : "false");
        bool had = Regex.IsMatch(Raw(root), @"(?m)^console\s*=");
        bool ok = had ? ReplaceLine(root, new Regex(@"(?m)^console\s*=\s*(?:true|false)(?-m)"), line, "console")
                      : InsertEndLine(root, line);        // a root config without the key at all
        if (ok) Print(Loc.F("comp.console", want == "on" ? Loc.F("comp.conOn") : Loc.F("comp.conOff")));
        return ok ? 0 : 1;
    }

    // DivaModLoader's dinput8.dll, compiled in by build.bat (/resource:payload\dinput8.dll).  It is
    // somebody else's MIT-licensed work; THIRD_PARTY.md records exactly which bytes we ship.
    const string LoaderResource = "modswitch.dinput8.dll";

    /// The loader copy inside this exe, or null when this build was compiled without one.
    internal static byte[] BundledLoader()
    {
        try
        {
            using (Stream s = typeof(ModSwitch).Assembly.GetManifestResourceStream(LoaderResource))
            {
                if (s == null) return null;
                byte[] all = new byte[s.Length];
                int got = 0;
                while (got < all.Length)
                {
                    int n = s.Read(all, got, all.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                return got == all.Length ? all : null;
            }
        }
        catch (Exception) { return null; }    // a build without the resource stays supported
    }

    /// How to check the bytes we ship against the upstream release.
    internal static string Fingerprint(byte[] data)
    {
        using (MD5 md5 = MD5.Create())
            return BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "").ToLowerInvariant()
                   + " / " + data.Length + " B";
    }

    internal static int InstallLoader()
    {
        int rc = 0;
        WithoutBackup(delegate { rc = InstallLoaderCore(); });
        return rc;
    }

    static int InstallLoaderCore()
    {
        Print(Loc.F("comp.install", game));
        if (!Directory.Exists(PayloadDir)) Directory.CreateDirectory(PayloadDir);
        string live = Path.Combine(game, LoaderDll);
        if (!File.Exists(PayloadDll) && File.Exists(live))
        {
            File.Copy(live, PayloadDll, true);
            Print("   " + Loc.F("comp.keptCopy", LoaderDll, PayloadDll));
        }
        // Only when neither of those offers anything does the copy compiled into this exe step in, so a
        // fresh install needs no download while anyone who dropped their own (newer) dinput8.dll into
        // loader\ still decides what gets written.  Writing it out to loader\ rather than straight
        // into the game keeps every size comparison below working exactly as before.
        if (!File.Exists(PayloadDll) && !dryRun)
        {
            byte[] bundled = BundledLoader();
            if (bundled != null)
            {
                Directory.CreateDirectory(PayloadDir);
                File.WriteAllBytes(PayloadDll, bundled);
                Print("   " + Loc.F("comp.bundled", LoaderDll, PayloadDll, Fingerprint(bundled)));
            }
        }
        if (!File.Exists(PayloadDll))
            return Fail(Loc.F("comp.noPayload", LoaderDll, PayloadDir));
        if (!dryRun && !File.Exists(live))
        {
            File.Copy(PayloadDll, live, false);
            Print("   " + Loc.F("comp.placed", LoaderDll, new FileInfo(live).Length));
        }
        else if (File.Exists(live) && new FileInfo(live).Length == new FileInfo(PayloadDll).Length)
            Print("   " + Loc.F("comp.sameSize", LoaderDll));
        else
            Print("   " + Loc.F("comp.diffSize", LoaderDll));
        // the root config: keep whatever it says, only make sure a priority array exists and is complete
        if (!File.Exists(root))
        {
            if (dryRun) { Print("dry-run: would create " + root); return 0; }
            var sb = new StringBuilder();
            sb.Append("enabled = true\r\nconsole = false\r\nmods = \"mods\"\r\n");
            sb.Append("priority = " + Priority(Order()) + "\r\n");
            File.WriteAllBytes(root, L1.GetBytes(sb.ToString()));
            Print("   " + Loc.F("comp.created", root));
            return 0;
        }
        // a first run has no priority array to edit: append one, and remember the slots it implies
        if (ArrayValues(Raw(root), "priority") == null)
            return InsertEndLine(root, "priority = " + Priority(Order())) ? WriteOrder(Order()) : 1;
        return WriteOrder(Order());   // idempotent when the array already matches

    }

    static bool InsertEndLine(string path, string line)
    {
        if (dryRun) { Print("dry-run: would append " + line); return true; }
        var bak = Backup(path);
        var data = new List<byte>(File.ReadAllBytes(path));
        while (data.Count > 0 && (data[data.Count - 1] == (byte)'\r' || data[data.Count - 1] == (byte)'\n'))
            data.RemoveAt(data.Count - 1);
        data.AddRange(NL);
        data.AddRange(L1.GetBytes(line));
        data.AddRange(NL);
        File.WriteAllBytes(path, data.ToArray());
        Print("   " + Loc.F("comp.appended", line));
        Print("   " + Loc.F("w.backup", bak));
        return true;
    }

    internal static int RemoveLoader()
    {
        int rc = 0;
        WithoutBackup(delegate { rc = RemoveLoaderCore(); });
        return rc;
    }

    static int RemoveLoaderCore()
    {
        Print(Loc.F("comp.remove", game));
        int done = 0;
        foreach (var p in new[] { Path.Combine(game, LoaderDll), root })
        {
            if (!File.Exists(p)) { Print("   " + Loc.F("comp.absent", p)); continue; }
            if (dryRun) { Print("dry-run: would remove " + p); done++; continue; }
            string bak = Backup(p);
            File.Delete(p);
            Print("   " + Loc.F("comp.removed", p));
            done++;
        }
        return done == 0 ? Fail(Loc.F("comp.nothing")) : 0;
    }

    // ------------------------------------------------------------------ order & backups
    static int WriteOrder(List<string> seq)
    {
        if (!File.Exists(root)) return Fail(Loc.F("w.noOrder", root));
        string want = "priority = " + Priority(seq);
        if (ArrayValues(Raw(root), "priority") != null)
        {
            if (!ReplaceLine(root, new Regex(@"(?m)^priority\s*=\s*\[.*?\](?-m)"), want, "priority")) return 1;
        }
        else if (!InsertEndLine(root, want)) return 1;
        if (!dryRun)
        {
            if (File.Exists(OrderFile)) Backup(OrderFile);      // undo has to be able to put the slots back
            var sb = new StringBuilder();
            sb.Append("{\n  \"game\": \"" + game.Replace("\\", "\\\\") + "\",\n  \"note\": \"mod slots in load order, "
                      + "first = highest priority. modswitch's own list, not read by the loader.\",\n  \"order\": [\n");
            sb.Append(string.Join(",\n", seq.Select(x => "    \"" + x + "\"").ToArray()));
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(OrderFile, sb.ToString(), new UTF8Encoding(false));
        }
        return 0;
    }

    // Backups mirror the game folder's structure under modswitch-backup/<stamp>/, so a whole set can be
    // put back where it came from.  One set per run, the first copy of a file is the pristine one, and
    // manifest.txt holds the relative paths to restore.
    static string Backup(string path)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       System.Diagnostics.Process.GetCurrentProcess().Id;   // one set per run
        string set = Path.Combine(BackupRoot, stamp);
        bool underGame = path.StartsWith(game + "\\", StringComparison.OrdinalIgnoreCase);
        string rel = underGame ? path.Substring(game.Length).TrimStart('\\', '/')
                               : "@home\\" + Path.GetFileName(path);       // the order sidecar lives next to the exe
        string dst = Path.Combine(set, rel);
        if (noBackup) return Loc.F("w.noSlot");
        if (!File.Exists(path)) return "(nothing to back up) " + dst;
        if (File.Exists(dst)) return dst + "  [first copy of this run kept]";
        if (dryRun) return dst + "  [dry run]";
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.Copy(path, dst, false);
        File.AppendAllText(Path.Combine(set, "manifest.txt"), rel + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(BackupRoot, "latest.txt"), set.Replace("\\", "/") + "\n", new UTF8Encoding(false));
        return dst;
    }

    internal static int Undo()
    {
        string latest = Path.Combine(BackupRoot, "latest.txt");
        if (!File.Exists(latest)) return Fail(Loc.F("w.noUndo", BackupRoot));
        string set = Raw(latest).Split('\n')[0].Trim();
        if (!Directory.Exists(set)) return Fail("the recorded backup set is gone: " + set);
        string manifest = Path.Combine(set, "manifest.txt");
        if (!File.Exists(manifest)) return Fail("no manifest in " + set);
        int n = 0;
        foreach (var rel in Raw(manifest).Split('\n').Where(x => x.Trim() != ""))
        {
            string r = rel.Trim(), src = Path.Combine(set, r);
            string dst = r.StartsWith("@home\\") ? Path.Combine(Home, r.Substring(6)) : Path.Combine(game, r);
            if (!File.Exists(src)) { Print("   skipped, backup missing: " + src); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            Print(Loc.F("w.restored", dst));
            n++;
        }
        Print(n == 0 ? Loc.F("w.restoredNone") : Loc.F("w.restart"));
        return n == 0 ? 1 : 0;
    }

    // The sidecar next to the exe keeps the slot of a mod that is currently OFF, so switching it back
    // on cannot reshuffle the others.  A first run seeds itself from the loader's priority array.
    internal static List<string> Order()
    {
        var names = Folders();
        var saved = LoadSidecar();
        var prio = ArrayValues(Raw(root), "priority") ?? new List<string>();
        var seq = new List<string>();
        foreach (var x in (saved.Count > 0 ? saved : prio).Concat(names))
            if (names.Contains(x) && !seq.Contains(x)) seq.Add(x);
        return seq;
    }

    static List<string> LoadSidecar()
    {
        if (!File.Exists(OrderFile)) return new List<string>();
        string t = Raw(OrderFile);
        // The slots belong to one game folder; a second install must not inherit the first one's order.
        Match g = Regex.Match(t, "\"game\"\\s*:\\s*\"([^\"]*)\"");
        if (g.Success && Norm(g.Groups[1].Value) != Norm(game)) return new List<string>();
        var l = new List<string>();
        Match o = Regex.Match(t, "\"order\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
        if (!o.Success) return l;
        foreach (Match m in Regex.Matches(o.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            l.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        return l;
    }

    static string Norm(string p)
    {
        return (p ?? "").Replace("\\\\", "\\").Replace('/', '\\').Trim().TrimEnd('\\').ToUpperInvariant();
    }

    static string Priority(List<string> seq)
    {
        var on = seq.Where(Enabled).ToList();
        return "[" + string.Join(", ", on.Select(x => "\"" + x + "\"").ToArray()) + "]";
    }

    // ------------------------------------------------------------------ menu
    static int Menu()
    {
        List();
        Audit();
        Print("");
        Print("  <number|name>   toggle      o <..> enable      x <..> disable");
        Print("  u <..> move up (higher priority)     d <..> move down");
        Print("  i install-loader     r remove-loader     c console on|off     a audit     l list     q quit");
        for (; ; )
        {
            Console.Write("modswitch> ");
            string line = Console.ReadLine();
            if (line == null) return 0;
            line = line.Trim();
            if (line == "") continue;
            string verb = line.Length > 1 && (line[0] == 'o' || line[0] == 'x' || line[0] == 'u' || line[0] == 'd' || line[0] == 'c')
                ? line.Substring(0, 1) : line;
            string arg = verb == line ? "" : line.Substring(1).Trim();
            if (verb == "q" || verb == "quit" || verb == "exit") return 0;
            if (verb == "l" || verb == "ls" || verb == "list") { List(); continue; }
            if (verb == "a" || verb == "audit") { Audit(); continue; }
            if (verb == "i") { InstallLoader(); continue; }
            if (verb == "r") { RemoveLoader(); continue; }
            if (verb == "c") { if (arg == "") Print("usage: c on|off"); else ConsoleSwitch(arg); continue; }
            if (verb == "u" || verb == "d") { Move(Resolve(arg), verb == "u" ? -1 : 1); continue; }
            if (verb == "o" || verb == "x") { SetEnabled(Resolve(arg), verb == "o"); List(); continue; }
            string name = Resolve(line);
            if (name == null) { Print("unknown command or mod: " + line); continue; }
            SetEnabled(name, !Enabled(name));
            List();
        }
    }

    internal static string Resolve(string arg)
    {
        var seq = Order();
        if (string.IsNullOrEmpty(arg)) { Print("give a mod name or its number"); return null; }
        int n;
        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= seq.Count)
            return seq[n - 1];
        var hits = seq.Where(x => string.Equals(x, arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) hits = seq.Where(x => x.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (hits.Count == 1) return hits[0];
        if (hits.Count > 1) Print("ambiguous: " + string.Join(", ", hits.ToArray()));
        else Print("no mod matches: " + arg);
        return null;
    }

    // ------------------------------------------------------------------ odds and ends
    internal static int Fail(string msg) { Console.Error.WriteLine("!! " + msg); return 1; }
    static void Print(string s) { Sink(s); }
}
