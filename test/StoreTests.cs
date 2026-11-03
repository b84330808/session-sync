// Runs Store against a throwaway fake Claude folder. Never touches real data.
// Build and run: build.cmd test
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SessionSync.Tests
{
    static class StoreTests
    {
        static string tmp, root, projects;
        const string A = "11111111-1111-1111-1111-111111111111/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string B = "22222222-2222-2222-2222-222222222222/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        const string C = "33333333-3333-3333-3333-333333333333/cccccccc-cccc-cccc-cccc-cccccccccccc";
        static int pass;

        static string Cli(int n) { return string.Format("c1100000-0000-4000-8000-{0:D12}", n); }
        static string Id(int n) { return new string((char)('0' + n), 8) + "-0000-0000-0000-000000000000"; }
        static string Dir(string k) { return Path.Combine(root, k.Replace('/', '\\')); }
        static string CardFile(string k, int n) { return Path.Combine(Dir(k), "local_" + Id(n) + ".json"); }
        static string Tomb(string k, int n) { return Path.Combine(Dir(k), "deleted_" + Id(n)); }
        static bool Has(string k, int n) { return File.Exists(CardFile(k, n)); }
        static string Title(string k, int n) { return Json.Str(Json.ParseObject(File.ReadAllText(CardFile(k, n))), "title"); }
        static List<string> L(params string[] ids) { return ids.ToList(); }

        static void MakeCard(string k, int n, string extra = "", long? activity = null, string title = null)
        {
            Directory.CreateDirectory(Dir(k));
            // A big nested field like the real cards, to exercise the skipping reader.
            string json = "{\"sessionId\":\"local_" + Id(n) + "\",\"cliSessionId\":\"" + Cli(n) + "\",\"title\":\"" + (title ?? "S" + n) +
                "\",\"remoteMcpServersConfig\":[{\"name\":\"x\",\"tools\":[{\"d\":\"a \\\"quoted\\\" } ] { string\"}]}]," +
                "\"cwd\":\"D:\\\\x\",\"lastActivityAt\":" + (activity ?? 1000L * n) + ",\"completedTurns\":" + n + extra + "}";
            File.WriteAllText(CardFile(k, n), json);
        }

        static void Ok(bool cond, string what) { if (!cond) throw new Exception("FAILED: " + what); }

        // Every Chinese string in the source is a key of L's table with English and Japanese.
        static void TranslationsComplete()
        {
            Test("every UI string has English and Japanese", () =>
            {
                string src = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src"));
                var lit = new System.Text.RegularExpressions.Regex("\"((?:[^\"\\\\\\n]|\\\\.)*)\"");
                var letters = new System.Text.RegularExpressions.Regex("[一-鿿぀-ヺー-ヿ]");
                var missing = new List<string>();
                int checkedKeys = 0;
                foreach (var file in Directory.GetFiles(src, "*.cs"))
                {
                    if (Path.GetFileName(file) == "L.cs") continue;
                    foreach (var line in File.ReadAllLines(file))
                    {
                        if (line.TrimStart().StartsWith("//")) continue;
                        foreach (System.Text.RegularExpressions.Match m in lit.Matches(line))
                        {
                            string raw = m.Groups[1].Value;
                            if (!letters.IsMatch(raw)) continue;
                            string key = raw.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
                            checkedKeys++;
                            if (!SessionSync.L.Has(key, "en") || !SessionSync.L.Has(key, "ja")) missing.Add(Path.GetFileName(file) + ": " + raw);
                        }
                    }
                }
                Ok(checkedKeys > 150, "found the strings (" + checkedKeys + ")");
                Ok(missing.Count == 0, "missing translations:\n  " + string.Join("\n  ", missing.Distinct()));
                SessionSync.L.Lang = "en";
                Ok(SessionSync.L.F("已複製 {0} 個 session", 1) == "Copied 1 session" && SessionSync.L.F("已複製 {0} 個 session", 3) == "Copied 3 sessions", "English plural");
                SessionSync.L.Lang = "zh";
            });
        }

        // A V8-serialized account record shaped like the desktop app's cache entry.
        static byte[] FakeAccountBlob()
        {
            var b = new List<byte>();
            Action<string> one = s => { b.Add(0x22); b.Add((byte)s.Length); b.AddRange(System.Text.Encoding.GetEncoding(28591).GetBytes(s)); };
            Action<string> two = s => { var u = System.Text.Encoding.Unicode.GetBytes(s); b.Add(0x63); b.Add((byte)u.Length); b.AddRange(u); };
            b.AddRange(new byte[] { 0xff, 0x0f, 0x6f });
            one("tagged_id"); one("user_x");
            one("uuid"); one("11111111-1111-1111-1111-111111111111");
            one("email_address"); one("a@example.com");
            one("full_name"); two("王小明");
            one("memberships"); b.Add(0x61); b.Add(0x01);
            one("organization"); b.Add(0x6f); one("id"); b.Add(0x49); b.Add(0x08);
            one("uuid"); one("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            one("name"); one("Team X");
            // A cached chat with uuid + name must not be read as an org.
            one("conversations"); one("uuid"); one("cccccccc-cccc-cccc-cccc-cccccccccccc"); one("name"); one("my chat title");
            return b.ToArray();
        }

        static void Throws(Action a, string contains)
        {
            try { a(); }
            catch (Exception e)
            {
                if (e.Message.Contains(contains)) return;
                throw new Exception("wrong error: " + e.Message);
            }
            throw new Exception("expected error containing " + contains);
        }

        static void Test(string name, Action body)
        {
            body();
            pass++;
            Console.WriteLine("ok - " + name);
        }

        static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            tmp = Path.Combine(Path.GetTempPath(), "ss-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            root = Path.Combine(tmp, "claude_x", "claude-code-sessions");
            projects = Path.Combine(tmp, "projects");
            try
            {
                Directory.CreateDirectory(Path.Combine(projects, "p"));
                foreach (var n in new[] { 1, 2, 3, 4 }) File.WriteAllText(Path.Combine(projects, "p", Cli(n) + ".jsonl"), "{}\n");
                MakeCard(A, 1);
                MakeCard(A, 2);
                MakeCard(A, 3, ",\"isArchived\":true");
                MakeCard(A, 5);
                MakeCard(B, 2, "", 9999, "S2-newer");
                MakeCard(B, 4);
                File.WriteAllText(Path.Combine(Dir(A), "archived-sessions.idx"), "{\"v\":1,\"archived\":[\"local_" + Id(3) + "\"]}");
                File.WriteAllText(Tomb(B, 1), "123");
                Directory.CreateDirectory(Dir(C));
                File.WriteAllText(Path.Combine(tmp, "claude_x", "config.json"), "{\"lastKnownAccountUuid\":\"11111111-1111-1111-1111-111111111111\"}");

                // What the desktop app's web cache and the CLI login would hold.
                string blobDir = Path.Combine(tmp, "claude_x", "IndexedDB", "https_claude.ai_0.indexeddb.blob", "1", "00");
                Directory.CreateDirectory(blobDir);
                File.WriteAllBytes(Path.Combine(blobDir, "7"), FakeAccountBlob());
                string claudeJson = Path.Combine(tmp, "claude.json");
                File.WriteAllText(claudeJson, "{\"oauthAccount\":{\"accountUuid\":\"22222222-2222-2222-2222-222222222222\",\"emailAddress\":\"b@example.com\"," +
                    "\"displayName\":\"Bee\",\"organizationUuid\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"organizationName\":\"b@example.com's Organization\"}}");

                SessionSync.L.Lang = "zh";
                var store = new Store(new List<string> { root }, projects, Path.Combine(tmp, "data"));
                store.ClaudeJsonPaths = new List<string> { claudeJson };
                Run(store);
                TranslationsComplete();
                Console.WriteLine();
                Console.WriteLine(pass + " passed");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void Run(Store store)
        {
            Test("scan sees accounts, archive, transcripts, current", () =>
            {
                store.Scan();
                var a = store.Get(A);
                Ok(store.Accounts.Count == 3, "3 accounts");
                Ok(a.Current, "A current");
                Ok(a.Cards.Count == 4, "A has 4 cards");
                Ok(a.Find(Id(3)).Archived, "3 archived");
                Ok(!a.Find(Id(5)).TranscriptExists, "5 transcript missing");
                Ok(a.Find(Id(2)).Stale, "A2 stale");
                Ok(a.Find(Id(2)).AlsoIn.SequenceEqual(new[] { B }), "A2 also in B");
                Ok(a.Find(Id(1)).Title == "S1", "title read past nested field");
            });

            Test("copy adds new card, skips when target is newer", () =>
            {
                var r = store.Transfer(A, C, L(Id(1)), false, false);
                Ok(r.Done[0].Type == "added", "added");
                Ok(Has(C, 1) && Has(A, 1), "copied, source kept");
                Ok(File.ReadAllBytes(CardFile(A, 1)).SequenceEqual(File.ReadAllBytes(CardFile(C, 1))), "byte-identical copy");
                var r2 = store.Transfer(A, B, L(Id(2)), false, false);
                Ok(r2.Done.Count == 0 && r2.Skipped[0].CanOverwrite, "skipped, can overwrite");
                Ok(Title(B, 2) == "S2-newer", "target untouched");
                Ok(r2.BatchId == null, "no-op batch leaves no history");
            });

            Test("copy over a tombstone removes the tombstone", () =>
            {
                var r = store.Transfer(A, B, L(Id(1)), false, false);
                Ok(r.Done.Count == 1 && Has(B, 1) && !File.Exists(Tomb(B, 1)), "tombstone gone");
            });

            Test("copy of archived card marks it archived in target index", () =>
            {
                store.Transfer(A, C, L(Id(3)), false, false);
                Ok(File.ReadAllText(Path.Combine(Dir(C), "archived-sessions.idx")).Contains("local_" + Id(3)), "idx updated");
            });

            Test("overwrite replaces newer target; undo restores it", () =>
            {
                var before = File.ReadAllBytes(CardFile(B, 2));
                var r = store.Transfer(A, B, L(Id(2)), false, true);
                Ok(r.Done[0].Type == "updated" && Title(B, 2) == "S2", "overwritten");
                store.Undo(r.BatchId);
                Ok(File.ReadAllBytes(CardFile(B, 2)).SequenceEqual(before), "restored bytes");
            });

            Test("a copy with the same size and file time is not served from cache", () =>
            {
                // Same JSON length, same write time: only the content differs.
                MakeCard(A, 6, "", 6000, "Same-size-a");
                MakeCard(B, 6, "", 5000, "Same-size-b");
                var t = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(CardFile(A, 6), t);
                File.SetLastWriteTimeUtc(CardFile(B, 6), t);
                store.Scan();
                Ok(store.Get(B).Find(Id(6)).LastActivityAt == 5000, "B's own copy read first");
                store.Transfer(A, B, L(Id(6)), false, false);
                store.Scan();
                Ok(store.Get(B).Find(Id(6)).LastActivityAt == 6000, "after the copy B shows A's version");
                store.Remove(A, L(Id(6)));
                store.Remove(B, L(Id(6)));
            });

            Test("move copies, removes source, writes tombstone", () =>
            {
                var r = store.Transfer(B, C, L(Id(4)), true, false);
                Ok(r.Done.Count == 1 && Has(C, 4) && !Has(B, 4), "moved");
                long ts;
                Ok(long.TryParse(File.ReadAllText(Tomb(B, 4)), out ts) && ts > 0, "tombstone timestamp");
            });

            Test("undo out of order is refused", () =>
            {
                var r1 = store.Transfer(C, A, L(Id(4)), false, false);
                var r2 = store.Remove(A, L(Id(4)));
                Throws(() => store.Undo(r1.BatchId), "請先復原較新的操作");
                store.Undo(r2.BatchId);
                Ok(Has(A, 4), "remove undone");
                store.Undo(r1.BatchId);
                Ok(!Has(A, 4), "copy undone");
                Throws(() => store.Undo(r1.BatchId), "已經復原過");
            });

            Test("remove deletes card, tombstones, drops archive entry", () =>
            {
                var r = store.Remove(C, L(Id(3)));
                Ok(r.Done.Count == 1 && !Has(C, 3) && File.Exists(Tomb(C, 3)), "removed");
                Ok(!File.ReadAllText(Path.Combine(Dir(C), "archived-sessions.idx")).Contains(Id(3)), "idx entry dropped");
                store.Undo(r.BatchId);
                Ok(Has(C, 3) && !File.Exists(Tomb(C, 3)), "remove undone");
            });

            Test("sync plan: newest wins, tombstones/archived/missing skipped", () =>
            {
                File.WriteAllText(Tomb(C, 2), "5");
                var plan = store.PlanSync(new SyncOptions { Targets = L(A, B, C) });
                Func<string, string> tag = k => k == A ? "A" : k == B ? "B" : "C";
                var ops = plan.Ops.Select(o => o.Type + ":" + o.Id[0] + ":" + tag(o.To)).ToList();
                Ok(ops.Contains("update:2:A"), "update 2 in A: " + string.Join(",", ops));
                Ok(!ops.Any(o => o.EndsWith(":2:C")), "2 not re-added to C (tombstone)");
                Ok(ops.Contains("add:4:A"), "4 added to A");
                Ok(!ops.Any(o => o.StartsWith("add:4:B")), "4 not re-added to B (tombstone)");
                Ok(!ops.Any(o => o.Contains(":5:")), "missing transcript skipped");
                Ok(!ops.Contains("add:3:B"), "archived skipped");
                var reasons = plan.Skipped.Select(s => s.Reason).ToList();
                Ok(reasons.Contains("在這個帳號被刪除過") && reasons.Contains("已封存"), "skip reasons");

                var r = store.ApplySync(plan.Ops);
                Ok(r.Done.Count == plan.Ops.Count, "all applied");
                Ok(Title(A, 2) == "S2-newer", "newest won");
                Ok(store.PlanSync(new SyncOptions { Targets = L(A, B, C) }).Ops.Count == 0, "second sync is a no-op");
                store.Undo(r.BatchId);
                Ok(Title(A, 2) == "S2", "sync undone");
            });

            Test("rejects bad ids and unknown accounts", () =>
            {
                Throws(() => store.Transfer(A, B, L("..\\..\\x"), false, false), "編號不正確");
                Throws(() => store.Transfer(A, "nope", L(Id(1)), false, false), "找不到帳號");
            });

            Test("labels persist outside Claude folders", () =>
            {
                store.Scan();
                store.SetLabel(A, "主帳號");
                store.Scan();
                Ok(store.Get(A).Name == "主帳號", "label shown");
                Ok(File.Exists(Path.Combine(tmp, "data", "settings.json")), "settings in data dir");
            });

            Test("history lists batches newest first", () =>
            {
                var h = store.History();
                Ok(h.Count >= 5, "batches recorded");
                Ok(Json.Long(h[0], "createdAt") >= Json.Long(h[h.Count - 1], "createdAt"), "sorted");
            });

            Test("readable names from web cache and CLI login", () =>
            {
                store.SetLabel(A, "");
                store.Scan();
                var a = store.Get(A);
                Ok(a.Name == "a@example.com", "A e-mail from cache: " + a.Name);
                Ok(a.Short.Contains("王小明") && a.Short.Contains("Team X"), "A person + org: " + a.Short);
                var b = store.Get(B);
                Ok(b.Name == "b@example.com" && b.Short.Contains("Bee") && b.Short.Contains("個人組織"), "B from claude.json: " + b.Short);
                var c = store.Get(C);
                Ok(!c.Known && c.Name.StartsWith("未認出的帳號"), "C unknown: " + c.Name);
                Ok(!File.ReadAllText(Path.Combine(tmp, "data", "settings.json")).Contains("my chat title"), "chat title not taken as org");
                // Remembered after the sources forget the account.
                File.Delete(Path.Combine(tmp, "claude_x", "IndexedDB", "https_claude.ai_0.indexeddb.blob", "1", "00", "7"));
                var again = new Store(store.Roots, projects, Path.Combine(tmp, "data")) { ClaudeJsonPaths = new List<string>() };
                again.Scan();
                Ok(again.Get(A).Name == "a@example.com", "name remembered in settings");
            });

            Test("permanent delete removes cards everywhere and the files", () =>
            {
                store.Scan();
                Ok(store.Accounts.Count(x => x.Find(Id(1)) != null) == 3, "session 1 in all three accounts");
                string side = Path.Combine(projects, "p", Cli(1), "subagents");
                Directory.CreateDirectory(side);
                File.WriteAllText(Path.Combine(side, "agent.jsonl"), "{}");
                string fh = Path.Combine(tmp, "file-history", Cli(1));
                Directory.CreateDirectory(fh);
                File.WriteAllText(Path.Combine(fh, "a.txt"), "old version");
                var plan = store.PlanPurge(L(Id(1)));
                Ok(plan.Count == 1 && plan[0].Cards.Count == 3 && plan[0].Paths.Count == 3 && plan[0].Bytes > 0, "plan: 3 cards, 3 paths");

                var r = store.Purge(L(Id(1)));
                Ok(r.Done.Count == 1, "purged");
                Ok(!Has(A, 1) && !Has(B, 1) && !Has(C, 1), "cards gone in every account");
                Ok(File.Exists(Tomb(A, 1)) && File.Exists(Tomb(C, 1)), "tombstones like the app");
                Ok(!File.Exists(Path.Combine(projects, "p", Cli(1) + ".jsonl")), "transcript deleted");
                Ok(!Directory.Exists(Path.Combine(projects, "p", Cli(1))) && !Directory.Exists(fh), "sidecar + file-history deleted");
                Ok(File.Exists(Path.Combine(projects, "p", Cli(2) + ".jsonl")), "other transcripts untouched");
                var h = store.History()[0];
                Ok(Json.Str(h, "kind") == "purge" && !Json.Bool(h, "undoable"), "history records non-undoable purge");
                Ok(!Directory.Exists(Path.Combine(store.HistoryDir, Json.Str(h, "id"), "files")), "no backups kept");
                Throws(() => store.Undo(r.BatchId), "永久刪除無法復原");
            });

            Test("empty an account: exclusive purged, shared only removed", () =>
            {
                store.Scan();
                Ok(store.Get(C).Find(Id(3)) != null && store.Get(A).Find(Id(3)) != null, "3 shared by A and C");
                Ok(store.Get(C).Find(Id(4)) != null && store.Get(C).Find(Id(4)).AlsoIn.Count == 0, "4 only in C");
                var plan = store.PlanAccountPurge(C);
                Ok(plan.Exclusive.Count == 1 && plan.Shared.Count == 1, "plan splits exclusive/shared");
                var r = store.PurgeAccount(C);
                Ok(r.Done.Count(d => d.Type == "purged") == 1 && r.Done.Count(d => d.Type == "removed") == 1, "one purged, one removed");
                store.Scan();
                Ok(store.Get(C).Cards.Count == 0, "account emptied");
                Ok(store.Get(C).InUse, "emptied account still listed (has delete records)");
                Ok(Has(A, 3) && File.Exists(Path.Combine(projects, "p", Cli(3) + ".jsonl")), "shared session kept for A");
                Ok(!File.Exists(Path.Combine(projects, "p", Cli(4) + ".jsonl")), "exclusive transcript deleted");
            });

            Test("never-used account folders are not in use", () =>
            {
                string d = Dir("44444444-4444-4444-4444-444444444444/dddddddd-dddd-dddd-dddd-dddddddddddd");
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "scheduled-tasks.json"), "{\"scheduledTasks\":[]}");
                store.Scan();
                Ok(!store.Get("44444444-4444-4444-4444-444444444444/dddddddd-dddd-dddd-dddd-dddddddddddd").InUse, "unused folder hidden");
            });

            Test("a newly signed-in account shows before its first session", () =>
            {
                string cfg = Path.Combine(tmp, "claude_x", "config.json");
                string fresh = "55555555-5555-5555-5555-555555555555/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
                Directory.CreateDirectory(Dir(fresh));
                File.WriteAllText(cfg, "{\"lastKnownAccountUuid\":\"55555555-5555-5555-5555-555555555555\"}");
                store.Scan();
                Ok(store.Get(fresh).InUse, "signed-in account with no sessions counts");
                // An empty org folder of an account that already uses another one stays hidden.
                string extra = "11111111-1111-1111-1111-111111111111/ffffffff-ffff-ffff-ffff-ffffffffffff";
                Directory.CreateDirectory(Dir(extra));
                File.WriteAllText(cfg, "{\"lastKnownAccountUuid\":\"11111111-1111-1111-1111-111111111111\"}");
                store.Scan();
                Ok(!store.Get(extra).InUse && !store.Get(fresh).InUse, "spare folders stay hidden");
            });

            Test("a malformed transcript id never becomes a path to delete", () =>
            {
                File.WriteAllText(CardFile(A, 7), "{\"sessionId\":\"local_" + Id(7) + "\",\"cliSessionId\":\"..\",\"title\":\"Odd\",\"cwd\":\"D:\\\\x\",\"lastActivityAt\":1,\"completedTurns\":1}");
                string keep = Path.Combine(tmp, "file-history", "keep.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(keep));
                File.WriteAllText(keep, "x");
                var plan = store.PlanPurge(L(Id(7)));
                Ok(plan.Count == 1 && plan[0].Blocked != null && plan[0].Paths.Count == 0, "plan refuses it");
                var r = store.Purge(L(Id(7)));
                Ok(r.Done.Count == 0 && r.Skipped.Count == 1, "purge skips it");
                Ok(Has(A, 7) && File.Exists(keep) && Directory.Exists(projects), "nothing deleted");
                File.Delete(CardFile(A, 7));
            });

            Test("a locked transcript leaves the session whole", () =>
            {
                MakeCard(A, 8);
                string jsonl = Path.Combine(projects, "p", Cli(8) + ".jsonl");
                File.WriteAllText(jsonl, "{}\n");
                using (new FileStream(jsonl, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var r = store.Purge(L(Id(8)));
                    Ok(r.Done.Count == 0 && r.Skipped.Count == 1 && r.Skipped[0].Reason.StartsWith("檔案操作失敗"), "skipped with the file error");
                    Ok(Has(A, 8) && File.Exists(jsonl), "card and transcript both kept");
                }
                var again = store.Purge(L(Id(8)));
                Ok(again.Done.Count == 1 && !Has(A, 8) && !File.Exists(jsonl), "works once unlocked");
            });

            Test("emptying an account keeps cards that share a transcript", () =>
            {
                File.WriteAllText(CardFile(C, 9), "{\"sessionId\":\"local_" + Id(9) + "\",\"cliSessionId\":\"" + Cli(2) + "\",\"title\":\"Fork\",\"cwd\":\"D:\\\\x\",\"lastActivityAt\":1,\"completedTurns\":1}");
                var plan = store.PlanAccountPurge(C);
                Ok(plan.Exclusive.Count == 0 && plan.Shared.Count == 1, "shared by transcript, not only by id");
                File.Delete(CardFile(C, 9));
            });

            Test("undo asks before overwriting later changes", () =>
            {
                var r = store.Transfer(A, C, L(Id(2)), false, false);
                Ok(r.Done.Count == 1, "copied");
                File.AppendAllText(CardFile(C, 2), " ");
                try { store.Undo(r.BatchId); Ok(false, "should refuse"); }
                catch (ChangedSinceException e) { Ok(e.Paths.Count == 1, "names the changed file"); }
                store.Undo(r.BatchId, true);
                Ok(!Has(C, 2), "forced undo restores");
            });

            Test("a file error skips one item, not the batch", () =>
            {
                string idx = Path.Combine(Dir(C), "archived-sessions.idx");
                File.WriteAllText(idx, "{\"v\":1,\"archived\":[]}");
                OpResult r;
                using (new FileStream(idx, FileMode.Open, FileAccess.Read, FileShare.Read))
                    r = store.Transfer(A, C, L(Id(2), Id(3)), false, false);
                Ok(r.Done.Any(d => d.Id == Id(2)), "the other item went through");
                Ok(r.Skipped.Any(d => d.Id == Id(3) && d.Reason.StartsWith("檔案操作失敗")), "the locked one is a skip: " + string.Join(";", r.Skipped.Select(x => x.Reason)));
                store.Undo(r.BatchId, true);
            });

            Test("an unknown file layout makes the store read-only", () =>
            {
                string idx = Path.Combine(Dir(C), "archived-sessions.idx");
                File.WriteAllText(idx, "{\"v\":2,\"archived\":[]}");
                store.Scan();
                Ok(store.ReadOnly && store.FormatIssues[0].Contains("2"), "read-only: " + string.Join(";", store.FormatIssues));
                Throws(() => store.Transfer(A, C, L(Id(2)), false, false), "唯讀模式");
                Store.TrustFormat = true;
                try
                {
                    var r = store.Transfer(A, C, L(Id(2)), false, false);
                    Ok(r.Done.Count == 1, "--trust-format lets changes through");
                    store.Undo(r.BatchId, true);
                }
                finally { Store.TrustFormat = false; }
                File.WriteAllText(idx, "{\"v\":1,\"archived\":[]}");
                store.Scan();
                Ok(!store.ReadOnly, "back to normal");
            });

            Test("undo can be redone, and redo undone again", () =>
            {
                var r = store.Transfer(A, C, L(Id(2)), false, false);
                Ok(Has(C, 2), "copied");
                var undone = store.Undo(r.BatchId);
                string redoId = Json.Str(undone, "redoId");
                Ok(!Has(C, 2) && redoId != null, "undo removes it and offers a redo");
                var redone = store.Undo(redoId);
                Ok(Has(C, 2) && Json.Str(redone, "reverts") == r.BatchId, "redo puts it back");
                Ok(!Json.Bool(Journal.Load(store.HistoryDir, r.BatchId), "undone"), "the original counts as applied again");
                store.Undo(r.BatchId);
                Ok(!Has(C, 2), "and can be undone again");
            });

            Test("conversations no list points at: list, recover, delete", () =>
            {
                Func<int, string, string> line = (n, text) => "{\"type\":\"user\",\"cwd\":\"D:\\\\proj\",\"timestamp\":\"2026-10-01T10:0" + n + ":00Z\",\"message\":{\"content\":\"" + text + "\"}}\n";
                string a = Path.Combine(projects, "p", Cli(20) + ".jsonl");
                File.WriteAllText(a, line(0, "<command-name>/clear</command-name>") + line(1, "Fix the login bug") + line(2, "and the tests"));
                string b = Path.Combine(projects, "p", Cli(21) + ".jsonl");
                File.WriteAllText(b, line(0, "Old notes"));
                Directory.CreateDirectory(Path.Combine(projects, "p", Cli(21), "subagents"));
                string busy = Path.Combine(projects, "p", Cli(22) + ".jsonl");
                File.WriteAllText(busy, line(0, "Still running"));
                var old = DateTime.UtcNow.AddHours(-2);
                File.SetLastWriteTimeUtc(a, old);
                File.SetLastWriteTimeUtc(b, old);

                var list = store.Orphans();
                var oa = list.FirstOrDefault(o => o.Id == Cli(20));
                Ok(list.Count == 3 && oa != null, "three orphans found: " + list.Count);
                Ok(oa.Title == "Fix the login bug" && oa.Cwd == "D:\\proj" && oa.FirstMs > 0, "title skips the command wrapper: " + oa.Title);

                var rec = store.Recover(L(Cli(20)), A);
                Ok(rec.Done.Count == 1, "recovered into A");
                var card = Json.ParseObject(File.ReadAllText(CardFile(A, 0).Replace(Id(0), rec.Done[0].Id)));
                Ok(Json.Str(card, "cliSessionId") == Cli(20) && Json.Str(card, "title") == "Fix the login bug" && Json.Str(card, "cwd") == "D:\\proj", "card points at the transcript");
                Ok(Json.Str(card, "sessionId") == "local_" + rec.Done[0].Id && !card.ContainsKey("bridgeSessionIds") && Json.Long(card, "completedTurns") == 2, "fresh session fields");
                store.Scan();
                Ok(!store.OrphanIds.Contains(Cli(20)), "no longer an orphan");
                store.Undo(rec.BatchId);
                store.Scan();
                Ok(store.OrphanIds.Contains(Cli(20)), "undo removes the new card");

                var del = store.PurgeOrphans(L(Cli(21), Cli(22)));
                Ok(del.Done.Count == 1 && !File.Exists(b) && !Directory.Exists(Path.Combine(projects, "p", Cli(21))), "deleted with its side folder");
                Ok(del.Skipped.Count == 1 && File.Exists(busy), "a recently written one is left alone");
                Throws(() => store.Undo(del.BatchId), "永久刪除無法復原");
                File.Delete(a);
                File.Delete(busy);
            });

            Test("the same account in two Claude data folders stays apart", () =>
            {
                string root2 = Path.Combine(tmp, "claude_y", "claude-code-sessions");
                string d2 = Path.Combine(root2, A.Replace('/', '\\'));
                Directory.CreateDirectory(d2);
                File.Copy(CardFile(A, 2), Path.Combine(d2, "local_" + Id(2) + ".json"));
                var two = new Store(new List<string> { root, root2 }, projects, Path.Combine(tmp, "data2")) { ClaudeJsonPaths = new List<string>() };
                two.Scan();
                var same = two.Accounts.Where(x => x.AccountUuid == "11111111-1111-1111-1111-111111111111" && x.OrgUuid.StartsWith("aaaa")).ToList();
                Ok(same.Count == 2 && same[0].Key != same[1].Key, "two columns with their own keys");
                two.SetLabel(same[0].Key, "first");
                two.Scan();
                Ok(two.Get(same[0].Key).Label == "first" && two.Get(same[1].Key).Label == null, "a label sticks to one folder");
            });

            Test("Microsoft Store installs restart through their app id", () =>
            {
                string pkg = Path.Combine(tmp, "WindowsApps", "Claude_1.2.3.0_x64__abc123");
                Directory.CreateDirectory(Path.Combine(pkg, "app"));
                File.WriteAllText(Path.Combine(pkg, "AppxManifest.xml"),
                    "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Applications>" +
                    "<Application Id=\"Helper\" Executable=\"app\\helper.exe\"/><Application Id=\"Claude\" Executable=\"app\\Claude.exe\"/></Applications></Package>");
                Ok(ClaudeApp.AppUserModelId(Path.Combine(pkg, "app", "Claude.exe")) == "Claude_abc123!Claude", "app id from the manifest");
                Ok(ClaudeApp.AppUserModelId(@"C:\Users\x\AppData\Local\AnthropicClaude\claude.exe") == null, "other installs start by path");
            });
        }
    }
}
