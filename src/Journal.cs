using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SessionSync
{
    static class Fs
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool MoveFileEx(string from, string to, int flags);
        const int MOVEFILE_REPLACE_EXISTING = 1, MOVEFILE_WRITE_THROUGH = 8;

        public static readonly Encoding Utf8 = new UTF8Encoding(false);

        // Raw Win32 calls need the \\?\ prefix to reach paths over MAX_PATH.
        static string Long(string p)
        {
            p = Path.GetFullPath(p);
            if (p.Length < 248 || p.StartsWith(@"\\?\")) return p;
            return p.StartsWith(@"\\") ? @"\\?\UNC\" + p.Substring(2) : @"\\?\" + p;
        }

        static void Replace(string tmp, string dst)
        {
            if (!MoveFileEx(Long(tmp), Long(dst), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                int err = Marshal.GetLastWin32Error();
                try { File.Delete(tmp); } catch { }
                throw new IOException(L.F("無法寫入 {0}（Windows 錯誤 {1}）", dst, err));
            }
        }

        static string Tmp(string dst) { return dst + ".sstmp" + Guid.NewGuid().ToString("N").Substring(0, 6); }

        public static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = Tmp(path);
            File.WriteAllText(tmp, text, Utf8);
            Replace(tmp, path);
        }

        public static void AtomicCopy(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string tmp = Tmp(dst);
            File.Copy(src, tmp, true);
            Replace(tmp, dst);
        }

        public static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }

    // Every change to Claude's files goes through a Journal. Before a path is
    // written or removed for the first time in a batch, its previous state is
    // saved, so the whole batch can be undone later. The manifest is rewritten as
    // the batch goes, so a crash half-way still leaves something to undo.
    class Journal
    {
        public readonly string Id, Dir, Label, Kind;
        public readonly bool Undoable;
        public readonly List<string> Purged = new List<string>();
        // Set on the batch an undo writes: the batch it reverted, so undoing it again is a redo.
        public string Reverts, RevertsLabel;
        readonly long createdAt = Fs.NowMs();
        readonly List<Dictionary<string, object>> entries = new List<Dictionary<string, object>>();
        readonly Dictionary<string, Dictionary<string, object>> byPath = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        // undoable = false records what changed without keeping backups (permanent delete).
        public Journal(string historyDir, string label, string kind, bool undoable = true)
        {
            Undoable = undoable;
            Id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Dir = Path.Combine(historyDir, Id);
            Label = label;
            Kind = kind;
        }

        // What the file holds now, so undo can tell whether someone changed it afterwards.
        static string Fingerprint(string path)
        {
            if (!File.Exists(path)) return "";
            using (var sha = System.Security.Cryptography.SHA1.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        Dictionary<string, object> Snapshot(string path)
        {
            path = Path.GetFullPath(path);
            Dictionary<string, object> e;
            if (byPath.TryGetValue(path, out e)) return e;
            e = new Dictionary<string, object>();
            e["path"] = path;
            e["existed"] = File.Exists(path);
            if (Undoable && File.Exists(path))
            {
                string rel = "files/" + entries.Count;
                Directory.CreateDirectory(Path.Combine(Dir, "files"));
                File.Copy(path, Path.Combine(Dir, rel), true);
                e["backup"] = rel;
            }
            entries.Add(e);
            byPath[path] = e;
            Save(true);
            return e;
        }

        void After(Dictionary<string, object> e)
        {
            if (Undoable) e["after"] = Fingerprint((string)e["path"]);
        }

        public void Write(string path, string text) { var e = Snapshot(path); Fs.AtomicWrite(path, text); After(e); }

        public void Copy(string src, string dst) { var e = Snapshot(dst); Fs.AtomicCopy(src, dst); After(e); }

        public void Remove(string path) { var e = Snapshot(path); if (File.Exists(path)) File.Delete(path); After(e); }

        void Save(bool incomplete, int done = 0, int skipped = 0)
        {
            var m = new Dictionary<string, object>();
            m["id"] = Id;
            m["label"] = Label;
            m["kind"] = Kind;
            m["createdAt"] = createdAt;
            m["undone"] = false;
            m["undoable"] = Undoable;
            if (Reverts != null) { m["reverts"] = Reverts; m["revertsLabel"] = RevertsLabel; }
            if (incomplete) m["incomplete"] = true;
            if (Purged.Count > 0) m["purged"] = Purged;
            m["summary"] = new Dictionary<string, object> { { "done", done }, { "skipped", skipped } };
            m["entries"] = entries;
            Fs.AtomicWrite(Path.Combine(Dir, "manifest.json"), Json.Write(m));
        }

        // A batch that changed nothing leaves no trace.
        public string Commit(int done, int skipped)
        {
            if (entries.Count == 0 && Purged.Count == 0)
            {
                try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
                return null;
            }
            Save(false, done, skipped);
            return Id;
        }

        public static List<Dictionary<string, object>> List(string historyDir)
        {
            var list = new List<Dictionary<string, object>>();
            if (!Directory.Exists(historyDir)) return list;
            foreach (var dir in Directory.GetDirectories(historyDir))
            {
                try { list.Add(Json.ParseObject(File.ReadAllText(Path.Combine(dir, "manifest.json"), Fs.Utf8))); }
                catch { /* batch folder without a manifest */ }
            }
            return list.OrderByDescending(m => Json.Long(m, "createdAt")).ToList();
        }

        public static List<Dictionary<string, object>> Entries(Dictionary<string, object> manifest)
        {
            var raw = manifest["entries"] as System.Collections.IEnumerable;
            var list = new List<Dictionary<string, object>>();
            if (raw != null) foreach (var e in raw) list.Add((Dictionary<string, object>)e);
            return list;
        }

        // Restores every path the batch touched to its state before the batch.
        // Refuses when a newer, still applied batch touched the same files, because
        // undoing out of order would silently drop that newer change. Files changed
        // since the batch by something else (Claude itself, usually) need force.
        public static Dictionary<string, object> Load(string historyDir, string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
                throw new InvalidOperationException(L.T("紀錄編號不正確"));
            return Json.ParseObject(File.ReadAllText(Path.Combine(historyDir, id, "manifest.json"), Fs.Utf8));
        }

        // redo: when given, every restore goes through it, so the undo itself can be undone.
        public static Dictionary<string, object> Undo(string historyDir, string id, bool force, Journal redo = null)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
                throw new InvalidOperationException(L.T("紀錄編號不正確"));
            string dir = Path.Combine(historyDir, id);
            string manifestPath = Path.Combine(dir, "manifest.json");
            var m = Json.ParseObject(File.ReadAllText(manifestPath, Fs.Utf8));
            if (Json.Bool(m, "undone")) throw new InvalidOperationException(L.T("這筆操作已經復原過了"));
            if (m.ContainsKey("undoable") && !Json.Bool(m, "undoable")) throw new InvalidOperationException(L.T("永久刪除無法復原"));

            var mine = new HashSet<string>(Entries(m).Select(e => (string)e["path"]), StringComparer.OrdinalIgnoreCase);
            long created = Json.Long(m, "createdAt");
            var blocking = List(historyDir)
                // A later undo entry doesn't block: it only records that something was reverted.
                .Where(o => !Json.Bool(o, "undone") && Json.Str(o, "id") != id && Json.Long(o, "createdAt") > created && Json.Str(o, "kind") != "undo")
                .Where(o => Entries(o).Any(e => mine.Contains((string)e["path"])))
                .ToList();
            if (blocking.Any(o => o.ContainsKey("undoable") && !Json.Bool(o, "undoable")))
                throw new InvalidOperationException(L.T("之後有「永久刪除」動過同一批檔案，這筆已經無法復原"));
            if (blocking.Count > 0)
                throw new InvalidOperationException(L.F("請先復原較新的操作：{0}", string.Join(L.T("、"), blocking.Select(o => Json.Str(o, "label")))));

            var list = Entries(m);
            if (!force)
            {
                var changed = list.Where(e => e.ContainsKey("after") && Fingerprint((string)e["path"]) != Json.Str(e, "after"))
                    .Select(e => (string)e["path"]).ToList();
                if (changed.Count > 0) throw new ChangedSinceException(changed);
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                string path = (string)e["path"];
                if (Json.Bool(e, "existed"))
                {
                    string backup = Json.Str(e, "backup");
                    if (backup == null) continue;
                    string from = Path.Combine(dir, backup.Replace('/', Path.DirectorySeparatorChar));
                    if (redo != null) redo.Copy(from, path); else Fs.AtomicCopy(from, path);
                }
                else if (redo != null) redo.Remove(path);
                else if (File.Exists(path)) File.Delete(path);
            }
            // Undoing an undo puts the batch it had reverted back in effect.
            string reverted = Json.Str(m, "reverts");
            if (reverted != null)
            {
                string other = Path.Combine(historyDir, reverted, "manifest.json");
                if (File.Exists(other))
                {
                    var o = Json.ParseObject(File.ReadAllText(other, Fs.Utf8));
                    o["undone"] = false;
                    o.Remove("undoneAt");
                    Fs.AtomicWrite(other, Json.Write(o));
                }
            }
            m["undone"] = true;
            m["undoneAt"] = Fs.NowMs();
            Fs.AtomicWrite(manifestPath, Json.Write(m));
            return m;
        }
    }

    // Undo found files that changed after the batch; the caller asks before forcing.
    class ChangedSinceException : InvalidOperationException
    {
        public readonly List<string> Paths;
        public ChangedSinceException(List<string> paths)
            : base(L.F("有 {0} 個檔案在這次操作之後又被改過", paths.Count)) { Paths = paths; }
    }
}
