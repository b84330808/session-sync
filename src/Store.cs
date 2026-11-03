// Reads and changes the Claude desktop app's per-account session lists.
//
// Layout (Claude desktop, Code tab):
//   <sessionsRoot>\<accountUuid>\<orgUuid>\local_<uuid>.json    one sidebar card
//   <sessionsRoot>\<accountUuid>\<orgUuid>\deleted_<uuid>        tombstone, body = ms timestamp
//   <sessionsRoot>\<accountUuid>\<orgUuid>\archived-sessions.idx {"v":1,"archived":["local_<uuid>",...]}
//   <projectsDir>\<project>\<cliSessionId>.jsonl                  transcript, shared by all accounts
//
// The card is the only per-account piece, so moving a session between
// accounts means copying its card file byte for byte. Cards are never re-serialized.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SessionSync
{
    class Card
    {
        public string Id, SessionId, CliSessionId, Title, Cwd, Model, Effort, Broken;
        public long CreatedAt, LastActivityAt, LastFocusedAt, CompletedTurns, Size;
        public DateTime Mtime;
        public bool Archived, Scratch, TranscriptExists;
        public long TranscriptSize;
        public string TranscriptPath;
        public List<string> AlsoIn = new List<string>();
        public bool Stale;

        // Cached cards are shared; each scan works on its own copy.
        public Card Fresh()
        {
            var n = (Card)MemberwiseClone();
            n.AlsoIn = new List<string>();
            n.Stale = false;
            return n;
        }
    }

    class Account
    {
        public string Key, LabelKey, Root, Dir, AccountUuid, OrgUuid, Label, Email, Person, OrgName;
        public bool Current, OtherOrgsInUse;
        public List<Card> Cards = new List<Card>();
        public Dictionary<string, long> Tombstones = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public bool Known { get { return !string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(Email); } }

        // The app also makes folders for account/org pairs that were never used
        // (only scheduled-tasks.json inside). A folder counts once it has held a session,
        // or when it belongs to the signed-in account and that account has no other folder in use.
        public bool InUse;
        public string Name
        {
            get { return !string.IsNullOrEmpty(Label) ? Label : !string.IsNullOrEmpty(Email) ? Email : L.F("未認出的帳號（{0}）", AccountUuid.Substring(0, 4)); }
        }

        // Second line under the name: the person and the org, in words where we know them.
        public string Short
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Person) && Person != Name) parts.Add(Person);
                if (!string.IsNullOrEmpty(Label) && !string.IsNullOrEmpty(Email)) parts.Add(Email);
                if (!string.IsNullOrEmpty(OrgName))
                {
                    const string suffix = "'s Organization";
                    string owner = OrgName.EndsWith(suffix) ? OrgName.Substring(0, OrgName.Length - suffix.Length) : null;
                    parts.Add(owner == null ? OrgName : owner == Email ? L.T("個人組織") : L.F("{0} 的個人組織", owner));
                }
                else if (OtherOrgsInUse || Cards.Count == 0) parts.Add(L.F("組織 {0}", OrgUuid.Substring(0, 8)));
                if (!Known) parts.Add(L.T("用這個帳號登入 Claude 後開一次本程式就會認出"));
                return string.Join(" · ", parts);
            }
        }
        public Card Find(string id) { return Cards.FirstOrDefault(c => c.Broken == null && c.Id == id); }
    }

    class Item
    {
        public string Id, Title, From, To, Type, Reason;
        public bool CanOverwrite;
    }

    class PurgeTarget
    {
        public string Id, Title, Blocked;
        public long LastActivityAt, Bytes, TranscriptWriteMs;
        public List<string> AlsoDeleted = new List<string>();
        public List<KeyValuePair<Account, string>> Cards = new List<KeyValuePair<Account, string>>();
        public List<string> Paths = new List<string>();
    }

    class Orphan
    {
        public string Id, Path, Title, Cwd;
        public long Size, LastWriteMs, FirstMs;
    }

    class AccountPurgePlan
    {
        public Account Account;
        public List<Card> Exclusive, Shared;
    }

    class OpResult
    {
        public string BatchId;
        public long Bytes;
        public List<Item> Done = new List<Item>();
        public List<Item> Skipped = new List<Item>();
    }

    class SyncOptions
    {
        public List<string> Targets = new List<string>();
        public bool IncludeArchived = false, RespectTombstones = true, SkipMissingTranscript = true;
    }

    class SyncPlan
    {
        public List<Item> Ops = new List<Item>();
        public List<Item> Skipped = new List<Item>();
    }

    class SkipException : Exception
    {
        public bool CanOverwrite;
        public SkipException(string reason, bool canOverwrite = false) : base(reason) { CanOverwrite = canOverwrite; }
    }

    class Store
    {
        const string UUID = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
        static readonly Regex CardRe = new Regex("^local_(" + UUID + ")\\.json$");
        static readonly Regex TombRe = new Regex("^deleted_(" + UUID + ")$");
        static readonly Regex UuidRe = new Regex("^" + UUID + "$");
        const string ArchiveIdx = "archived-sessions.idx";
        static readonly string[] CardFields = {
            "sessionId", "cliSessionId", "title", "cwd", "originCwd", "model", "effort",
            "createdAt", "lastActivityAt", "lastFocusedAt", "completedTurns", "isArchived" };

        // Demo mode works on made-up data, so the real Claude app's state doesn't apply.
        public static bool Demo;

        // Set by --trust-format: allow changes even when the files don't look as expected.
        public static bool TrustFormat;
        public const long BusyMs = 3 * 60 * 1000;

        // What the last scan found odd about Claude's files. Any entry makes the store read-only,
        // because writing into a layout we don't understand could corrupt Claude's lists.
        public List<string> FormatIssues = new List<string>();
        public bool ReadOnly { get { return FormatIssues.Count > 0 && !TrustFormat; } }

        // Conversation files on disk that no account's list points at.
        public List<string> OrphanIds = new List<string>();
        Dictionary<string, string> lastTranscripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Guard()
        {
            if (ReadOnly) throw new InvalidOperationException(L.F("唯讀模式：{0}", FormatIssues[0]));
        }

        public List<string> Roots;
        public string ProjectsDir, DataDir, HistoryDir, SettingsPath;
        public List<string> ClaudeJsonPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".claude.json"),
        };
        public List<Account> Accounts = new List<Account>();

        // Parsed cards keyed by path, reused while size and write time match. A copied card
        // keeps its source's write time, so it can match the old entry; every write batch clears it.
        class Cached { public DateTime Mtime; public long Size; public Card Card; }
        readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);

        public Store(List<string> roots, string projectsDir, string dataDir)
        {
            Roots = roots;
            ProjectsDir = projectsDir;
            DataDir = dataDir;
            HistoryDir = Path.Combine(dataDir, "history");
            SettingsPath = Path.Combine(dataDir, "settings.json");
        }

        public static Store FromEnvironment()
        {
            var roots = new List<string>();
            string env = Environment.GetEnvironmentVariable("CSS_SESSIONS_ROOT");
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            string roaming = Environment.GetEnvironmentVariable("APPDATA");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(env))
            {
                roots.AddRange(env.Split(Path.PathSeparator).Where(p => p.Length > 0));
            }
            else
            {
                string baseDir = Path.Combine(local ?? "", "Claude-Data");
                if (Directory.Exists(baseDir))
                    foreach (var d in Directory.GetDirectories(baseDir, "claude_*"))
                        roots.Add(Path.Combine(d, "claude-code-sessions"));
                roots.Add(Path.Combine(roaming ?? "", "Claude", "claude-code-sessions"));
                roots = roots.Where(Directory.Exists).ToList();
            }
            string configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
            string projects = Environment.GetEnvironmentVariable("CSS_PROJECTS_DIR") ?? Path.Combine(configDir, "projects");
            string data = Environment.GetEnvironmentVariable("CSS_DATA_DIR") ?? Path.Combine(local ?? home, "claude-session-sync");
            return new Store(roots, projects, data);
        }

        // ---------- reading ----------

        bool settingsBackedUp;

        Dictionary<string, object> Settings()
        {
            if (!File.Exists(SettingsPath)) return new Dictionary<string, object>();
            try { return Json.ParseObject(File.ReadAllText(SettingsPath, Fs.Utf8)); }
            catch
            {
                // Keep the unreadable file, so the next save doesn't wipe labels and names unseen.
                if (!settingsBackedUp)
                {
                    settingsBackedUp = true;
                    try { File.Copy(SettingsPath, SettingsPath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), false); } catch { }
                }
                return new Dictionary<string, object>();
            }
        }

        Dictionary<string, object> Labels(Dictionary<string, object> settings)
        {
            object v;
            return settings.TryGetValue("labels", out v) && v is Dictionary<string, object> ? (Dictionary<string, object>)v : new Dictionary<string, object>();
        }

        public void SetLabel(string key, string label)
        {
            var acct = Get(key);
            var s = Settings();
            var labels = Labels(s);
            label = (label ?? "").Trim();
            if (label.Length > 40) label = label.Substring(0, 40);
            if (label.Length > 0) labels[acct.LabelKey] = label; else labels.Remove(acct.LabelKey);
            s["labels"] = labels;
            Fs.AtomicWrite(SettingsPath, Json.Write(s));
            acct.Label = label;
        }

        // Small UI preferences (window size, drag mode) live next to the labels.
        public string Pref(string key)
        {
            var s = Settings();
            object ui;
            return s.TryGetValue("ui", out ui) ? Json.Str(ui as Dictionary<string, object>, key) : null;
        }

        public void SetPref(string key, string value)
        {
            try
            {
                var s = Settings();
                object ui;
                var d = s.TryGetValue("ui", out ui) && ui is Dictionary<string, object> ? (Dictionary<string, object>)ui : new Dictionary<string, object>();
                d[key] = value;
                s["ui"] = d;
                Fs.AtomicWrite(SettingsPath, Json.Write(s));
            }
            catch { /* preferences are best effort */ }
        }

        static string CurrentAccountFor(string root)
        {
            // The app records the signed-in account next to the sessions folder.
            try { return Json.Str(Json.ParseObject(File.ReadAllText(Path.Combine(Path.GetDirectoryName(root), "config.json"), Fs.Utf8)), "lastKnownAccountUuid"); }
            catch { return null; }
        }

        KnownNames known;

        // Learns account e-mails and org names from local caches and remembers them,
        // in memory for this run and in settings.json for the next one.
        KnownNames LearnNames(Dictionary<string, object> settings)
        {
            if (known == null) known = KnownNames.From(settings);
            var dataDirs = Roots.Select(r => Path.GetDirectoryName(r)).Where(d => d != null);
            if (Names.Learn(known, dataDirs, ClaudeJsonPaths))
            {
                try
                {
                    var fresh = Settings();
                    known.WriteTo(fresh);
                    Fs.AtomicWrite(SettingsPath, Json.Write(fresh));
                }
                catch { /* names are a convenience */ }
            }
            return known;
        }

        Dictionary<string, string> TranscriptIndex()
        {
            var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(ProjectsDir)) return idx;
            foreach (var dir in Directory.GetDirectories(ProjectsDir))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
                        idx[Path.GetFileNameWithoutExtension(f)] = f;
                }
                catch { }
            }
            return idx;
        }

        Card ReadCard(string file)
        {
            var fi = new FileInfo(file);
            Cached hit;
            if (cache.TryGetValue(file, out hit) && hit.Mtime == fi.LastWriteTimeUtc && hit.Size == fi.Length) return hit.Card;
            string text;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs, Fs.Utf8)) text = r.ReadToEnd();
            var j = Json.TopLevel(text, CardFields);
            var c = new Card
            {
                SessionId = Json.Str(j, "sessionId"),
                CliSessionId = Json.Str(j, "cliSessionId"),
                Title = Json.Str(j, "title") ?? L.T("(未命名)"),
                Cwd = Json.Str(j, "cwd") ?? Json.Str(j, "originCwd") ?? "",
                Model = Json.Str(j, "model") ?? "",
                Effort = Json.Str(j, "effort") ?? "",
                CreatedAt = Json.Long(j, "createdAt"),
                LastActivityAt = Json.Long(j, "lastActivityAt"),
                LastFocusedAt = Json.Long(j, "lastFocusedAt"),
                CompletedTurns = Json.Long(j, "completedTurns"),
                Archived = Json.Bool(j, "isArchived"),
                Mtime = fi.LastWriteTimeUtc,
                Size = fi.Length,
            };
            cache[file] = new Cached { Mtime = fi.LastWriteTimeUtc, Size = fi.Length, Card = c };
            return c;
        }

        public void Scan()
        {
            var settings = Settings();
            var labels = Labels(settings);
            var names = LearnNames(settings);
            var transcripts = TranscriptIndex();
            var list = new List<Account>();
            bool multi = Roots.Count > 1;
            var issues = new List<string>();
            int cardsSeen = 0, cardsOdd = 0;

            for (int ri = 0; ri < Roots.Count; ri++)
            {
                string root = Roots[ri];
                if (!Directory.Exists(root)) continue;
                string current = CurrentAccountFor(root);
                foreach (var aDir in Directory.GetDirectories(root))
                {
                    string acctUuid = Path.GetFileName(aDir);
                    if (!UuidRe.IsMatch(acctUuid)) continue;
                    foreach (var oDir in Directory.GetDirectories(aDir))
                    {
                        string orgUuid = Path.GetFileName(oDir);
                        if (!UuidRe.IsMatch(orgUuid)) continue;
                        // With several Claude data folders, the folder name keeps the same account apart.
                        string labelKey = (multi ? Path.GetFileName(Path.GetDirectoryName(root)) + ":" : "") + acctUuid + "/" + orgUuid;
                        string mail, person, orgName;
                        names.Email.TryGetValue(acctUuid, out mail);
                        names.Person.TryGetValue(acctUuid, out person);
                        names.Org.TryGetValue(orgUuid, out orgName);
                        object lbl;
                        var acct = new Account
                        {
                            Key = labelKey,
                            LabelKey = labelKey,
                            Root = root,
                            Dir = oDir,
                            AccountUuid = acctUuid,
                            OrgUuid = orgUuid,
                            Label = labels.TryGetValue(labelKey, out lbl) ? lbl as string : null,
                            Email = mail,
                            Person = person,
                            OrgName = orgName,
                            Current = string.Equals(current, acctUuid, StringComparison.OrdinalIgnoreCase),
                        };
                        var archived = ReadArchiveIndex(oDir);
                        string idxVersion = ArchiveIndexVersion(oDir);
                        if (idxVersion != null && idxVersion != "1") issues.Add(L.F("archived-sessions.idx 的版本是 {0}", idxVersion));
                        foreach (var f in Directory.GetFiles(oDir))
                        {
                            string fn = Path.GetFileName(f);
                            var m = CardRe.Match(fn);
                            if (m.Success)
                            {
                                string id = m.Groups[1].Value.ToLowerInvariant();
                                try
                                {
                                    var c = ReadCard(f).Fresh();
                                    c.Id = id;
                                    cardsSeen++;
                                    if (!string.Equals(c.SessionId, "local_" + id, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.CliSessionId)) cardsOdd++;
                                    c.Archived = c.Archived || archived.Contains("local_" + id);
                                    string tp = null;
                                    if (c.CliSessionId != null) transcripts.TryGetValue(c.CliSessionId, out tp);
                                    c.TranscriptPath = tp;
                                    c.TranscriptExists = tp != null;
                                    c.TranscriptSize = tp != null ? SafeLength(tp) : 0;
                                    c.Scratch = Regex.IsMatch(c.Cwd, @"[\\/]scratch-workspaces[\\/]", RegexOptions.IgnoreCase);
                                    acct.Cards.Add(c);
                                }
                                catch (Exception e)
                                {
                                    acct.Cards.Add(new Card { Id = id, Title = fn, Broken = e.Message });
                                }
                                continue;
                            }
                            m = TombRe.Match(fn);
                            if (m.Success)
                            {
                                long ts = 0;
                                try { long.TryParse(File.ReadAllText(f).Trim(), out ts); } catch { }
                                acct.Tombstones[m.Groups[1].Value.ToLowerInvariant()] = ts;
                            }
                        }
                        acct.Cards = acct.Cards.OrderByDescending(c => c.LastActivityAt).ToList();
                        list.Add(acct);
                    }
                }
            }

            // A few odd cards can be mid-write; many mean the layout has changed.
            if (cardsSeen > 0 && cardsOdd > Math.Max(2, cardsSeen / 5))
                issues.Add(L.F("{0} 張卡片的格式和預期不同", cardsOdd));
            FormatIssues = issues;

            var used = new HashSet<string>(list.SelectMany(a => a.Cards).Where(c => c.CliSessionId != null).Select(c => c.CliSessionId), StringComparer.OrdinalIgnoreCase);
            OrphanIds = transcripts.Keys.Where(k => UuidRe.IsMatch(k) && !used.Contains(k)).ToList();
            lastTranscripts = transcripts;

            foreach (var a in list)
                a.OtherOrgsInUse = list.Any(o => o != a && o.AccountUuid == a.AccountUuid && o.Cards.Count > 0);
            Func<Account, bool> held = x => x.Cards.Count > 0 || x.Tombstones.Count > 0;
            foreach (var a in list)
                a.InUse = held(a) || (a.Current && !list.Any(o => o != a && o.AccountUuid == a.AccountUuid && held(o)));

            // Where each session lives, for "also in" dots and stale copies.
            var where = new Dictionary<string, List<KeyValuePair<Account, Card>>>();
            foreach (var a in list)
                foreach (var c in a.Cards.Where(c => c.Broken == null))
                {
                    if (!where.ContainsKey(c.Id)) where[c.Id] = new List<KeyValuePair<Account, Card>>();
                    where[c.Id].Add(new KeyValuePair<Account, Card>(a, c));
                }
            foreach (var a in list)
                foreach (var c in a.Cards.Where(c => c.Broken == null))
                {
                    var others = where[c.Id].Where(p => p.Key != a).ToList();
                    c.AlsoIn = others.Select(p => p.Key.Key).ToList();
                    c.Stale = others.Any(p => IsBehind(c, p.Value));
                }

            Accounts = list
                .OrderByDescending(a => a.Cards.Count > 0)
                .ThenByDescending(a => a.Current)
                .ThenByDescending(a => a.Cards.Count)
                .ThenBy(a => a.Key, StringComparer.Ordinal)
                .ToList();
        }

        static long SafeLength(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }

        static string ArchiveIndexVersion(string dir)
        {
            try
            {
                var idx = Json.ParseObject(File.ReadAllText(Path.Combine(dir, ArchiveIdx), Fs.Utf8));
                return idx.ContainsKey("v") ? Convert.ToString(idx["v"], System.Globalization.CultureInfo.InvariantCulture) : null;
            }
            catch { return null; }
        }

        static HashSet<string> ReadArchiveIndex(string dir)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var idx = Json.ParseObject(File.ReadAllText(Path.Combine(dir, ArchiveIdx), Fs.Utf8));
                var arr = idx.ContainsKey("archived") ? idx["archived"] as System.Collections.IEnumerable : null;
                if (arr != null) foreach (var x in arr) set.Add(Convert.ToString(x));
            }
            catch { }
            return set;
        }

        // Which copy is newer. lastFocusedAt is ignored on purpose: just clicking
        // a session changes it, without any new conversation.
        public static int Compare(Card a, Card b)
        {
            int r = a.LastActivityAt.CompareTo(b.LastActivityAt);
            if (r != 0) return r;
            r = a.CompletedTurns.CompareTo(b.CompletedTurns);
            if (r != 0) return r;
            return a.Mtime.CompareTo(b.Mtime);
        }

        // Whether `copy` misses conversation that `other` has. File times don't count:
        // the app rewrites a card (focus time, connectors) without any new turns,
        // so two copies of the same conversation must not look "older" because of that.
        public static bool IsBehind(Card copy, Card other)
        {
            return other.LastActivityAt > copy.LastActivityAt
                || (other.LastActivityAt == copy.LastActivityAt && other.CompletedTurns > copy.CompletedTurns);
        }

        public Account Get(string key)
        {
            var a = Accounts.FirstOrDefault(x => x.Key == key);
            if (a == null) throw new InvalidOperationException(L.F("找不到帳號資料夾：{0}", key));
            return a;
        }

        public string CardPath(Account a, string id)
        {
            if (!UuidRe.IsMatch(id ?? "")) throw new InvalidOperationException(L.T("session 編號不正確"));
            return Path.Combine(a.Dir, "local_" + id + ".json");
        }

        // File errors (locked by Claude, antivirus, permissions) skip one item instead of the batch.
        static bool IsFileError(Exception e) { return e is IOException || e is UnauthorizedAccessException; }

        static void CheckIds(IEnumerable<string> ids)
        {
            var list = ids == null ? new List<string>() : ids.ToList();
            if (list.Count == 0) throw new InvalidOperationException(L.T("沒有選任何 session"));
            foreach (var id in list) if (!UuidRe.IsMatch(id ?? "")) throw new InvalidOperationException(L.F("session 編號不正確：{0}", id));
        }

        // Full card for the detail view.
        public Dictionary<string, object> ReadRaw(Account a, string id)
        {
            using (var fs = new FileStream(CardPath(a, id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs, Fs.Utf8)) return Json.ParseObject(r.ReadToEnd());
        }

        // ---------- Claude running? ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static string ProcessPath(Process p)
        {
            try
            {
                IntPtr h = OpenProcess(0x1000, false, p.Id);
                if (h == IntPtr.Zero) return null;
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
                }
                finally { CloseHandle(h); }
            }
            catch { return null; }
        }

        // The desktop app and its bundled CLI are both claude.exe; tell them apart by path.
        public static bool IsDesktopPath(string path)
        {
            return path != null && !Regex.IsMatch(path, @"[\\/]claude-code(-vm)?[\\/]", RegexOptions.IgnoreCase);
        }

        public static bool DesktopRunning()
        {
            if (Demo) return false;
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                try
                {
                    IntPtr h = OpenProcess(0x1000, false, p.Id);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(h, 0, sb, ref size))
                        {
                            string path = sb.ToString();
                            if (!Regex.IsMatch(path, @"[\\/]claude-code(-vm)?[\\/]", RegexOptions.IgnoreCase)) return true;
                        }
                    }
                    finally { CloseHandle(h); }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        // ---------- writing ----------

        void SetArchived(Journal j, Account a, string id, bool archived)
        {
            string p = Path.Combine(a.Dir, ArchiveIdx);
            Dictionary<string, object> idx;
            try { idx = Json.ParseObject(File.ReadAllText(p, Fs.Utf8)); }
            catch { idx = new Dictionary<string, object> { { "v", 1 } }; }
            var list = new List<object>();
            if (idx.ContainsKey("archived") && idx["archived"] is System.Collections.IEnumerable)
                foreach (var x in (System.Collections.IEnumerable)idx["archived"]) list.Add(x);
            string name = "local_" + id;
            bool has = list.Any(x => string.Equals(Convert.ToString(x), name, StringComparison.OrdinalIgnoreCase));
            if (archived == has) return;
            if (archived) list.Add(name);
            else list = list.Where(x => !string.Equals(Convert.ToString(x), name, StringComparison.OrdinalIgnoreCase)).ToList();
            idx["archived"] = list;
            j.Write(p, Json.Write(idx));
        }

        string CopyOne(Journal j, Account src, Account dst, string id, bool overwrite)
        {
            var card = src.Find(id);
            if (card == null) throw new SkipException(L.T("來源帳號裡找不到這張卡片"));
            var existing = dst.Find(id);
            if (existing != null && !overwrite && !IsBehind(existing, card))
                throw new SkipException(L.T("目標已有相同或較新的版本"), true);
            j.Copy(CardPath(src, id), CardPath(dst, id));
            if (dst.Tombstones.ContainsKey(id)) j.Remove(Path.Combine(dst.Dir, "deleted_" + id));
            if (card.Archived) SetArchived(j, dst, id, true);
            return existing != null ? "updated" : "added";
        }

        void RemoveOne(Journal j, Account a, string id)
        {
            if (a.Find(id) == null) throw new SkipException(L.T("這個帳號裡沒有這張卡片"));
            j.Remove(CardPath(a, id));
            // Same tombstone the app writes when you delete a session yourself.
            j.Write(Path.Combine(a.Dir, "deleted_" + id), Fs.NowMs().ToString());
            SetArchived(j, a, id, false);
        }

        public OpResult Transfer(string from, string to, IList<string> ids, bool move, bool overwrite)
        {
            CheckIds(ids);
            Scan();
            Guard();
            var src = Get(from);
            var dst = Get(to);
            if (src == dst) throw new InvalidOperationException(L.T("來源和目標是同一個帳號"));
            var j = new Journal(HistoryDir, L.F(move ? "搬移 {0} 個 session：{1} → {2}" : "複製 {0} 個 session：{1} → {2}", ids.Count, src.Name, dst.Name), move ? "move" : "copy");
            var r = new OpResult();
            try
            {
                foreach (var id in ids)
                {
                    var c = src.Find(id);
                    var item = new Item { Id = id, Title = c != null ? c.Title : id, From = from, To = to };
                    try
                    {
                        item.Type = CopyOne(j, src, dst, id, overwrite);
                        if (move) RemoveOne(j, src, id);
                        r.Done.Add(item);
                    }
                    catch (SkipException e)
                    {
                        item.Reason = e.Message;
                        item.CanOverwrite = e.CanOverwrite;
                        r.Skipped.Add(item);
                    }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            return r;
        }

        public OpResult Remove(string from, IList<string> ids)
        {
            CheckIds(ids);
            Scan();
            Guard();
            var a = Get(from);
            var j = new Journal(HistoryDir, L.F("從 {0} 移除 {1} 個 session", a.Name, ids.Count), "remove");
            var r = new OpResult();
            try
            {
                foreach (var id in ids)
                {
                    var c = a.Find(id);
                    var item = new Item { Id = id, Title = c != null ? c.Title : id, From = from, Type = "removed" };
                    try { RemoveOne(j, a, id); r.Done.Add(item); }
                    catch (SkipException e) { item.Reason = e.Message; r.Skipped.Add(item); }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            return r;
        }

        // Full sync: every session ends up in every chosen account, newest copy wins.
        public SyncPlan PlanSync(SyncOptions o)
        {
            Scan();
            var chosen = o.Targets.Select(Get).ToList();
            if (chosen.Count < 2) throw new InvalidOperationException(L.T("至少要選兩個帳號"));
            var newest = new Dictionary<string, KeyValuePair<Account, Card>>();
            foreach (var a in chosen)
                foreach (var c in a.Cards.Where(c => c.Broken == null))
                {
                    KeyValuePair<Account, Card> cur;
                    if (!newest.TryGetValue(c.Id, out cur) || Compare(c, cur.Value) > 0)
                        newest[c.Id] = new KeyValuePair<Account, Card>(a, c);
                }
            var plan = new SyncPlan();
            foreach (var kv in newest)
            {
                var src = kv.Value.Key;
                var card = kv.Value.Value;
                foreach (var dst in chosen)
                {
                    if (dst == src) continue;
                    var have = dst.Find(kv.Key);
                    var item = new Item { Id = kv.Key, Title = card.Title, From = src.Key, To = dst.Key };
                    if (have != null)
                    {
                        if (IsBehind(have, card)) { item.Type = "update"; plan.Ops.Add(item); }
                        continue;
                    }
                    if (o.RespectTombstones && dst.Tombstones.ContainsKey(kv.Key)) { item.Reason = L.T("在這個帳號被刪除過"); plan.Skipped.Add(item); continue; }
                    if (!o.IncludeArchived && card.Archived) { item.Reason = L.T("已封存"); plan.Skipped.Add(item); continue; }
                    if (o.SkipMissingTranscript && !card.TranscriptExists) { item.Reason = L.T("對話檔已遺失，搬過去也接不回"); plan.Skipped.Add(item); continue; }
                    item.Type = "add";
                    plan.Ops.Add(item);
                }
            }
            plan.Ops = plan.Ops.OrderBy(x => x.To).ThenBy(x => x.Type).ThenBy(x => x.Title).ToList();
            return plan;
        }

        public OpResult ApplySync(IList<Item> ops, string label = null)
        {
            CheckIds(ops.Select(x => x.Id));
            Scan();
            Guard();
            var j = new Journal(HistoryDir, label ?? L.F("完整同步：{0} 項", ops.Count), "sync");
            var r = new OpResult();
            try
            {
                foreach (var op in ops)
                {
                    var item = new Item { Id = op.Id, Title = op.Title, From = op.From, To = op.To, Type = op.Type };
                    try
                    {
                        // Plans can go stale; re-check "newer" instead of forcing.
                        item.Type = CopyOne(j, Get(op.From), Get(op.To), op.Id, false);
                        r.Done.Add(item);
                    }
                    catch (SkipException e) { item.Reason = e.Message; r.Skipped.Add(item); }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            return r;
        }

        // ---------- permanent delete ----------

        // What a permanent delete of these sessions would remove.
        public List<PurgeTarget> PlanPurge(IList<string> ids)
        {
            CheckIds(ids);
            Scan();
            var transcripts = TranscriptIndex();
            string configDir = Path.GetDirectoryName(ProjectsDir);
            string projectsRoot = Path.GetFullPath(ProjectsDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string historyRoot = configDir == null ? null : Path.GetFullPath(Path.Combine(configDir, "file-history")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var targets = new List<PurgeTarget>();
            foreach (var id in ids.Distinct())
            {
                var own = Accounts.SelectMany(a => a.Cards.Where(c => c.Broken == null && c.Id == id)).ToList();
                if (own.Count == 0) continue;
                var t = new PurgeTarget { Id = id, Title = own[0].Title, LastActivityAt = own.Max(c => c.LastActivityAt) };
                targets.Add(t);
                // A card that can't be read may be mid-write by Claude; deciding around it could orphan it.
                if (Accounts.Any(a => a.Cards.Any(c => c.Broken != null && c.Id == id))) { t.Blocked = L.T("有帳號的這張卡片讀不了（可能正在寫入），請稍後再試"); continue; }
                var clis = new HashSet<string>(own.Select(c => c.CliSessionId).Where(x => !string.IsNullOrEmpty(x)), StringComparer.OrdinalIgnoreCase);
                // The transcript id comes from the card; only a plain UUID may become a path to delete.
                if (clis.Any(c => !UuidRe.IsMatch(c))) { t.Blocked = L.T("對話檔編號不正確，為了安全不刪除"); continue; }
                // Any card that points at the same transcript would be left broken, so it goes too.
                foreach (var a in Accounts)
                    foreach (var c in a.Cards.Where(c => c.Broken == null && (c.Id == id || (c.CliSessionId != null && clis.Contains(c.CliSessionId)))))
                    {
                        t.Cards.Add(new KeyValuePair<Account, string>(a, c.Id));
                        if (c.Id != id && !t.AlsoDeleted.Contains(c.Title)) t.AlsoDeleted.Add(c.Title);
                    }
                // Order matters: side folders first, the transcript last (see Purge).
                foreach (var cli in clis)
                {
                    if (historyRoot != null) t.Paths.Add(Path.Combine(historyRoot, cli));
                    string jsonl;
                    if (transcripts.TryGetValue(cli, out jsonl))
                    {
                        t.Paths.Add(Path.Combine(Path.GetDirectoryName(jsonl), cli));
                        t.Paths.Add(jsonl);
                        try { t.TranscriptWriteMs = Math.Max(t.TranscriptWriteMs, (long)(File.GetLastWriteTimeUtc(jsonl) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds); } catch { }
                    }
                }
                t.Paths = t.Paths.Select(Path.GetFullPath)
                    .Where(p => p.StartsWith(projectsRoot, StringComparison.OrdinalIgnoreCase) || (historyRoot != null && p.StartsWith(historyRoot, StringComparison.OrdinalIgnoreCase)))
                    .Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                t.Bytes = t.Paths.Sum(p => SizeOf(p));
            }
            return targets;
        }

        static long SizeOf(string p)
        {
            try
            {
                if (File.Exists(p)) return new FileInfo(p).Length;
                return new DirectoryInfo(p).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // Removes the sessions' cards from every account and deletes their
        // conversation files for good. The history keeps a record but cannot undo it.
        public OpResult Purge(IList<string> ids)
        {
            var plan = PlanPurge(ids);
            Guard();
            var j = new Journal(HistoryDir, L.F("永久刪除 {0} 個 session 的對話", plan.Count), "purge", false);
            var r = new OpResult();
            try
            {
                foreach (var t in plan)
                {
                    var item = new Item { Id = t.Id, Title = t.Title, Type = "purged" };
                    if (t.Blocked != null) { item.Reason = t.Blocked; r.Skipped.Add(item); continue; }
                    try
                    {
                        // Files first and the transcript last: if one is locked, the cards stay
                        // and still point at a transcript, so nothing is left half deleted.
                        foreach (var p in t.Paths)
                        {
                            if (File.Exists(p)) File.Delete(p);
                            else if (Directory.Exists(p)) Directory.Delete(p, true);
                            j.Purged.Add(p);
                        }
                        foreach (var kv in t.Cards)
                            if (kv.Key.Find(kv.Value) != null) RemoveOne(j, kv.Key, kv.Value);
                        r.Bytes += t.Bytes;
                        r.Done.Add(item);
                    }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            cache.Clear();
            return r;
        }

        // Everything one account holds. Sessions only it has are deleted for good;
        // sessions other accounts also have are only taken off this account.
        public AccountPurgePlan PlanAccountPurge(string key)
        {
            Scan();
            var a = Get(key);
            var live = a.Cards.Where(c => c.Broken == null).ToList();
            // Shared means another account has the same session id (readable or not)
            // or a card pointing at the same transcript; those only leave this account.
            var others = Accounts.Where(o => o != a).SelectMany(o => o.Cards).ToList();
            var otherIds = new HashSet<string>(others.Select(c => c.Id));
            var otherClis = new HashSet<string>(others.Where(c => c.CliSessionId != null).Select(c => c.CliSessionId), StringComparer.OrdinalIgnoreCase);
            Func<Card, bool> shared = c => otherIds.Contains(c.Id) || (c.CliSessionId != null && otherClis.Contains(c.CliSessionId));
            return new AccountPurgePlan
            {
                Account = a,
                Exclusive = live.Where(c => !shared(c)).ToList(),
                Shared = live.Where(shared).ToList(),
            };
        }

        public OpResult PurgeAccount(string key)
        {
            var p = PlanAccountPurge(key);
            var r = new OpResult();
            if (p.Shared.Count > 0)
            {
                var removed = Remove(key, p.Shared.Select(c => c.Id).ToList());
                r.Done.AddRange(removed.Done);
                r.Skipped.AddRange(removed.Skipped);
            }
            if (p.Exclusive.Count > 0)
            {
                var purged = Purge(p.Exclusive.Select(c => c.Id).ToList());
                r.Done.AddRange(purged.Done);
                r.Skipped.AddRange(purged.Skipped);
                r.Bytes = purged.Bytes;
                r.BatchId = purged.BatchId;
            }
            return r;
        }

        public List<Dictionary<string, object>> History() { return Journal.List(HistoryDir); }

        // ---------- conversations no list points at ----------

        static long ToMs(DateTime utc) { return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        public List<Orphan> Orphans()
        {
            Scan();
            var list = new List<Orphan>();
            foreach (var id in OrphanIds)
            {
                string p;
                if (lastTranscripts.TryGetValue(id, out p)) list.Add(ReadOrphan(id, p));
            }
            return list.OrderByDescending(o => o.LastWriteMs).ToList();
        }

        // Title, folder and start time from the first lines of a transcript.
        public static Orphan ReadOrphan(string id, string path)
        {
            var o = new Orphan { Id = id, Path = path };
            try
            {
                var fi = new FileInfo(path);
                o.Size = fi.Length;
                o.LastWriteMs = ToMs(fi.LastWriteTimeUtc);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Fs.Utf8))
                {
                    string line;
                    int n = 0;
                    long read = 0;
                    while ((line = r.ReadLine()) != null && n++ < 300 && read < 8000000)
                    {
                        read += line.Length;
                        if (line.Length > 400000) continue;
                        Dictionary<string, object> j;
                        try { j = Json.ParseObject(line); } catch { continue; }
                        if (o.Cwd == null) o.Cwd = Json.Str(j, "cwd");
                        if (o.FirstMs == 0)
                        {
                            DateTime t;
                            string ts = Json.Str(j, "timestamp");
                            if (ts != null && DateTime.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t))
                                o.FirstMs = ToMs(t);
                        }
                        if (o.Title == null && Json.Str(j, "type") == "user" && !Json.Bool(j, "isMeta")) o.Title = FirstText(j);
                        if (o.Title != null && o.Cwd != null && o.FirstMs != 0) break;
                    }
                }
            }
            catch { }
            return o;
        }

        static string FirstText(Dictionary<string, object> j)
        {
            var msg = j.ContainsKey("message") ? j["message"] as Dictionary<string, object> : null;
            if (msg == null) return null;
            object c = msg.ContainsKey("content") ? msg["content"] : null;
            string s = c as string;
            if (s == null && c is System.Collections.IEnumerable)
                foreach (var part in (System.Collections.IEnumerable)c)
                {
                    var d = part as Dictionary<string, object>;
                    if (d != null && Json.Str(d, "type") == "text") { s = Json.Str(d, "text"); break; }
                }
            // Command and system wrappers start with a tag; the first real prompt makes a better title.
            if (string.IsNullOrWhiteSpace(s) || s.TrimStart().StartsWith("<")) return null;
            s = Regex.Replace(s.Trim(), @"\s+", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "…" : s;
        }

        // Transcript, its side folder and its file-history, only inside Claude's own folders.
        List<string> TranscriptPaths(string cli, string jsonl)
        {
            string configDir = Path.GetDirectoryName(ProjectsDir);
            string projectsRoot = Path.GetFullPath(ProjectsDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string historyRoot = configDir == null ? null : Path.GetFullPath(Path.Combine(configDir, "file-history")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var paths = new List<string>();
            if (historyRoot != null) paths.Add(Path.Combine(historyRoot, cli));
            paths.Add(Path.Combine(Path.GetDirectoryName(jsonl), cli));
            paths.Add(jsonl);
            return paths.Select(Path.GetFullPath)
                .Where(p => p.StartsWith(projectsRoot, StringComparison.OrdinalIgnoreCase) || (historyRoot != null && p.StartsWith(historyRoot, StringComparison.OrdinalIgnoreCase)))
                .Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        }

        string OrphanPath(string id, Item item)
        {
            string jsonl;
            if (!UuidRe.IsMatch(id ?? "") || !OrphanIds.Contains(id) || !lastTranscripts.TryGetValue(id, out jsonl))
            {
                item.Reason = L.T("已經不是沒在清單裡的對話");
                return null;
            }
            if (Fs.NowMs() - ToMs(File.GetLastWriteTimeUtc(jsonl)) < BusyMs)
            {
                item.Reason = L.T("最近 3 分鐘內還有寫入，可能正在使用");
                return null;
            }
            return jsonl;
        }

        public OpResult PurgeOrphans(IList<string> cliIds)
        {
            Scan();
            Guard();
            var j = new Journal(HistoryDir, L.F("永久刪除 {0} 個沒在清單裡的對話", cliIds.Count), "purge", false);
            var r = new OpResult();
            try
            {
                foreach (var id in cliIds)
                {
                    var item = new Item { Id = id, Title = id, Type = "purged" };
                    string jsonl = OrphanPath(id, item);
                    if (jsonl == null) { r.Skipped.Add(item); continue; }
                    item.Title = ReadOrphan(id, jsonl).Title ?? id;
                    try
                    {
                        foreach (var p in TranscriptPaths(id, jsonl))
                        {
                            r.Bytes += SizeOf(p);
                            if (File.Exists(p)) File.Delete(p);
                            else if (Directory.Exists(p)) Directory.Delete(p, true);
                            j.Purged.Add(p);
                        }
                        r.Done.Add(item);
                    }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            return r;
        }

        // Fields that belong to one live session and must not be carried into a new card.
        static readonly string[] SessionOnlyKeys = {
            "bridgeSessionIds", "postTurnSummary", "postTurnSummaryFor", "lastAssistantUuid", "turnWrapUp",
            "interruptedAwaitingPerson", "latestUserFrameAt", "lastTurnEdgeAt", "titleTurn", "spawnSeed",
            "lastSpawnRootDetected", "alwaysAllowedReasons", "sessionPermissionUpdates", "steeredByRemoteClient" };

        // Experimental: gives orphan conversations a sidebar card in one account. The account's
        // newest card is the template for the app's own settings; session fields come from the transcript.
        public OpResult Recover(IList<string> cliIds, string toKey)
        {
            Scan();
            Guard();
            var dst = Get(toKey);
            var template = dst.Cards.Where(c => c.Broken == null).OrderByDescending(c => c.LastActivityAt).FirstOrDefault();
            Account templateAccount = dst;
            if (template == null)
                foreach (var a in Accounts)
                {
                    var c = a.Cards.Where(x => x.Broken == null).OrderByDescending(x => x.LastActivityAt).FirstOrDefault();
                    if (c != null && (template == null || c.LastActivityAt > template.LastActivityAt)) { template = c; templateAccount = a; }
                }
            if (template == null) throw new InvalidOperationException(L.T("需要至少一張現有的卡片當範本"));
            var raw = ReadRaw(templateAccount, template.Id);
            var j = new Journal(HistoryDir, L.F("把 {0} 個對話加進 {1}", cliIds.Count, dst.Name), "recover");
            var r = new OpResult();
            try
            {
                foreach (var id in cliIds)
                {
                    var item = new Item { Id = id, Title = id, To = toKey, Type = "added" };
                    string jsonl = OrphanPath(id, item);
                    if (jsonl == null) { r.Skipped.Add(item); continue; }
                    var o = ReadOrphan(id, jsonl);
                    item.Title = o.Title ?? id;
                    if (string.IsNullOrEmpty(o.Cwd)) { item.Reason = L.T("對話檔裡找不到工作資料夾"); r.Skipped.Add(item); continue; }
                    var card = new Dictionary<string, object>(raw);
                    foreach (var k in SessionOnlyKeys) card.Remove(k);
                    string newId = Guid.NewGuid().ToString();
                    card["sessionId"] = "local_" + newId;
                    card["cliSessionId"] = id;
                    card["title"] = o.Title ?? L.T("(未命名)");
                    card["titleSource"] = "auto";
                    card["cwd"] = o.Cwd;
                    card["originCwd"] = o.Cwd;
                    if (card.ContainsKey("gitAnchorsFolderRealpath")) card["gitAnchorsFolderRealpath"] = o.Cwd;
                    card["createdAt"] = o.FirstMs > 0 ? o.FirstMs : o.LastWriteMs;
                    card["lastActivityAt"] = o.LastWriteMs;
                    card["lastFocusedAt"] = o.LastWriteMs;
                    card["completedTurns"] = CountTurns(jsonl);
                    card["isArchived"] = false;
                    try
                    {
                        j.Write(CardPath(dst, newId), Json.Write(card));
                        item.Id = newId;
                        r.Done.Add(item);
                    }
                    catch (Exception e)
                    {
                        if (!IsFileError(e)) throw;
                        item.Reason = L.F("檔案操作失敗：{0}", e.Message);
                        r.Skipped.Add(item);
                    }
                }
            }
            finally { r.BatchId = j.Commit(r.Done.Count, r.Skipped.Count); cache.Clear(); }
            return r;
        }

        // Prompts typed by the user. Tool results, meta lines and command/system wrappers
        // (content starting with a tag) are "user" lines too and don't count.
        static int CountTurns(string path)
        {
            int n = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Fs.Utf8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Contains("\"type\":\"user\"") && !line.Contains("\"tool_result\"") && !line.Contains("\"isMeta\":true")
                            && !line.Contains("\"content\":\"<")) n++;
                }
            }
            catch { }
            return n;
        }

        // Undo writes its own batch, so it can be undone again (redo). Undoing that batch
        // puts the original back and needs no further entry. The result carries "redoId"
        // (the new batch) when there is one.
        public Dictionary<string, object> Undo(string id, bool force = false)
        {
            Scan();
            Guard();
            var target = Journal.Load(HistoryDir, id);
            if (Json.Str(target, "kind") == "undo")
            {
                var back = Journal.Undo(HistoryDir, id, force);
                cache.Clear();
                return back;
            }
            var redo = new Journal(HistoryDir, L.F("復原：{0}", Json.Str(target, "label")), "undo")
            {
                Reverts = id,
                RevertsLabel = Json.Str(target, "label"),
            };
            Dictionary<string, object> m = null;
            string redoId = null;
            try { m = Journal.Undo(HistoryDir, id, force, redo); }
            finally
            {
                redoId = redo.Commit(0, 0);
                cache.Clear();
            }
            m["redoId"] = redoId;
            return m;
        }
    }
}
