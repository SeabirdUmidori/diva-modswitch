// Loc - the Chinese / English / Japanese string table for modswitch.
// Default language = the OS UI language; anything other than zh/ja/en falls back to English.
// A manual choice is remembered in modswitch-lang.txt next to the exe (values: auto | zh | en | ja),
// and modswitch-game.txt keeps the game folder the same way.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class Loc
{
    // index: 0 = 中文, 1 = English, 日本語
    const int ZH = 0, EN = 1, JA = 2;
    internal static string Lang = "en";
    internal static bool FromSystem = true;

    static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
    {
        // ---------------- window chrome
        { "title",      new[]{ "modswitch - Project DIVA Mega Mix+ mod 开关",
                               "modswitch - Project DIVA Mega Mix+ mod switcher",
                               "modswitch - Project DIVA Mega Mix+ mod スイッチ" } },
        { "btn.pick",   new[]{ "选择游戏目录", "Choose game folder", "ゲームフォルダを選択" } },
        { "btn.apply",  new[]{ "应用此路径", "Use this path", "このパスを適用" } },
        { "btn.refresh",new[]{ "刷新", "Reload", "再読込" } },
        { "btn.audit",  new[]{ "审计", "Check", "チェック" } },
        { "btn.loader", new[]{ "mod 加载组件", "mod loader", "mod ローダー" } },
        { "btn.install",new[]{ "安装 mod 加载组件", "Install the mod loader", "mod ローダーを導入" } },
        { "btn.remove", new[]{ "移除 mod 加载组件", "Remove the mod loader", "mod ローダーを削除" } },
        { "btn.console", new[]{ "调试控制台", "Debug console", "デバッグコンソール" } },
        { "btn.up",     new[]{ "↑ 上移", "Move up", "↑ 上へ" } },
        { "btn.down",   new[]{ "↓ 下移", "Move down", "↓ 下へ" } },
        { "btn.undo",   new[]{ "撤销上次开关/排序", "Undo last change", "前の変更を取り消す" } },
        { "btn.addMod", new[]{ "安装 mod（拖压缩包到此窗口）", "Install mod (drop an archive here)", "mod をインストール（アーカイブをここに）" } },
        { "btn.delMod", new[]{ "删除所选 mod", "Delete selected mod", "選択した mod を削除" } },
        { "btn.help",   new[]{ "说明", "Help", "ヘルプ" } },
        { "btn.lang",   new[]{ "语言", "Language", "言語" } },
        { "lang.auto",  new[]{ "自动（跟随系统）", "Auto (system)", "自動（OS に従う）" } },

        { "col.row",      new[]{ "行", "#", "#" } },
        { "col.folder",   new[]{ "mod 目录名", "mod folder", "mod フォルダ" } },
        { "col.display",  new[]{ "显示名", "display name", "表示名" } },
        { "col.author",   new[]{ "作者", "author", "作者" } },
        { "col.priority", new[]{ "优先级", "priority", "優先度" } },
        { "col.state",    new[]{ "加载情况", "load result", "読み込み" } },

        { "head.hint", new[]{
            "拖动一行 = 改优先级（第一行最高；也可选中后 Alt+↑/↓）；点行首 ☑/☐ 立即写入；改完重启游戏生效",
            "Drag a row to change its priority (top = highest; Alt+Up/Down works too); click the \u2611/\u2610 at the head of a row to write at once; restart the game to apply",
            "行をドラッグで優先度を変更（一番上が最高、Alt+↑/↓ でも可）。行先の ☑/☐ クリックで直ちに保存、ゲーム再起動で反映" } },

        { "status.counts", new[]{ "{0} 个 mod，{1} 个启用；开关和排序前的原件在 {2}",
                                  "{0} mods, {1} enabled; originals before each change are kept in {2}",
                                  "{0} 個の mod、{1} 個有効。変更前の原件は {2} に保存" } },
        { "status.dropTo", new[]{ "松手就放到第 {0} 行（第一行优先级最高）",
                                  "Release to move to row {0} (top row = highest priority)",
                                  "ここで離すと {0} 行目へ（最上位が最高優先度）" } },
        { "slot.notIn",   new[]{ "不在表", "not listed", "未登録" } },
        { "slot.off",     new[]{ "关", "off", "無効" } },
        { "state.ok",     new[]{ "正常", "ok", "OK" } },

        // ---------------- game folder detection
        { "game.usingSaved", new[]{ "用上次选择的目录：{0}", "using the folder you chose before: {0}",
                                    "前に選んだフォルダ：{0}" } },
        { "game.auto",       new[]{ "自动检测到游戏目录：{0}", "game folder detected: {0}",
                                    "ゲームフォルダを検出：{0}" } },
        { "game.many",       new[]{ "检测到多个游戏目录：", "several game folders were found:",
                                    "複数のゲームフォルダが見つかりました：" } },
        { "game.manyHint",   new[]{ "默认用了第一个；要换就在左上角输入路径或点「{0}」（选完会记住，之后不再轮询）。",
                                    "The first one is used. Type a path or press \"{0}\" to pick another (remembered, no more polling).",
                                    "1 番目を使用しています。変更はパス入力か「{0}」で（記憶され、以後スキャンしません）。" } },
        { "game.none",       new[]{ "!! 没找到游戏目录 - 在左上角输入路径后回车，或点「{0}」挑一个。",
                                    "!! no game folder found - type a path (top left) or press \"{0}\".",
                                    "!! ゲームフォルダが見つかりません - 左上にパスを入力か「{0}」を押してください。" } },
        { "game.set",        new[]{ "游戏目录 -> {0}（已记住，下次启动不再轮询各盘）", "game folder -> {0} (remembered; next start will not scan drives)",
                                    "ゲームフォルダ -> {0}（記憶しました。次回スキャンしません）" } },
        { "game.is",         new[]{ "游戏目录：{0}", "game folder: {0}", "ゲームフォルダ：{0}" } },
        { "game.modsIs",     new[]{ "mods 目录 -> {0}", "mods folder -> {0}", "mods フォルダ -> {0}" } },
        { "game.cfgIs",      new[]{ "加载器配置 -> {0}", "loader config -> {0}", "ローダー設定 -> {0}" } },
        { "game.cfgMissing", new[]{ "（还没有，点「{0}」创建）", "(missing - press \"{0}\" to create it)",
                                    "（ありません。「{0}」で作成）" } },
        { "game.modsMade",   new[]{ "mods 目录不存在，已建好：{0}", "mods folder did not exist and was created: {0}",
                                    "mods フォルダが無いため作成しました：{0}" } },
        { "game.badExe",     new[]{ "{0} 里没有 DivaMegaMix.exe，不像游戏根目录", "{0} has no DivaMegaMix.exe, so it is not a game folder",
                                    "{0} に DivaMegaMix.exe がありません。ゲームフォルダではないようです" } },
        { "game.badPath",    new[]{ "那不是个合法路径：{0}", "not a valid path: {0}", "無効なパスです：{0}" } },
        { "game.badMods",    new[]{ "建不出 mods 目录：{0}", "could not create the mods folder: {0}",
                                    "mods フォルダを作れません：{0}" } },
        { "game.noRemember", new[]{ "!! 记不住这个选择：{0}", "!! the choice could not be saved: {0}",
                                    "!! 設定を保存できません：{0}" } },

        // ---------------- loader components
        { "comp.bundled", new[]{ "loader\\ 里没有本地副本 - 已写出 exe 内嵌的那份 {0}（{2}）。内嵌的是 DivaModLoader（MIT，见 THIRD_PARTY.md）；想用自己的版本就往 loader\\ 里放一个 {0}，它优先于内嵌副本",
                                 "no local copy in loader\\ - wrote the one compiled into this exe: {0} ({2}). The bundled file is DivaModLoader (MIT, see THIRD_PARTY.md); drop your own {0} into loader\\ and it takes precedence over the bundled copy",
                                 "loader\\ にコピーがありません - exe に組み込んだ {0} を書き出しました（{2}）。同封的是 DivaModLoader（MIT、THIRD_PARTY.md 参照）。自分の {0} を loader\\ に置けばそちらが優先されます" } },
        { "comp.install",   new[]{ "安装 mod 加载组件到 {0}", "installing the mod loader into {0}",
                                   "{0} にmod ローダーを導入します" } },
        { "comp.remove",    new[]{ "从 {0} 移除 mod 加载组件", "removing the mod loader from {0}",
                                   "{0} からmod ローダーを削除します" } },
        { "comp.placed",    new[]{ "{0} 已放入（{1} 字节）", "{0} placed ({1} bytes)", "{0} を配置（{1} バイト）" } },
        { "comp.keptCopy",  new[]{ "已把游戏目录里现成的 {0} 备份到 {1}", "kept a copy of the game's {0} in {1}",
                                   "ゲーム内の {0} を {1} に保存しました" } },
        { "comp.noPayload", new[]{ "没有载荷：{1} 里没有 {0}，这个 exe 里也没有内嵌副本", "no payload: no {0} in {1}, and this exe has no bundled copy",
                                   "ファイルがありません：DivaModLoader の {0} を {1} に置いて再試行" } },
        { "comp.sameSize",  new[]{ "{0} 已存在且大小相同，未改动", "{0} is already there and the same size, left alone",
                                   "{0} は同じサイズで存在するため変更なし" } },
        { "comp.diffSize",  new[]{ "{0} 已存在但大小不同，未改动（要更新请手动替换）", "{0} exists but differs in size - left alone; replace it by hand to update",
                                   "{0} はサイズが異なるため触れていません。更新は手動で置換してください" } },
        { "comp.created",   new[]{ "已创建 {0}", "created {0}", "{0} を作成しました" } },
        { "comp.appended",  new[]{ "已追加：{0}", "appended: {0}", "{0} を追加しました" } },
        { "comp.removed",   new[]{ "已删除 {0}（不进撤销列表；点「安装 mod 加载组件」即可装回）", "removed {0} (not undoable; press \"Install the mod loader\" to put it back)",
                                   "{0} を削除（取り消し対象外。「mod ローダーを導入」で復元）" } },
        { "comp.absent",    new[]{ "本来就没有：{0}", "not present: {0}", "元々ありません：{0}" } },
        { "comp.nothing",   new[]{ "没有可移除的东西", "nothing to remove", "削除する物がありません" } },
        { "comp.console",   new[]{ "调试控制台 = {0}", "debug console = {0}", "デバッグコンソール = {0}" } },
        { "comp.conOn",     new[]{ "开（启动时显示 ROM 注册列表）", "on (shows the ROM paths it registers)",
                                   "ON（起動時に ROM 登録一覧を表示）" } },
        { "comp.conOff",    new[]{ "关", "off", "OFF" } },
        { "comp.noCfg",     new[]{ "{0} 还没有 - 先「安装 mod 加载组件」", "{0} does not exist yet - install the mod loader first",
                                   "{0} がありません - まずmod ローダーを導入" } },

        // ---------------- audit
        { "audit.head",   new[]{ "审计 {0}", "audit of {0}", "{0} をチェック" } },
        { "audit.clean",  new[]{ "通过：组件齐全、include/dll 全部对得上、启用与 priority 一致",
                                 "clean: components present, every include/dll entry resolves, order and enabled agree",
                                 "OK：ローダー完備、include/dll すべて解決、有効状態と priority 一致" } },
        { "audit.notes",  new[]{ "（上面 {0} 条是可选内容，不算故障）", "({0} note(s) above are choices, not faults)",
                                 "（上の {0} 件は任意の内容で、問題ではありません）" } },
        { "audit.found",  new[]{ "{0} 项发现。modswitch 从不改写 mod 的 include - 那得由包自己提供。",
                                 "{0} finding(s). modswitch never rewrites a mod's include list - that has to come from the pack itself.",
                                 "{0} 件見つかりました。modswitch は include を書き換えません（配布元で直す必要あり）" } },
        { "audit.noDll",  new[]{ "{0} 缺失 - 所有 mod 都不会加载（点「安装 mod 加载组件」）", "{0} is missing - no mod loads at all (press \"Install the mod loader\")",
                                 "{0} がありません - mod は一切読み込まれません（「mod ローダーを導入」）" } },
        { "audit.noCfg",  new[]{ "加载器配置 {0} 缺失（点「安装 mod 加载组件」）", "the loader config {0} is missing (press \"Install the mod loader\")",
                                 "ローダー設定 {0} がありません（「mod ローダーを導入」）" } },
        { "audit.noPrio", new[]{ "根配置没有 priority 数组 - mod 会按字母序加载，不是你调过的顺序（点「安装 mod 加载组件」）",
                                 "the root config has no priority array - mods load in alphabetical order, not the order you tuned (press \"Install the mod loader\")",
                                 "根設定に priority 配列がありません - 英数字順で読み込みます（「mod ローダーを導入」）" } },
        { "audit.ghost",  new[]{ "priority 里有不存在的目录：{0}", "priority names a folder that is not there: {0}",
                                 "priority に実体の無いフォルダ：{0}" } },
        { "audit.notIn",  new[]{ "{0} 已启用但不在 priority 里，所以不会加载", "{0} is enabled but not in the priority array, so it never loads",
                                 "{0} は有効ですが priority に無い為読み込まれません" } },
        { "audit.noCfg2", new[]{ "没有 config.toml - 加载器根本不读这个目录：{0}", "no config.toml - the loader never reads this folder: {0}",
                                 "config.toml 無し - このフォルダは読み込まれません：{0}" } },
        { "audit.noName", new[]{ "没有 name 字段（列表里会显示目录名）：{0}", "no name field (the loader shows the folder name): {0}",
                                 "name がありません（フォルダ名が表示されます）：{0}" } },
        { "audit.noDll2", new[]{ "{0} 声明的 dll 不存在：{1}", "{0} declares a dll that is not there: {1}",
                                 "{0} の dll が見つかりません：{1}" } },

        // ---------------- what the loader will do with a folder (the 加载情况 column)
        { "inc.nothing",  new[]{ "什么都读不到：include = {0} 没有对上任何 rom/ 目录", "loads NOTHING: include = {0} matches no rom/ folder",
                                 "何も読み込めません：include = {0} が rom/ に一致しません" } },
        { "inc.unreg",    new[]{ "{0} 个子目录才有 rom/ 却没进 include：{1}", "{0} subfolder(s) hold the only rom/ trees but are not in include: {1}",
                                 "rom/ があるサブフォルダ {0} 個が include 外：{1}" } },
        { "inc.optional", new[]{ "{0} 个可选子目录带 rom/ 但未加载（{1}）- 这是选择，不是故障",
                                 "{0} optional subfolder(s) ship a rom/ and are not loaded ({1}) - a choice, not damage",
                                 "任意のサブフォルダ {0} 個（{1}）は未読み込み - 設定どおりで問題なし" } },
        { "inc.dead",     new[]{ "{0} 项 include 指向不存在的位置：{1}", "{0} include entr(y/ies) point at nothing: {1}",
                                 "include の {0} 項が存在しません：{1}" } },
        { "inc.noArray",  new[]{ "没有 include 数组，这个目录下的东西一个都不会注册", "no include array, so nothing under this folder is registered",
                                 "include 配列が無い為、このフォルダは登録されません" } },
        { "inc.noCfgSubs",new[]{ "rom/ 在 {0} 个子目录里，但没有 config.toml", "rom/ folders sit in {0} subdirectories but there is no config.toml",
                                 "rom/ がサブフォルダ {0} 個に分かれていますが config.toml がありません" } },

        // ---------------- writes and refusals
        { "w.on",     new[]{ "{0} -> 启用", "{0} -> enabled", "{0} -> 有効" } },
        { "w.off",    new[]{ "{0} -> 禁用", "{0} -> disabled", "{0} -> 無効" } },
        { "w.already",new[]{ "{0} 已经是{1}了", "{0} is already {1}", "{0} は既に{1}です" } },
        { "w.on2",    new[]{ "开启", "on", "有効" } },
        { "w.off2",   new[]{ "关闭", "off", "無効" } },
        { "w.noCfg",  new[]{ "{0} 没有 config.toml - 加载器不读它，无需切换", "{0} has no config.toml - the loader never reads it, nothing to toggle",
                             "{0} に config.toml がありません - 切り替え対象外です" } },
        { "w.slot",   new[]{ "{0} -> 第 {1} 行（共 {2}）", "{0} -> slot {1} of {2}", "{0} -> {1} 行目 / 全 {2}" } },
        { "w.same",   new[]{ "{0} 已经在第 {1} 行，没有写入", "{0} is already in slot {1}, nothing written",
                             "{0} は既に {1} 行目、変更なし" } },
        { "w.first",  new[]{ "{0} 已经是第一行（最高优先级）", "{0} is already first (highest priority)",
                             "{0} は最上位です（最高優先度）" } },
        { "w.last",   new[]{ "{0} 已经是最后一行", "{0} is already last", "{0} は最下位です" } },
        { "w.pickRow", new[]{ "先在列表里选中一行，再点上移/下移", "select a row first, then press move up/down",
                              "まず行を選択してください" } },
        { "w.noMod",  new[]{ "没有这个 mod：{0}", "no such mod: {0}", "mod が見つかりません：{0}" } },
        { "w.refused",new[]{ "!! 拒绝写入 {0}：除 {1} 外还会改动 {2} 处，文件保持原样",
                             "!! refused: {0} would change {2} line(s) beyond {1}; the file was left alone",
                             "!! 書き込み中止 {0}：{1} 以外に {2} 箇所変わるため未変更" } },
        { "w.same2",  new[]{ "{0} 已是该值，未改动", "{0} already set that way, file left alone", "{0} は同じ値のため変更なし" } },
        { "w.backup", new[]{ "备份：{0}", "backup: {0}", "バックアップ：{0}" } },
        { "w.noSlot", new[]{ "（这类操作不占撤销位）", "(this kind of change is not undoable)", "（この操作は取り消し対象外）" } },
        { "w.noUndo", new[]{ "没有备份可还原：{0}", "no backups in {0}", "バックアップがありません：{0}" } },
        { "w.restored",new[]{ "已还原 {0}", "restored {0}", "{0} を復元しました" } },
        { "w.restoredNone", new[]{ "什么都没还原", "nothing restored", "復元された物はありません" } },
        { "w.restart", new[]{ "加载器只在启动时读这些文件 - 重启游戏生效", "the loader reads these at startup - restart the game to see it",
                              "ローダーは起動時にのみ読みます - ゲーム再起動で反映" } },
        { "w.noOrder", new[]{ "{0} 还不存在 - 先「安装 mod 加载组件」", "{0} does not exist yet - install the mod loader first",
                              "{0} がありません - まずmod ローダーを導入" } },
        { "w.noPrioArr", new[]{ "拒绝：{0} 里没有 priority 数组 - 请手动加一行", "refused: {0} has no priority array - add one by hand",
                                "中止：{0} に priority 配列がありません。手動で追加してください" } },
        { "w.crcrlf",  new[]{ "!! 拒绝写出含 CR-CR-LF 的 config.toml（会破坏加载器的 TOML 解析）",
                              "!! refusing to write a config.toml containing CR-CR-LF (breaks the loader's TOML parser)",
                              "!! CR-CR-LF を含む config.toml は書き出しません（ローダーの解析が壊れます）" } },
        { "w.crlfBad", new[]{ "!! 拒绝：{0} 的行尾会变化（原 {1} 行 -> 现 {2} 行），未写入",
                              "!! refused: {0} would change the line count ({1} -> {2}); nothing written",
                              "!! 中止：{0} の行数が変わります（{1} -> {2}）。未変更" } },

        // ---------------- dialogs
        { "dlg.removeAsk", new[]{
            "移除 mod 加载组件会删掉游戏目录里的 {0} 和 config.toml。\n这两样不进撤销列表：点「安装 mod 加载组件」就装回（载荷在 loader\\ 里）。\n\n确定吗？",
            "Removing the loader deletes {0} and config.toml from the game folder.\nThese are not undoable - press \"Install the mod loader\" to put them back (the payload is kept in loader\\).\n\nContinue?",
            "mod ローダーを削除するとゲームフォルダの {0} と config.toml が消えます。\n取り消し対象外です。「mod ローダーを導入」で復元できます（loader\\ に保存済み）。\n\n続けますか？" } },
        { "dlg.badHint", new[]{ "\n\n判据只有一个：目录里要有 DivaMegaMix.exe；mods 不存在会自动创建。",
                                "\n\nThe only test: the folder must contain DivaMegaMix.exe; mods is created when missing.",
                                "\n\n条件はただ一つ：DivaMegaMix.exe があること。mods が無ければ自動作成します。" } },
        // ---------------- install / delete a mod
        { "in.pick",      new[]{ "选择 mod 压缩包", "Choose a mod archive", "mod アーカイブを選択" } },
        { "in.onlyZip",   new[]{ "不是可认的压缩包（只处理 zip / 7z / rar，按文件头判断，不看扩展名）",
                                 "not an archive this tool reads (zip / 7z / rar, decided by the file header, not the extension)",
                                 "このツールが読める圧縮形式ではありません（zip / 7z / rar、拡張子でなく中身で判定）" } },
        { "in.needTar",   new[]{ "系统里没有 System32\\tar.exe（Windows 10 1803 起自带），7z/rar 解不了；请改成 zip 打包",
                                 "no System32\\tar.exe on this system (it ships with Windows 10 1803 and later), so 7z/rar cannot be opened - re-pack as zip",
                                 "System32\\tar.exe が見つからないため 7z/rar を解凍できません（Windows 10 1803 以降に同梱）。zip で代用してください" } },
        { "in.noCfg",     new[]{ "压缩包里找不到 config.toml，不像一个 mod 包", "no config.toml inside the archive, so it is not a mod package",
                                 "アーカイブ内に config.toml がありません" } },
        { "in.ask", new[]{ "把 {0} 安装到\n\n{1}\n\nmod 名：{2}",
                                 "Install {0} into\n\n{1}\n\nmod name: {2}",
                                 "{0} をインストールします\n\n{1}\n\nmod 名：{2}" } },
        { "in.exists", new[]{ "{0} 已经存在。\n\n是 = 覆盖（旧副本先移到 modswitch-deleted\\）　否 = 跳过",
                                 "{0} already exists.\n\nYes = replace it (the old copy moves to modswitch-deleted\\), No = skip",
                                 "{0} は既に存在します。\n\nはい＝置換（旧物は modswitch-deleted\\ へ）　いいえ＝スキップ" } },
        { "in.done", new[]{ "装好 {0}：写出 {1} 个文件（包内根：{2}）",
                                 "installed {0}: {1} file(s) written (archive root: {2})",
                                 "{0}：{1} 個のファイルを書きました（アーカイブ内の根：{2}）" } },
        { "in.slip",  new[]{ "!! 跳过越界条目 {0}（只允许写进该 mod 自己的目录）",
                                 "!! skipped escaping entry {0} (only files inside that mod's own folder may be written)",
                                 "!! 脱出した項目 {0} をスキップ（その mod のフォルダ内にのみ書けます）" } },
        { "in.bad",   new[]{ "!! 压缩包没装成：{0}", "!! install failed: {0}", "!! インストール失敗：{0}" } },
        { "in.drop",  new[]{ "松手安装 {0}", "release to install {0}", "離すと {0} をインストール" } },
        { "del.pick", new[]{ "先在列表里选中一行，再点删除", "select a row first, then press delete",
                                 "行を選択してから削除してください" } },
        { "del.done", new[]{ "已删除 {0} - 原件在 {1}", "removed {0} - the files are in {1}",
                                 "{0} を削除しました - 実物は {1}" } },
        { "del.ask", new[]{ "删除 {0}？\n\n文件不会被抹掉，而是移到\n{1}\n\n（同一磁盘＝改名，随时可以移回）",
                                 "Delete {0}?\n\nNothing is erased; the folder is moved to\n{1}\n\n(same drive, so it is a rename you can undo)",
                                 "{0} を削除しますか？\n\n完全には消さず、次へ移動します\n{1}\n\n（同一ディスクなので戻せます）" } },
        { "del.askMany", new[]{ "删除选中的 {0} 个 mod？\n\n{1}\n\n文件不会被抹掉，而是各自移到\n{2}\n\n（同一磁盘＝改名，随时可以移回）",
                                 "Delete the {0} selected mods?\n\n{1}\n\nNothing is erased; each folder moves to\n{2}\n\n(same drive, so it is a rename you can undo)",
                                 "選択した {0} 件を削除しますか？\n\n{1}\n\n完全には消さず、各フォルダを次へ移動します\n{2}\n\n（同一ディスクなので戻せます）" } },
        { "del.running",  new[]{ "!! 游戏正在运行 - 先退出，否则删除后它仍会读旧文件",
                                 "!! the game is running - close it first",
                                 "!! ゲーム実行中です - 先に終了してください" } },

        // ---------------- the help dialog
        { "help.body", new[]{
            "这个窗口只做两件事，而且每次都只改文件里的一行：" + "\n" +
            "  1) 点行首的 ☑/☐（或选中一行按空格）= 改该 mod 自己 config.toml 的 enabled 一行（立即写入）；按住 Ctrl/Shift 点那里只选不翻；" + "\n" +
            "  2) 用鼠标拖动一行 = 改根 config.toml 的 priority 一行。要从行的文字区域起手（点最左边的 ☑/☐ 是开关，不会开始拖动）；选中一行后 Alt+上/下 是同一个操作。只有按下后再移动才算拖动，单击不会改序。" + "\n" +
            "「优先级」列 = 它在 priority 数组里的位置；「不在表」= 已启用但没进数组（加载器不会读它，橙底）；「关」= 该 mod 已禁用。第一行优先级最高。" + "\n" +
            "其他字节（include、name、author、注释、CRLF）一律原样保留；写完会逐行比对，改多了就拒绝写入并回报。" + "\n\n" +
            "【检查是干嘛的】按加载器的算法把这套装机预演一遍，只读不改：加载器把每个 include 项拼成 <mod目录>/<项>/rom，不存在的那条会被静默丢掉，所以它能看出某个包其实一字节都没注册上（DivaModManager 就是把 include 抹成 [\".\"] 的元凶）；另外还看带 rom/ 的子目录有没有漏登记、声明的 dll 在不在、启用的 mod 在不在 priority 里、dinput8.dll 和根 config.toml 齐不齐。" + "\n" +
            "红底行 = 加载不出来；橙底行 = 启用了但不在 priority 里。" + "\n\n" +
            "【游戏目录】左上角可直接输入/粘贴路径后回车，或点「选择游戏目录」浏览。判据只有目录里有 DivaMegaMix.exe；mods 不存在会自动创建。选好后记在 modswitch-game.txt，下次启动不再轮询各盘。" + "\n\n" +
            "【安装/删除 mod】把 .zip / .7z / .rar 拖到窗口任意位置，或点「安装 mod」选文件：读压缩包内 config.toml 的 name 当文件夹名，把「含 config.toml 的那一层」解到 mods\\<mod名>\\（压缩包外面套的目录会被去掉）；同名会先问，替换前旧副本移进 modswitch-deleted\\。「删除所选 mod」不抹文件，只移到 modswitch-deleted\\（同盘改名，可移回）；按住 Ctrl 或 Shift 点行可以多选，一次删多个（确认框会列出名字）。多选期间拖动行不会改序——改序只针对单行。 7z 与 rar 用 Windows 自带的 System32	ar.exe（bsdtar/libarchive，Win10 1803 起内置）解，不额外装任何东西；加密包它解不开，会照实报错。" + "\n\n" +
            "【mod 加载组件】只有一个按钮：游戏目录里没有 dinput8.dll 时它写「安装 mod 加载组件」，有了就写「移除 mod 加载组件」，点下去执行屏幕上显示的那一半。" + "\n" +
            "【语言】右上角可切 中文/English/日本語，默认跟随系统语言（不是这三种时用英文），选择记在 modswitch-lang.txt。" + "\n" +
            "【撤销】只有 mod 开关和排序进撤销列表；组件与控制台开关不进。原件在 modswitch-backup\\。",
            "This window does two things, and each one edits exactly one line in a file:" + "\n" +
            "  1) click the \u2611/\u2610 at the head of a row (or press Space on a selected row) = that mod's own config.toml enabled line, written immediately; Ctrl/Shift there only selects, it never flips;" + "\n" +
            "  2) drag a row with the mouse = the loader root config's priority line. Start the drag on the row text - the \u2611/\u2610 at the far left is the switch, not a handle; Alt+Up/Down on a selected row does the same. Moving after the press is what makes it a drag - a plain click never reorders." + "\n" +
            "The priority column = its position in the array; \"not listed\" = enabled but missing from it, so the loader skips it (orange row); \"off\" = disabled. The top row has the highest priority." + "\n" +
            "Everything else (include, name, author, comments, CRLF) is preserved byte for byte; after writing, every other line is compared and the write is refused if more than the one line changed." + "\n\n" +
            "[What Check does] It replays the loader's own algorithm over this install, read-only: each include entry becomes <mod folder>/<entry>/rom and entries that do not exist are dropped in silence, so it can show that a pack registers nothing at all (DivaModManager is what flattens include to [\".\"]); it also looks for rom/ subfolders left out of include, missing dll files, enabled mods missing from priority, and the loader components themselves. Red rows = will not load; orange = enabled but not in priority." + "\n\n" +
            "[Game folder] Type or paste a path at the top left and press Enter, or use \"Choose game folder\". The only requirement is DivaMegaMix.exe in it; mods is created when missing. The choice is remembered in modswitch-game.txt so the next start does not scan drives." + "\n\n" +
            "[Install / delete a mod] Drop a .zip / .7z / .rar anywhere on the window, or press \"Install mod\": the archive's config.toml name becomes the folder, and the level containing config.toml is unpacked into mods\\<name>\\ (a wrapper folder inside the zip is dropped). An existing folder is confirmed first and its old copy moves to modswitch-deleted\\. \"Delete selected mod\" never erases - it moves the folder there (same drive, a rename you can undo); Ctrl/Shift-click rows to pick several and delete them in one go (the confirm box lists them). While several rows are selected, dragging a row does not reorder - ordering is a one-row operation. 7z and rar are decoded by the bsdtar built into Windows (System32	ar.exe, libarchive, since 10 1803), so nothing has to be installed; encrypted archives cannot be opened and are reported as such." + "\n\n" +
            "[The mod loader] is one button: it reads \"Install the mod loader\" while dinput8.dll is missing and \"Remove the mod loader\" once it is there, and pressing it does the half shown on screen." + "\n" +
            "[Language] Top right switches 中文 / English / 日本語; the default follows the OS and falls back to English. Remembered in modswitch-lang.txt." + "\n" +
            "[Undo] Only mod toggles and ordering are undoable; loader components and the debug console are not. Originals go to modswitch-backup\\.",
            "このウィンドウは2つことだけをし、どちらも毎回1行だけ書き換えます：" + "\n" +
            "  1) 行の先の ☑/☐ をクリック（または選択行でスペース）= その mod の config.toml の enabled 行を即保存。Ctrl/Shift をそこに使うと選択だけで状態は変わりません；" + "\n" +
            "  2) 行をドラッグ = 根 config.toml の priority 行。掴むのは行の文字部分（先の ☑/☐ はスイッチで、ドラッグの取っ手ではありません）。選択行での Alt+上/下 でも同じ。押してから動かすのがドラッグで、クリックだけでは並びは変わりません。" + "\n" +
            "優先度列 = priority 配列中の位置。未登録 = 有効だが配列に無い為読み込まれません（橙）。無効 = OFF。最上位が最高優先度。" + "\n" +
            "それ以外（include、name、author、コメント、CRLF）はそのまま。書き込み後に行を突き合わせ、1行以上変わるなら拒否します。" + "\n\n" +
            "【チェックの役割】ローダーのアルゴリズムを読み取り専用で再現します：各 include 項目は <mod>/<項目>/rom となり、存在しない物は黙って破棄される為、このパッケージは実は一切登録されていないと分かります（DivaModManager は include を [\".\"] に潰します）。" + "\n\n" +
            "【ゲームフォルダ】左上にパスを入力して Enter、または「ゲームフォルダを選択」。条件は DivaMegaMix.exe がある事だけ、mods が無ければ作成します。modswitch-game.txt に記憶され、次回以降スキャンしません。" + "\n\n" +
            "【インストール/削除】.zip / .7z / .rar をウィンドウにドラッグ、または「mod をインストール」。アーカイブ内の config.toml の name がフォルダ名になり、config.toml を含む階層が mods\\<name>\\ に展開されます（外側の余分な上位フォルダは除去）。既存なら確認し、旧物は modswitch-deleted\\ へ。「選択した mod を削除」は消さずそこへ移します（同一ディスクなので戻せる）。Ctrl/Shift + クリックで複数選択して一括削除できます（確認ダイアログに一覧を表示）。複数選択中は行のドラッグで並び替えしません（並び替えは 1 行だけの操作）。7z/rar は Windows 標準の System32	ar.exe（bsdtar/libarchive、Win10 1803 以降）で解凍するため追加導入は不要です。暗号化アーカイブには対応せず、その場合はエラーを表示します。" + "\n\n" +
            "【mod ローター】ボタンは1個だけ：ゲームフォルダに dinput8.dll が無い時は「mod ローダーを導入」、有れば「mod ローダーを削除」と表示し、表示中の操作を実行します。" + "\n" +
            "【言語】右上で 中文/English/日本語 を切替。既定は OS に従い、この3つ以外なら英語。modswitch-lang.txt に保存。" + "\n" +
            "【取り消し】mod の開閉と並び順のみ対象。ローダー導入/削除とコンソールは対象外。原件は modswitch-backup\\。" } },
    };

    // exposed so a test can walk the table and check every language has the same placeholders
    internal static IEnumerable<string> Keys { get { return S.Keys; } }
    internal static string At(string key, int languageIndex) { return S[key][languageIndex]; }

    internal static string S_(string key)
    {
        string[] row;
        if (!S.TryGetValue(key, out row)) return key;
        int i = Lang == "zh" ? ZH : Lang == "ja" ? JA : EN;
        return row[i];
    }

    internal static string F(string key) { return S_(key); }
    internal static string F(string key, object a) { return string.Format(S_(key), a); }
    internal static string F(string key, object a, object b) { return string.Format(S_(key), a, b); }
    internal static string F(string key, object a, object b, object c) { return string.Format(S_(key), a, b, c); }

    static string LangFile
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'), "modswitch-lang.txt"); }
    }

    /// The remembered choice, or the OS UI language when nothing was chosen (or the choice is "auto").
    internal static void Detect()
    {
        string want = "";
        try { if (File.Exists(LangFile)) want = File.ReadAllText(LangFile).Trim().ToLowerInvariant(); }
        catch (Exception) { want = ""; }
        if (want == "zh" || want == "en" || want == "ja") { Lang = want; FromSystem = false; return; }
        Lang = FromCulture(Environment.GetEnvironmentVariable("LANG"))
             ?? FromCulture(System.Globalization.CultureInfo.CurrentUICulture.Name);
        FromSystem = true;
    }

    static string FromCulture(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return null;
        string c = culture.ToLowerInvariant();
        if (c.StartsWith("zh")) return "zh";
        if (c.StartsWith("ja")) return "ja";
        if (c.StartsWith("en")) return "en";
        return null;                    // anything else falls back to English
    }

    /// Persist a choice; "auto" goes back to following the system language.
    internal static void Choose(string lang)
    {
        try
        {
            if (lang == "auto") { if (File.Exists(LangFile)) File.Delete(LangFile); }
            else File.WriteAllText(LangFile, lang + "\n", new UTF8Encoding(false));
        }
        catch (Exception) { }
        Detect();
    }

    internal static string[] Options { get { return new[] { "auto", "zh", "en", "ja" }; } }

    internal static string Label(string option)
    {
        if (option == "auto") return S_("lang.auto");
        if (option == "zh") return "中文";
        if (option == "en") return "English";
        return "日本語";
    }
}
