// Readable names for account and org folders, which Claude names only by UUID.
//
// Two local sources, both read-only:
//   * the desktop app's web cache (IndexedDB blob) holds the signed-in account:
//     uuid, email_address, full_name and its org memberships, in V8 serialization;
//   * ~/.claude.json holds the CLI login: accountUuid, emailAddress, organizationName.
// Each only knows one account at a time, so what we learn is remembered in
// settings.json and an account keeps its name after you switch away from it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SessionSync
{
    class KnownNames
    {
        public readonly Dictionary<string, string> Email = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Person = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Org = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static KnownNames From(Dictionary<string, object> settings)
        {
            var k = new KnownNames();
            object v;
            var known = settings.TryGetValue("known", out v) ? v as Dictionary<string, object> : null;
            if (known == null) return k;
            var accts = known.ContainsKey("accounts") ? known["accounts"] as Dictionary<string, object> : null;
            if (accts != null)
                foreach (var kv in accts)
                {
                    var a = kv.Value as Dictionary<string, object>;
                    if (Json.Str(a, "email") != null) k.Email[kv.Key] = Json.Str(a, "email");
                    if (Json.Str(a, "name") != null) k.Person[kv.Key] = Json.Str(a, "name");
                }
            var orgs = known.ContainsKey("orgs") ? known["orgs"] as Dictionary<string, object> : null;
            if (orgs != null)
                foreach (var kv in orgs) if (kv.Value is string) k.Org[kv.Key] = (string)kv.Value;
            return k;
        }

        public void WriteTo(Dictionary<string, object> settings)
        {
            var accts = new Dictionary<string, object>();
            foreach (var id in Email.Keys.Union(Person.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var a = new Dictionary<string, object>();
                string s;
                if (Email.TryGetValue(id, out s)) a["email"] = s;
                if (Person.TryGetValue(id, out s)) a["name"] = s;
                accts[id] = a;
            }
            settings["known"] = new Dictionary<string, object>
            {
                { "accounts", accts },
                { "orgs", Org.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
            };
        }

        bool Set(Dictionary<string, string> map, string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return false;
            string old;
            if (map.TryGetValue(key, out old) && old == value) return false;
            map[key] = value;
            return true;
        }

        public bool Learn(string accountUuid, string email, string person)
        {
            bool a = Set(Email, accountUuid, email);
            bool b = Set(Person, accountUuid, person);
            return a || b;
        }

        public bool LearnOrg(string orgUuid, string name) { return Set(Org, orgUuid, name); }
    }

    static class Names
    {
        static readonly Regex UuidRe = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        static readonly Regex MailRe = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        static readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // Returns true when something new was learned.
        public static bool Learn(KnownNames known, IEnumerable<string> claudeDataDirs, IEnumerable<string> claudeJsonPaths)
        {
            bool changed = false;
            foreach (var p in claudeJsonPaths)
            {
                try
                {
                    if (!File.Exists(p) || Unchanged(p)) continue;
                    var oa = Json.ParseObject(File.ReadAllText(p, Fs.Utf8));
                    var acct = oa.ContainsKey("oauthAccount") ? oa["oauthAccount"] as Dictionary<string, object> : null;
                    if (acct == null) continue;
                    changed |= known.Learn(Json.Str(acct, "accountUuid"), Json.Str(acct, "emailAddress"), Json.Str(acct, "displayName"));
                    changed |= known.LearnOrg(Json.Str(acct, "organizationUuid"), Json.Str(acct, "organizationName"));
                }
                catch { }
            }
            foreach (var dataDir in claudeDataDirs)
            {
                string blobs = Path.Combine(dataDir, "IndexedDB", "https_claude.ai_0.indexeddb.blob");
                if (!Directory.Exists(blobs)) continue;
                foreach (var f in Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length > 32 * 1024 * 1024 || Unchanged(f)) continue;
                        changed |= ReadBlob(File.ReadAllBytes(f), known);
                    }
                    catch { }
                }
            }
            return changed;
        }

        static bool Unchanged(string path)
        {
            DateTime t = File.GetLastWriteTimeUtc(path), old;
            if (seen.TryGetValue(path, out old) && old == t) return true;
            seen[path] = t;
            return false;
        }

        // ---------- V8 value serialization, just enough to read strings ----------

        static byte[] Key(string name)
        {
            var b = new byte[name.Length + 2];
            b[0] = 0x22;                     // one-byte string tag
            b[1] = (byte)name.Length;        // varint length (all keys here are short)
            Latin1.GetBytes(name, 0, name.Length, b, 2);
            return b;
        }

        static readonly byte[] KEmail = Key("email_address"), KUuid = Key("uuid"), KFull = Key("full_name"),
            KDisplay = Key("display_name"), KMembers = Key("memberships"), KName = Key("name"), KOrg = Key("organization");

        static int Find(byte[] b, byte[] pat, int from, int to)
        {
            to = Math.Min(to, b.Length - pat.Length);
            for (int i = Math.Max(0, from); i <= to; i++)
            {
                int j = 0;
                while (j < pat.Length && b[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }

        static int FindLast(byte[] b, byte[] pat, int from, int to)
        {
            for (int i = Math.Min(to, b.Length - pat.Length); i >= Math.Max(0, from); i--)
            {
                int j = 0;
                while (j < pat.Length && b[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }

        static string Str(byte[] b, int i)
        {
            if (i < 0 || i >= b.Length) return null;
            byte tag = b[i++];
            int len = 0, shift = 0;
            while (i < b.Length && shift < 28)
            {
                byte x = b[i++];
                len |= (x & 0x7f) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            if (len <= 0 || len > 512 || i + len > b.Length) return null;
            if (tag == 0x22) return Latin1.GetString(b, i, len);              // one-byte (Latin-1)
            if (tag == 0x63 && len % 2 == 0) return Encoding.Unicode.GetString(b, i, len); // two-byte (UTF-16)
            if (tag == 0x53) return Encoding.UTF8.GetString(b, i, len);       // UTF-8
            return null;
        }

        static string ValueAfter(byte[] b, byte[] key, int from, int window)
        {
            int k = Find(b, key, from, from + window);
            return k < 0 ? null : Str(b, k + key.Length);
        }

        public static bool ReadBlob(byte[] b, KnownNames known)
        {
            bool changed = false;
            int pos = 0;
            while ((pos = Find(b, KEmail, pos, b.Length)) >= 0)
            {
                int at = pos + KEmail.Length;
                pos = at;
                string email = Str(b, at);
                int u = FindLast(b, KUuid, at - 160, at);
                string uuid = u < 0 ? null : Str(b, u + KUuid.Length);
                if (email == null || uuid == null || !MailRe.IsMatch(email) || !UuidRe.IsMatch(uuid)) continue;
                string person = ValueAfter(b, KFull, at, 300) ?? ValueAfter(b, KDisplay, at, 300);
                changed |= known.Learn(uuid, email, person);

                // Each membership is {organization: {id, uuid, name, ...}, ...}. Other cached
                // records (chats) also have uuid + name, so only read inside "organization".
                int m = Find(b, KMembers, at, at + 600);
                if (m < 0) continue;
                int scan = m, end = Math.Min(b.Length, m + 20000);
                int nextAccount = Find(b, KEmail, at, end);
                if (nextAccount > 0) end = nextAccount;
                for (int n = 0; n < 40; n++)
                {
                    int o = Find(b, KOrg, scan, end);
                    if (o < 0) break;
                    scan = o + KOrg.Length;
                    int ou = Find(b, KUuid, scan, scan + 48);
                    if (ou < 0) continue;
                    string orgUuid = Str(b, ou + KUuid.Length);
                    if (orgUuid == null || !UuidRe.IsMatch(orgUuid)) continue;
                    int nk = Find(b, KName, ou + KUuid.Length + 38, ou + KUuid.Length + 62);
                    string orgName = nk < 0 ? null : Str(b, nk + KName.Length);
                    if (orgName != null) changed |= known.LearnOrg(orgUuid, orgName);
                }
            }
            return changed;
        }
    }
}
