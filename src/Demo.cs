// Made-up accounts and sessions, for trying the app and for screenshots.
// Written to a temp folder; the real Claude folders are never read in demo mode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SessionSync
{
    static class Demo
    {
        class Acct { public string Id, Org, Email, Person, OrgName; }

        static readonly Acct[] Accounts =
        {
            new Acct { Id = "a11ce000-0000-4000-8000-000000000001", Org = "0a110000-0000-4000-8000-0000000000a1",
                       Email = "alice@example.com", Person = "Alice Chen", OrgName = "alice@example.com's Organization" },
            new Acct { Id = "b0b00000-0000-4000-8000-000000000002", Org = "0b0b0000-0000-4000-8000-0000000000b2",
                       Email = "alice.work@example.com", Person = "Alice Chen", OrgName = "Northwind Labs" },
            new Acct { Id = "c0ffee00-0000-4000-8000-000000000003", Org = "0c0f0000-0000-4000-8000-0000000000c3",
                       Email = "alice.side@example.com", Person = "Alice", OrgName = "alice.side@example.com's Organization" },
        };

        // title, folder ("scratch" = an app-made scratch folder), minutes since last activity, turns,
        // where it lives ("A", "AB", ...; lowercase = an older copy), extras: d = deleted in A, x = archived, m = transcript missing
        static readonly object[][] Sessions =
        {
            new object[] { "Fix flaky checkout test", @"D:\code\shop-web", 12, 34, "Ab", "" },
            new object[] { "Refactor auth middleware", @"D:\code\api-gateway", 45, 58, "B", "" },
            new object[] { "Write release notes for v2.3", @"D:\code\shop-web", 130, 9, "A", "" },
            new object[] { "Investigate memory leak in queue worker", @"D:\code\queue-worker", 320, 112, "BC", "" },
            new object[] { "Add dark mode to the settings page", @"D:\code\shop-web", 1500, 41, "A", "" },
            new object[] { "Migrate CI to GitHub Actions", @"D:\code\infra", 1700, 27, "ABC", "" },
            new object[] { "Draft blog post: caching tips", "scratch", 2900, 6, "C", "" },
            new object[] { "Plan a weekend trip", "scratch", 4300, 4, "C", "d" },
            new object[] { "Budget spreadsheet formulas", @"D:\personal\budget", 5800, 15, "C", "" },
            new object[] { "Translate docs to Japanese", @"D:\code\docs-site", 7200, 22, "A", "" },
            new object[] { "Speed up analytics SQL query", @"D:\code\analytics", 8700, 31, "B", "" },
            new object[] { "Kotlin coroutine questions", @"D:\code\android-app", 11500, 12, "AC", "" },
            new object[] { "Set up pre-commit hooks", @"D:\code\infra", 17300, 8, "A", "x" },
            new object[] { "Old prototype notes", @"D:\code\prototype", 57600, 3, "B", "m" },
        };

        public static Store Create(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            string dataRoot = Path.Combine(dir, "claude_demo");
            string root = Path.Combine(dataRoot, "claude-code-sessions");
            string projects = Path.Combine(dir, ".claude", "projects");
            string data = Path.Combine(dir, "data");
            long now = Fs.NowMs();

            foreach (var a in Accounts) Directory.CreateDirectory(Path.Combine(root, a.Id, a.Org));
            // The app also makes folders for account/org pairs that were never used.
            string unused = Path.Combine(root, Accounts[0].Id, Accounts[1].Org);
            Directory.CreateDirectory(unused);
            File.WriteAllText(Path.Combine(unused, "scheduled-tasks.json"), "{\"scheduledTasks\":[]}");
            File.WriteAllText(Path.Combine(dataRoot, "config.json"), "{\"lastKnownAccountUuid\":\"" + Accounts[0].Id + "\"}");

            for (int i = 0; i < Sessions.Length; i++)
            {
                var s = Sessions[i];
                string title = (string)s[0], folder = (string)s[1], where = (string)s[4], extras = (string)s[5];
                long last = now - (int)s[2] * 60000L;
                int turns = (int)s[3];
                string id = string.Format("5e550000-0000-4000-8000-{0:x12}", i + 1);
                string cli = string.Format("c1100000-0000-4000-8000-{0:x12}", i + 1);
                bool archived = extras.Contains("x");

                foreach (char w in where)
                {
                    var a = Accounts["abc".IndexOf(char.ToLowerInvariant(w))];
                    bool older = char.IsLower(w);
                    string cwd = folder == "scratch"
                        ? Path.Combine(dataRoot, "scratch-workspaces", a.Id, a.Org, string.Format("scratch-2026-10-{0:00}-{1:x6}", 20 + i % 9, i * 7919 % 0xffffff))
                        : folder;
                    var card = new Dictionary<string, object>
                    {
                        { "sessionId", "local_" + id },
                        { "cliSessionId", cli },
                        { "title", title },
                        { "cwd", cwd },
                        { "originCwd", cwd },
                        { "model", i % 3 == 1 ? "claude-sonnet-5-5" : "claude-opus-5-5" },
                        { "effort", "high" },
                        { "createdAt", last - turns * 240000L },
                        { "lastActivityAt", older ? last - 2 * 86400000L : last },
                        { "lastFocusedAt", last },
                        { "completedTurns", older ? turns - 9 : turns },
                        { "isArchived", archived },
                        { "permissionMode", "default" },
                        { "remoteMcpServersConfig", new object[] { new Dictionary<string, object> { { "name", "GitHub" } } } },
                    };
                    string dirA = Path.Combine(root, a.Id, a.Org);
                    File.WriteAllText(Path.Combine(dirA, "local_" + id + ".json"), Json.Write(card));
                    if (archived) File.WriteAllText(Path.Combine(dirA, "archived-sessions.idx"), "{\"v\":1,\"archived\":[\"local_" + id + "\"]}");
                }
                if (extras.Contains("d"))
                    File.WriteAllText(Path.Combine(root, Accounts[0].Id, Accounts[0].Org, "deleted_" + id), (now - 86400000L).ToString());

                if (!extras.Contains("m"))
                {
                    string project = Path.Combine(projects, Regex.Replace(folder == "scratch" ? "scratch-" + i : folder, "[^A-Za-z0-9]", "-"));
                    Directory.CreateDirectory(project);
                    var sb = new StringBuilder();
                    for (int t = 0; t < turns; t++)
                        sb.Append("{\"type\":\"user\",\"message\":{\"content\":\"demo turn ").Append(t).Append("\"},\"pad\":\"").Append('x', 2400).Append("\"}\n");
                    string jsonl = Path.Combine(project, cli + ".jsonl");
                    File.WriteAllText(jsonl, sb.ToString());
                    // Like a real transcript, last written at the session's last activity.
                    File.SetLastWriteTimeUtc(jsonl, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(last));
                }
            }

            // Conversations no account's list points at, e.g. from the command line.
            var loose = new[] { new[] { "Explain this regex for log parsing", @"D:\code\scripts", "4" }, new[] { "Benchmark three JSON parsers", @"D:\code\bench", "9" } };
            for (int i = 0; i < loose.Length; i++)
            {
                string project = Path.Combine(projects, Regex.Replace(loose[i][1], "[^A-Za-z0-9]", "-"));
                Directory.CreateDirectory(project);
                long when = now - int.Parse(loose[i][2]) * 86400000L;
                string stamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(when).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                string jsonl = Path.Combine(project, string.Format("0c1a0000-0000-4000-8000-{0:x12}", i + 1) + ".jsonl");
                var line = new Dictionary<string, object>
                {
                    { "type", "user" }, { "cwd", loose[i][1] }, { "timestamp", stamp },
                    { "message", new Dictionary<string, object> { { "role", "user" }, { "content", loose[i][0] } } },
                };
                File.WriteAllText(jsonl, Json.Write(line) + "\n" + new string('x', 30000) + "\n");
                File.SetLastWriteTimeUtc(jsonl, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(when));
            }

            // Readable names, as if the app had learned them earlier.
            var known = new KnownNames();
            foreach (var a in Accounts)
            {
                known.Learn(a.Id, a.Email, a.Person);
                known.LearnOrg(a.Org, a.OrgName);
            }
            var settings = new Dictionary<string, object>();
            known.WriteTo(settings);
            Fs.AtomicWrite(Path.Combine(data, "settings.json"), Json.Write(settings));

            Store.Demo = true;
            return new Store(new List<string> { root }, projects, data) { ClaudeJsonPaths = new List<string>() };
        }
    }
}
