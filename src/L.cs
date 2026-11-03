// UI language: Traditional Chinese (the keys), English and Japanese.
// Code passes the Chinese text as the key: L.T("顯示已封存"), L.F("略過 {0} 項", n).
// A missing translation falls back to the Chinese text; test\StoreTests checks none are missing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SessionSync
{
    static class L
    {
        public static readonly string[] Codes = { "zh", "en", "ja" };
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "zh", "繁體中文" }, { "en", "English" }, { "ja", "日本語" },
        };

        public static string Lang = "zh";

        public static string Bullet { get { return Lang == "en" ? "• " : "・"; } }

        // Saved choice first, then the Windows display language.
        public static string Pick(string saved)
        {
            if (saved != null && Names.ContainsKey(saved)) return saved;
            string ui = CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            if (ui.StartsWith("zh")) return "zh";
            if (ui.StartsWith("ja")) return "ja";
            return "en";
        }

        public static bool Has(string key, string lang)
        {
            string[] t;
            return Table.TryGetValue(key, out t) && !string.IsNullOrEmpty(t[lang == "en" ? 0 : 1]);
        }

        public static string T(string key)
        {
            if (Lang == "zh") return key;
            string[] t;
            if (!Table.TryGetValue(key, out t)) return key;
            string s = t[Lang == "en" ? 0 : 1];
            return string.IsNullOrEmpty(s) ? key : s;
        }

        static readonly Regex OneEn = new Regex(@"(?<![\d.])1 (session|card|file|account|change|conversation|item|minute)s\b");

        public static string F(string key, params object[] args)
        {
            string s = string.Format(T(key), args);
            return Lang == "en" ? OneEn.Replace(s, "1 $1") : s;
        }

        // zh key → { English, 日本語 }
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // ---- main window ----
            { "Session 同步器", new[] { "Session Sync", "Session 同期ツール" } },
            { "搜尋標題或資料夾（Ctrl+F）", new[] { "Search title or folder (Ctrl+F)", "タイトルやフォルダーを検索（Ctrl+F）" } },
            { "顯示已封存", new[] { "Show archived", "アーカイブを表示" } },
            { "完整同步…", new[] { "Full sync…", "完全同期…" } },
            { "操作紀錄", new[] { "History", "操作履歴" } },
            { "重新整理", new[] { "Refresh", "更新" } },
            { "讓所有帳號都有同一組 session，先預覽再套用", new[] { "Give every account the same sessions. Preview first, then apply.", "すべてのアカウントに同じセッションをそろえます。先にプレビューしてから適用します。" } },
            { "Claude 桌面版正在執行：加進去的 session 要重開 Claude 才會出現在側欄。要移除或覆蓋「目前登入」帳號的卡片，建議先關閉 Claude，不然它可能把卡片寫回去。",
                new[] { "Claude desktop is running: added sessions appear in its sidebar after you restart Claude. Close Claude before removing or overwriting cards of the signed-in account, or it may write them back.",
                        "Claude デスクトップが起動中です。追加したセッションは Claude を再起動するとサイドバーに表示されます。ログイン中のアカウントのカードを削除・上書きする前に Claude を閉じてください。閉じないと書き戻されることがあります。" } },
            { "讀取中…", new[] { "Loading…", "読み込み中…" } },
            { "略過明細", new[] { "Skipped details", "スキップの詳細" } },
            { "點圓點＝加入／移除　🗑＝永久刪除　←→＋空白鍵＝鍵盤操作　點帳號名稱＝帳號選單　雙擊＝詳細",
                new[] { "Click a dot = add / remove   🗑 = delete for good   ←→ + Space = keyboard   Click an account name = account menu   Double-click = details",
                        "丸をクリック＝追加／外す　🗑＝完全削除　←→＋スペース＝キーボード操作　アカウント名をクリック＝メニュー　ダブルクリック＝詳細" } },
            { "{0} 個 session", new[] { "{0} sessions", "{0} 件のセッション" } },
            { "沒有 session", new[] { "no sessions", "セッションなし" } },
            { "顯示「{0}」（{1}）", new[] { "Show “{0}” ({1})", "「{0}」を表示（{1}）" } },
            { "全部顯示", new[] { "Show all", "すべて表示" } },
            { " {0} 個", new[] { " ({0})", "（{0} 件）" } },
            { "詳細資料", new[] { "Details", "詳細" } },
            { "加到所有帳號", new[] { "Add to all accounts", "すべてのアカウントに追加" } },
            { "加到", new[] { "Add to", "追加先" } },
            { "從…移除", new[] { "Remove from", "外す" } },
            { "（{0} 個）", new[] { " ({0})", "（{0} 件）" } },
            { "永久刪除對話", new[] { "Delete conversation for good", "会話を完全に削除" } },
            { "改名…", new[] { "Rename…", "名前を変更…" } },
            { "隱藏這個帳號", new[] { "Hide this account", "このアカウントを隠す" } },
            { "把列表中的 session 都加進來（{0} 個）", new[] { "Add the listed sessions here ({0})", "一覧のセッションをここに追加（{0} 件）" } },
            { "刪除這個帳號的所有 session…", new[] { "Delete all sessions in this account…", "このアカウントのセッションをすべて削除…" } },
            { "讀取失敗：{0}", new[] { "Could not read: {0}", "読み込みに失敗しました：{0}" } },
            { "找到 {0} 個帳號、{1} 個 session（{2} 張卡片）", new[] { "Found {0} accounts, {1} sessions ({2} cards)", "{0} 個のアカウント、{1} 件のセッション（カード {2} 枚）が見つかりました" } },
            { "。{0} 個帳號還沒認出：用它登入 Claude 後開一次本程式，或點名稱自己取",
                new[] { ". {0} accounts not recognized yet: sign in to Claude with it and open this app once, or click its name to name it.",
                        "。{0} 個のアカウントがまだ不明です：そのアカウントで Claude にログインしてからこのアプリを一度開くか、名前をクリックして付けてください" } },
            { "隱藏的帳號（{0}）", new[] { "Hidden accounts ({0})", "非表示のアカウント（{0}）" } },
            { "所選的 session 在這些帳號都已經是最新的", new[] { "The selected sessions are already up to date in these accounts", "選んだセッションはこれらのアカウントですでに最新です" } },
            { "加入 {0} 張卡片", new[] { "Add {0} cards", "カード {0} 枚を追加" } },
            { "已加入 {0} 個 session", new[] { "Added {0} sessions", "{0} 件のセッションを追加しました" } },
            { "沒有加入任何 session", new[] { "No sessions added", "追加したセッションはありません" } },
            { "「{0}」是目前登入的帳號，而且 Claude 正在執行。\n改動它已經載入的卡片時，Claude 之後可能又把卡片寫回去。\n\n建議先關閉 Claude 再做。仍要繼續嗎？",
                new[] { "“{0}” is the signed-in account and Claude is running.\nIf you change cards it has loaded, Claude may write them back later.\n\nClosing Claude first is recommended. Continue anyway?",
                        "「{0}」はログイン中のアカウントで、Claude が起動中です。\n読み込み済みのカードを変更すると、あとで Claude が書き戻すことがあります。\n\n先に Claude を閉じることをおすすめします。続行しますか？" } },
            { "Claude 正在執行", new[] { "Claude is running", "Claude が起動中です" } },
            { "已搬移 {0} 個 session", new[] { "Moved {0} sessions", "{0} 件のセッションを移動しました" } },
            { "已複製 {0} 個 session", new[] { "Copied {0} sessions", "{0} 件のセッションをコピーしました" } },
            { "沒有搬移任何 session", new[] { "No sessions moved", "移動したセッションはありません" } },
            { "沒有複製任何 session", new[] { "No sessions copied", "コピーしたセッションはありません" } },
            { "要從「{0}」移除這 {1} 個 session 嗎？", new[] { "Remove {1} sessions from “{0}”?", "「{0}」からこの {1} 件のセッションを外しますか？" } },
            { "只會移除這個帳號側欄裡的卡片。對話檔不會刪，其他帳號不受影響，之後可以在「操作紀錄」復原。",
                new[] { "Only the cards in this account's sidebar are removed. Conversation files stay, other accounts are not affected, and you can undo it in History.",
                        "このアカウントのサイドバーからカードを外すだけです。会話ファイルは残り、ほかのアカウントには影響しません。「操作履歴」から元に戻せます。" } },
            { "注意：其中 {0} 個只存在這個帳號。移除後，任何帳號的側欄都看不到它（對話檔還在，可以復原）。",
                new[] { "Note: {0} of them exist only in this account. After removal no account's sidebar shows them (the conversation files remain and you can undo).",
                        "注意：うち {0} 件はこのアカウントにしかありません。外すとどのアカウントのサイドバーにも表示されなくなります（会話ファイルは残り、元に戻せます）。" } },
            { "Claude 正在執行，而且這是目前登入的帳號。Claude 可能把卡片寫回去，建議先關閉 Claude。",
                new[] { "Claude is running and this is the signed-in account. Claude may write the cards back; close Claude first.",
                        "Claude が起動中で、これはログイン中のアカウントです。カードが書き戻されることがあるので、先に Claude を閉じてください。" } },
            { "移除 session", new[] { "Remove sessions", "セッションを外す" } },
            { "已移除 {0} 個 session", new[] { "Removed {0} sessions", "{0} 件のセッションを外しました" } },
            { "沒有移除任何 session", new[] { "No sessions removed", "外したセッションはありません" } },
            { "「{0}」最近 3 分鐘內還有活動，可能正在執行。\n\n請先在 Claude 停止它（或關閉 Claude），再刪除。",
                new[] { "“{0}” was active in the last 3 minutes and may still be running.\n\nStop it in Claude (or close Claude) before deleting.",
                        "「{0}」は直近 3 分以内に動きがあり、実行中の可能性があります。\n\nClaude で停止する（または Claude を閉じる）か、少し待ってから削除してください。" } },
            { "先停止這個 session", new[] { "Stop this session first", "先にこのセッションを停止してください" } },
            { "永久刪除這 {0} 個 session 的對話？", new[] { "Permanently delete the conversations of {0} sessions?", "この {0} 件のセッションの会話を完全に削除しますか？" } },
            { "　（{0}，在 {1} 個帳號）", new[] { "  ({0}, in {1} accounts)", "　（{0}、{1} 個のアカウント）" } },
            { "會刪除：", new[] { "This deletes:", "削除されるもの：" } },
            { "・所有帳號側欄裡的這些 session", new[] { "• these sessions in every account's sidebar", "・すべてのアカウントのサイドバーにあるこれらのセッション" } },
            { "・對話檔（.jsonl）和它的附屬資料夾", new[] { "• the conversation files (.jsonl) and their side folders", "・会話ファイル（.jsonl）と付随フォルダー" } },
            { "・這些 session 的檔案編輯備份（file-history）", new[] { "• the file-edit backups of these sessions (file-history)", "・これらのセッションのファイル編集バックアップ（file-history）" } },
            { "刪除後無法復原，也不會進資源回收筒。專案資料夾裡的檔案不受影響。",
                new[] { "This cannot be undone and nothing goes to the Recycle Bin. Files in your project folders are not touched.",
                        "削除後は元に戻せず、ごみ箱にも入りません。プロジェクトフォルダー内のファイルには影響しません。" } },
            { "Claude 正在執行：側欄要重開 Claude 才會更新。", new[] { "Claude is running: its sidebar updates after you restart Claude.", "Claude が起動中です。サイドバーは Claude を再起動すると更新されます。" } },
            { "永久刪除對話…", new[] { "Delete conversation for good…", "会話を完全に削除…" } },
            { "已永久刪除 {0} 個 session 的對話", new[] { "Permanently deleted the conversations of {0} sessions", "{0} 件のセッションの会話を完全に削除しました" } },
            { "沒有刪除任何 session", new[] { "No sessions deleted", "削除したセッションはありません" } },
            { "，釋放 {0}", new[] { ", freed {0}", "（{0} 解放）" } },
            { "另外 {0} 個共用同一個對話檔的 session 也會一起刪除：", new[] { "{0} other sessions that share the same conversation file are deleted too:", "同じ会話ファイルを使う別のセッション {0} 件も一緒に削除されます：" } },
            { "，{0} 個在該帳號刪除過或對話檔遺失，沒有加入", new[] { ", {0} not added (deleted there before, or conversation file missing)", "、{0} 件は追加しませんでした（そのアカウントで削除済み、または会話ファイルなし）" } },
            { "沒用過的帳號資料夾", new[] { "Account folders never used", "未使用のアカウントフォルダー" } },
            { "有 {0} 個檔案在這次操作之後又被改過（例如你在 Claude 裡繼續了那個 session）。\n\n仍要復原嗎？那些之後的變更會被蓋掉。",
                new[] { "{0} files changed after this operation (for example, you continued that session in Claude).\n\nUndo anyway? Those later changes will be overwritten.",
                        "この操作のあとで {0} 個のファイルが変更されています（たとえば Claude でそのセッションを続けた場合）。\n\nそれでも元に戻しますか？その後の変更は上書きされます。" } },
            { "刪除時出錯：{0}", new[] { "Error while deleting: {0}", "削除中にエラーが発生しました：{0}" } },
            { "刪除「{0}」的所有 session？", new[] { "Delete all sessions in “{0}”?", "「{0}」のセッションをすべて削除しますか？" } },
            { "只在這個帳號的 {0} 個：永久刪除，連同對話檔，無法復原",
                new[] { "{0} only in this account: deleted for good with their conversation files, cannot be undone",
                        "このアカウントにしかない {0} 件：会話ファイルごと完全に削除（元に戻せません）" } },
            { "其他帳號也有的 {0} 個：只從這個帳號移除，其他帳號照常可用（可以復原）",
                new[] { "{0} also in other accounts: only removed from this account, the others keep them (can be undone)",
                        "ほかのアカウントにもある {0} 件：このアカウントから外すだけで、ほかのアカウントではそのまま使えます（元に戻せます）" } },
            { "帳號本身、排程任務和專案資料夾都不會動。", new[] { "The account itself, its scheduled tasks and your project folders are not touched.", "アカウント自体、スケジュールタスク、プロジェクトフォルダーには触れません。" } },
            { "Claude 正在執行，而且這是目前登入的帳號，建議先關閉 Claude。", new[] { "Claude is running and this is the signed-in account; close Claude first.", "Claude が起動中で、これはログイン中のアカウントです。先に Claude を閉じてください。" } },
            { "清空帳號", new[] { "Empty account", "アカウントを空にする" } },
            { "再確認一次：永久刪除 {0} 個對話，刪了就找不回來。", new[] { "One more check: delete {0} conversations for good? They cannot be recovered.", "最終確認：{0} 件の会話を完全に削除します。削除すると元に戻せません。" } },
            { "已清空「{0}」：永久刪除 {1} 個、移除 {2} 個，釋放 {3}", new[] { "Emptied “{0}”: {1} deleted for good, {2} removed, freed {3}", "「{0}」を空にしました：完全削除 {1} 件、外した {2} 件、{3} 解放" } },
            { "已復原：{0}", new[] { "Undone: {0}", "元に戻しました：{0}" } },
            { "無法復原：{0}", new[] { "Cannot undo: {0}", "元に戻せません：{0}" } },
            { "到「{0}」", new[] { " to “{0}”", "（→「{0}」）" } },
            { "，略過 {0} 個（{1}）", new[] { ", skipped {0} ({1})", "、{0} 件スキップ（{1}）" } },
            { "。重開 Claude 後才會出現在側欄", new[] { ". They appear in the sidebar after you restart Claude", "。Claude を再起動するとサイドバーに表示されます" } },
            { "仍要覆蓋 {0} 個", new[] { "Overwrite {0} anyway", "{0} 件を上書きする" } },
            { "・…還有 {0} 個", new[] { "• …and {0} more", "・…ほか {0} 件" } },
            { "略過的 session", new[] { "Skipped sessions", "スキップしたセッション" } },
            { "帳號名稱", new[] { "Account name", "アカウント名" } },
            { "給這個帳號取個好認的名字（只存在這台電腦）。\n{0}\n留空就用預設名稱。",
                new[] { "Give this account a name you'll recognize (kept on this computer only).\n{0}\nLeave empty to use the default.",
                        "このアカウントにわかりやすい名前を付けます（このパソコンにだけ保存）。\n{0}\n空欄にすると既定の名前に戻ります。" } },
            { "找不到檔案", new[] { "File not found", "ファイルが見つかりません" } },
            { "已同步 {0} 個 session", new[] { "Synced {0} sessions", "{0} 件のセッションを同期しました" } },
            { "沒有同步任何 session", new[] { "No sessions synced", "同期したセッションはありません" } },
            { "已從操作紀錄復原", new[] { "Undone from History", "操作履歴から元に戻しました" } },
            { "Session 同步器發生錯誤", new[] { "Session Sync error", "Session 同期ツールでエラーが発生しました" } },
            { "找不到 Claude 桌面版的 session 資料夾（claude-code-sessions）。\n\n請確認這台電腦用過 Claude 桌面版的 Code 分頁。",
                new[] { "Could not find the Claude desktop session folder (claude-code-sessions).\n\nMake sure the Code tab of Claude desktop has been used on this computer.",
                        "Claude デスクトップのセッションフォルダー（claude-code-sessions）が見つかりません。\n\nこのパソコンで Claude デスクトップの Code タブを使ったことがあるか確認してください。" } },

            // ---- index ----
            { "最後活動", new[] { "Last active", "最終操作" } },
            { "輪數", new[] { "Turns", "ターン" } },
            { "暫存 · {0}", new[] { "Scratch · {0}", "一時フォルダー · {0}" } },
            { "已封存", new[] { "Archived", "アーカイブ済み" } },
            { "對話檔遺失", new[] { "Conversation file missing", "会話ファイルなし" } },
            { "永久刪除這個 session 的對話\n（所有帳號，連同對話檔，無法復原）",
                new[] { "Delete this session's conversation for good\n(all accounts, with the conversation file; cannot be undone)",
                        "このセッションの会話を完全に削除\n（すべてのアカウント、会話ファイルごと。元に戻せません）" } },
            { "「{0}」有這個 session（最新）\n點一下＝從這個帳號移除", new[] { "“{0}” has this session (latest)\nClick = remove from this account", "「{0}」にこのセッションがあります（最新）\nクリック＝このアカウントから外す" } },
            { "「{0}」有，但比「{1}」的舊\n點一下＝更新成最新版本", new[] { "“{0}” has it, but older than “{1}”\nClick = update to the latest", "「{0}」にありますが「{1}」より古いです\nクリック＝最新版に更新" } },
            { "這個 session 在「{0}」刪除過\n點一下＝加回去", new[] { "This session was deleted in “{0}”\nClick = add it back", "このセッションは「{0}」で削除されました\nクリック＝戻す" } },
            { "「{0}」沒有這個 session\n點一下＝加進去", new[] { "“{0}” doesn't have this session\nClick = add it", "「{0}」にこのセッションはありません\nクリック＝追加" } },
            { "沒有符合的 session", new[] { "No matching sessions", "一致するセッションはありません" } },

            // ---- dialogs ----
            { "Session 詳細資料", new[] { "Session details", "セッションの詳細" } },
            { "在「{0}」", new[] { "In “{0}”", "「{0}」内" } },
            { " · 已封存", new[] { " · archived", " · アーカイブ済み" } },
            { "各帳號的狀態（勾選要複製過去的帳號）", new[] { "Status per account (check the accounts to copy to)", "アカウントごとの状態（コピー先にチェック）" } },
            { "帳號", new[] { "Account", "アカウント" } },
            { "狀態", new[] { "Status", "状態" } },
            { "正在看的這份", new[] { "This copy", "表示中のもの" } },
            { "有 · {0}", new[] { "Yes · {0}", "あり · {0}" } },
            { "（比正在看的這份舊）", new[] { " (older than the one shown)", "（表示中のものより古い）" } },
            { "（比正在看的這份新）", new[] { " (newer than the one shown)", "（表示中のものより新しい）" } },
            { "（相同）", new[] { " (same)", "（同じ）" } },
            { "刪除過 · {0}", new[] { "Deleted · {0}", "削除済み · {0}" } },
            { "沒有", new[] { "No", "なし" } },
            { "複製到勾選的帳號", new[] { "Copy to checked accounts", "チェックしたアカウントにコピー" } },
            { "資料", new[] { "Info", "情報" } },
            { "資料夾", new[] { "Folder", "フォルダー" } },
            { "模型", new[] { "Model", "モデル" } },
            { "建立", new[] { "Created", "作成" } },
            { "權限模式", new[] { "Permissions", "権限モード" } },
            { "對話檔", new[] { "Conversation", "会話ファイル" } },
            { "找不到，接不回", new[] { "missing, cannot resume", "見つからないため再開できません" } },
            { "、", new[] { ", ", "、" } },
            { "：", new[] { ": ", "：" } },
            { "卡片檔", new[] { "Card file", "カードファイル" } },
            { "關閉", new[] { "Close", "閉じる" } },
            { "從這個帳號移除", new[] { "Remove from this account", "このアカウントから外す" } },
            { "顯示對話檔", new[] { "Show conversation file", "会話ファイルを表示" } },
            { "在檔案總管顯示卡片", new[] { "Show card in Explorer", "エクスプローラーでカードを表示" } },
            { "不要加回在該帳號刪過的 session", new[] { "Don't add back sessions deleted in that account", "そのアカウントで削除したセッションは戻さない" } },
            { "略過對話檔已遺失的 session", new[] { "Skip sessions whose conversation file is missing", "会話ファイルがないセッションはスキップ" } },
            { "包含已封存的 session", new[] { "Include archived sessions", "アーカイブ済みのセッションも含める" } },
            { "動作", new[] { "Action", "操作" } },
            { "來源", new[] { "From", "コピー元" } },
            { "原因", new[] { "Reason", "理由" } },
            { "套用", new[] { "Apply", "適用" } },
            { "完整同步", new[] { "Full sync", "完全同期" } },
            { "讓勾選的帳號都有同一組 session。同一個 session 在好幾個帳號時，以「最後活動」較新的那份為準。不會刪除任何卡片，套用後可以在「操作紀錄」整批復原。",
                new[] { "Give the checked accounts the same set of sessions. When a session is in several accounts, the copy with the newer last activity wins. No cards are deleted, and you can undo the whole batch in History.",
                        "チェックしたアカウントに同じセッションをそろえます。同じセッションが複数のアカウントにある場合は、最終操作が新しいものを使います。カードは削除されず、適用後は「操作履歴」からまとめて元に戻せます。" } },
            { "參與的帳號", new[] { "Accounts", "対象のアカウント" } },
            { "Claude 正在執行。加進目前登入帳號的 session 要重開 Claude 才看得到；更新目前帳號裡已載入的卡片，可能會被 Claude 蓋回去，建議先關閉 Claude。",
                new[] { "Claude is running. Sessions added to the signed-in account show after you restart Claude; cards it has loaded may be written back, so close Claude first.",
                        "Claude が起動中です。ログイン中のアカウントに追加したセッションは再起動後に表示されます。読み込み済みのカードは書き戻されることがあるので、先に Claude を閉じてください。" } },
            { "會做的變更（取消勾選就不做）", new[] { "Changes to make (uncheck to skip)", "実行する変更（チェックを外すと実行しません）" } },
            { "取消", new[] { "Cancel", "キャンセル" } },
            { "至少勾選兩個帳號。", new[] { "Check at least two accounts.", "アカウントを 2 つ以上選んでください。" } },
            { "＋ 新增", new[] { "+ Add", "＋ 追加" } },
            { "↻ 更新", new[] { "↻ Update", "↻ 更新" } },
            { "略過 {0} 項", new[] { "Skipped: {0}", "スキップ {0} 件" } },
            { "這些帳號已經同步好了。", new[] { "These accounts are already in sync.", "これらのアカウントはすでに同期済みです。" } },
            { "套用 {0} 項", new[] { "Apply {0}", "{0} 件を適用" } },
            { "會改動 {0} 張卡片，全部可以復原", new[] { "Changes {0} cards; all can be undone", "カード {0} 枚を変更します（すべて元に戻せます）" } },
            { "沒有勾選任何變更", new[] { "Nothing checked", "変更が選ばれていません" } },
            { "時間", new[] { "Time", "日時" } },
            { "操作", new[] { "Action", "操作" } },
            { "檔案", new[] { "Files", "ファイル" } },
            { "每次複製、搬移、移除、同步之前，都會先備份它要改的檔案。復原會把那些檔案放回操作前的狀態。",
                new[] { "Before every copy, move, remove or sync, the files it changes are backed up. Undo puts those files back as they were.",
                        "コピー・移動・削除・同期の前に、変更するファイルをバックアップします。元に戻すと、それらのファイルを操作前の状態に戻します。" } },
            { "復原所選", new[] { "Undo selected", "選択を元に戻す" } },
            { "開啟備份資料夾", new[] { "Open backup folder", "バックアップフォルダーを開く" } },
            { "已復原", new[] { "Undone", "元に戻し済み" } },
            { "未完成", new[] { "Incomplete", "未完了" } },
            { "無法復原", new[] { "Can't undo", "元に戻せません" } },
            { "還沒有任何操作", new[] { "Nothing yet", "まだ操作はありません" } },
            { "復原「{0}」？\n\n會把這次改到的 {1} 個檔案放回操作前的狀態。", new[] { "Undo “{0}”?\n\nThe {1} files it changed go back to how they were.", "「{0}」を元に戻しますか？\n\nこの操作で変更した {1} 個のファイルを操作前の状態に戻します。" } },
            { "復原", new[] { "Undo", "元に戻す" } },
            { "儲存", new[] { "Save", "保存" } },

            // ---- store, journal, json ----
            { "未認出的帳號（{0}）", new[] { "Unrecognized account ({0})", "不明なアカウント（{0}）" } },
            { "個人組織", new[] { "personal org", "個人の組織" } },
            { "{0} 的個人組織", new[] { "{0}'s personal org", "{0} の個人の組織" } },
            { "組織 {0}", new[] { "org {0}", "組織 {0}" } },
            { "用這個帳號登入 Claude 後開一次本程式就會認出", new[] { "sign in to Claude with it and open this app once to recognize it", "このアカウントで Claude にログインしてから本アプリを一度開くと認識されます" } },
            { "(未命名)", new[] { "(untitled)", "(無題)" } },
            { "找不到帳號資料夾：{0}", new[] { "Account folder not found: {0}", "アカウントフォルダーが見つかりません：{0}" } },
            { "session 編號不正確", new[] { "Invalid session id", "セッション ID が正しくありません" } },
            { "沒有選任何 session", new[] { "No session selected", "セッションが選ばれていません" } },
            { "session 編號不正確：{0}", new[] { "Invalid session id: {0}", "セッション ID が正しくありません：{0}" } },
            { "來源帳號裡找不到這張卡片", new[] { "The card is not in the source account", "コピー元のアカウントにカードがありません" } },
            { "目標已有相同或較新的版本", new[] { "The target already has the same or a newer copy", "コピー先に同じか新しいものがすでにあります" } },
            { "這個帳號裡沒有這張卡片", new[] { "This account doesn't have the card", "このアカウントにそのカードはありません" } },
            { "來源和目標是同一個帳號", new[] { "Source and target are the same account", "コピー元とコピー先が同じアカウントです" } },
            { "搬移 {0} 個 session：{1} → {2}", new[] { "Move {0} sessions: {1} → {2}", "{0} 件のセッションを移動：{1} → {2}" } },
            { "複製 {0} 個 session：{1} → {2}", new[] { "Copy {0} sessions: {1} → {2}", "{0} 件のセッションをコピー：{1} → {2}" } },
            { "從 {0} 移除 {1} 個 session", new[] { "Remove {1} sessions from {0}", "{0} から {1} 件のセッションを外す" } },
            { "至少要選兩個帳號", new[] { "Choose at least two accounts", "アカウントを 2 つ以上選んでください" } },
            { "在這個帳號被刪除過", new[] { "Deleted in this account before", "このアカウントで削除済み" } },
            { "對話檔已遺失，搬過去也接不回", new[] { "Conversation file is missing; it couldn't be resumed there", "会話ファイルがないため、移しても再開できません" } },
            { "完整同步：{0} 項", new[] { "Full sync: {0} changes", "完全同期：{0} 件" } },
            { "永久刪除 {0} 個 session 的對話", new[] { "Delete conversations of {0} sessions for good", "{0} 件のセッションの会話を完全に削除" } },
            { "無法寫入 {0}（Windows 錯誤 {1}）", new[] { "Cannot write {0} (Windows error {1})", "{0} に書き込めません（Windows エラー {1}）" } },
            { "紀錄編號不正確", new[] { "Invalid history id", "履歴 ID が正しくありません" } },
            { "這筆操作已經復原過了", new[] { "This operation was already undone", "この操作はすでに元に戻されています" } },
            { "永久刪除無法復原", new[] { "Permanent deletes cannot be undone", "完全削除は元に戻せません" } },
            { "之後有「永久刪除」動過同一批檔案，這筆已經無法復原", new[] { "A later permanent delete touched the same files, so this can no longer be undone", "後で「完全削除」が同じファイルを変更したため、これは元に戻せません" } },
            { "請先復原較新的操作：{0}", new[] { "Undo the newer operations first: {0}", "先に新しい操作を元に戻してください：{0}" } },
            { "檔案操作失敗：{0}", new[] { "File operation failed: {0}", "ファイル操作に失敗しました：{0}" } },
            { "有帳號的這張卡片讀不了（可能正在寫入），請稍後再試", new[] { "An account's copy of this card can't be read (it may be being written); try again shortly", "あるアカウントのこのカードが読めません（書き込み中の可能性があります）。少ししてから再度お試しください" } },
            { "對話檔編號不正確，為了安全不刪除", new[] { "The conversation id is invalid; not deleting anything to be safe", "会話 ID が正しくないため、安全のため削除しません" } },
            { "有 {0} 個檔案在這次操作之後又被改過", new[] { "{0} files changed after this operation", "この操作のあとで {0} 個のファイルが変更されています" } },
            { "JSON 不是物件", new[] { "JSON is not an object", "JSON がオブジェクトではありません" } },
            { "JSON 格式不正確，位置 {0}", new[] { "Malformed JSON at position {0}", "JSON の形式が正しくありません（位置 {0}）" } },
            { "JSON 提早結束", new[] { "JSON ended early", "JSON が途中で終わっています" } },
            { "JSON 預期 '{0}'，位置 {1}", new[] { "JSON expected '{0}' at position {1}", "JSON で '{0}' が必要です（位置 {1}）" } },

            // ---- v0.2: read-only guard, orphans, redo, restart ----
            { "標題", new[] { "Title", "タイトル" } },
            { "最後修改", new[] { "Modified", "更新日時" } },
            { "大小", new[] { "Size", "サイズ" } },
            { "沒在清單裡的對話", new[] { "Conversations not in any list", "一覧にない会話" } },
            { "這些對話檔在電腦上，但不在任何桌面版帳號的清單裡。可能是在命令列（Claude Code CLI）或 VS Code 用過的對話，或是清單被清掉的舊 session。",
                new[] { "These conversation files are on this PC but in no desktop account's list. They may come from the command line (Claude Code CLI) or VS Code, or be old sessions whose list entry was cleared.",
                        "これらの会話ファイルはこのパソコンにありますが、どのデスクトップアカウントの一覧にもありません。コマンドライン（Claude Code CLI）や VS Code で使った会話か、一覧から消えた古いセッションの可能性があります。" } },
            { "「加進帳號」是實驗功能：用那個帳號最新的一張卡片當範本產生新卡片。重開 Claude 後確認能不能接著聊，不行就到操作紀錄復原。",
                new[] { "\"Add to account\" is experimental: it makes a new card using that account's newest card as a template. After restarting Claude, check that you can continue the chat; if not, undo it in History.",
                        "「アカウントに追加」は試験的な機能です。そのアカウントの最新のカードをひな形に新しいカードを作ります。Claude を再起動して会話を続けられるか確認し、だめなら操作履歴から元に戻してください。" } },
            { "永久刪除…", new[] { "Delete for good…", "完全に削除…" } },
            { "加進帳號…", new[] { "Add to account…", "アカウントに追加…" } },
            { "加進帳號", new[] { "Add to account", "アカウントに追加" } },
            { "顯示檔案", new[] { "Show file", "ファイルを表示" } },
            { "(沒有標題)", new[] { "(no title)", "(タイトルなし)" } },
            { "先勾選要處理的對話。", new[] { "Check the conversations to act on first.", "先に対象の会話にチェックを付けてください。" } },
            { "重開 Claude", new[] { "Restart Claude", "Claude を再起動" } },
            { "偵測到不認得的 Claude 資料格式（{0}），為了安全目前只能瀏覽、不能修改。Claude 更新後可能會這樣，請等本程式更新。",
                new[] { "Claude's files don't look as expected ({0}), so to be safe you can only browse, not change anything. This can happen after a Claude update; please wait for an update of this app.",
                        "Claude のデータ形式が想定と違います（{0}）。安全のため閲覧のみで、変更はできません。Claude のアップデート後に起こることがあります。本アプリの更新をお待ちください。" } },
            { "唯讀模式：{0}", new[] { "Read-only: {0}", "閲覧専用：{0}" } },
            { "沒在清單裡的對話（{0}）", new[] { "Conversations not in any list ({0})", "一覧にない会話（{0}）" } },
            { "已重做：{0}", new[] { "Redone: {0}", "やり直しました：{0}" } },
            { "重做", new[] { "Redo", "やり直す" } },
            { "永久刪除這 {0} 個沒在清單裡的對話？", new[] { "Permanently delete these {0} conversations that are in no list?", "一覧にないこの {0} 件の会話を完全に削除しますか？" } },
            { "會刪掉對話檔、附屬資料夾和 file-history 備份。如果它們是命令列或 VS Code 還要用的對話，那邊也會跟著消失。",
                new[] { "This deletes the conversation files, their side folders and file-history backups. If the command line or VS Code still uses them, they disappear there too.",
                        "会話ファイル、付随フォルダー、file-history のバックアップを削除します。コマンドラインや VS Code でまだ使っている会話なら、そちらからも消えます。" } },
            { "已永久刪除 {0} 個對話", new[] { "Permanently deleted {0} conversations", "{0} 件の会話を完全に削除しました" } },
            { "把 {0} 個對話加進「{1}」的清單？\n\n這是實驗功能：會用這個帳號最新的一張卡片當範本產生新卡片。重開 Claude 後確認能不能接著聊；不行的話，可以在操作紀錄復原。",
                new[] { "Add {0} conversations to the list of “{1}”?\n\nThis is experimental: a new card is made from this account's newest card as a template. After restarting Claude, check that you can continue the chat; if not, undo it in History.",
                        "{0} 件の会話を「{1}」の一覧に追加しますか？\n\n試験的な機能です。このアカウントの最新のカードをひな形に新しいカードを作ります。Claude を再起動して会話を続けられるか確認し、だめなら操作履歴から元に戻してください。" } },
            { "重開 Claude 會中斷正在進行的 session（包括正在回答到一半的）。要重開嗎？", new[] { "Restarting Claude interrupts running sessions (including replies in progress). Restart?", "Claude を再起動すると、実行中のセッション（回答の途中のものも含む）が中断されます。再起動しますか？" } },
            { "Claude 沒有在 10 秒內關閉（可能縮到系統匣了）。要強制結束嗎？", new[] { "Claude didn't close within 10 seconds (it may have gone to the tray). Force it to quit?", "Claude が 10 秒以内に終了しませんでした（タスクトレイにある可能性があります）。強制終了しますか？" } },
            { "已重開 Claude", new[] { "Restarted Claude", "Claude を再起動しました" } },
            { "已關閉 Claude，請自己再打開它", new[] { "Closed Claude; please open it again yourself", "Claude を終了しました。手動で起動し直してください" } },
            { "archived-sessions.idx 的版本是 {0}", new[] { "archived-sessions.idx is version {0}", "archived-sessions.idx のバージョンが {0} です" } },
            { "{0} 張卡片的格式和預期不同", new[] { "{0} cards are not in the expected format", "{0} 枚のカードが想定と違う形式です" } },
            { "已經不是沒在清單裡的對話", new[] { "It is no longer a conversation outside the lists", "すでに一覧にない会話ではありません" } },
            { "最近 3 分鐘內還有寫入，可能正在使用", new[] { "Written in the last 3 minutes; it may be in use", "直近 3 分以内に書き込みがあり、使用中の可能性があります" } },
            { "永久刪除 {0} 個沒在清單裡的對話", new[] { "Permanently delete {0} conversations not in any list", "一覧にない {0} 件の会話を完全に削除" } },
            { "需要至少一張現有的卡片當範本", new[] { "At least one existing card is needed as a template", "ひな形にする既存のカードが 1 枚以上必要です" } },
            { "把 {0} 個對話加進 {1}", new[] { "Add {0} conversations to {1}", "{0} 件の会話を {1} に追加" } },
            { "對話檔裡找不到工作資料夾", new[] { "The conversation file names no working folder", "会話ファイルに作業フォルダーがありません" } },
            { "復原：{0}", new[] { "Undo: {0}", "元に戻す：{0}" } },

            // ---- time ----
            { "剛剛", new[] { "just now", "たった今" } },
            { "{0} 分鐘前", new[] { "{0} minutes ago", "{0} 分前" } },
            { "今天 {0}", new[] { "today {0}", "今日 {0}" } },
            { "昨天 {0}", new[] { "yesterday {0}", "昨日 {0}" } },
        };
    }
}
