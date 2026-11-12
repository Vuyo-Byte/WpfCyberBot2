using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CyberChatBot
{
    /// <summary>What the user is trying to do, worked out from their words.</summary>
    public enum Intent
    {
        None,
        ShowLog,
        AddTask,
        SetReminder,
        DeleteTask,
        CompleteTask,
        ShowTasks,
        StartQuiz
    }

    /// <summary>A known cybersecurity topic with a ready-made task title and description.</summary>
    public class TaskTopic
    {
        public string[] Keywords { get; }
        public string Title { get; }
        public string Description { get; }

        public TaskTopic(string[] keywords, string title, string description)
        {
            Keywords = keywords;
            Title = title;
            Description = description;
        }
    }

    /// <summary>
    /// Task 3: NLP simulation using simple string manipulation.
    ///
    /// Techniques used:
    ///  1. Tokenising   - split the message into lower-case words (Regex).
    ///  2. Keyword and synonym detection - "remind", "reminder", "task", "quiz",
    ///     "2FA", "two-factor", "phishing" ... mean something even if the
    ///     sentence around them is different.
    ///  3. Fuzzy matching (Levenshtein distance) - "pasword" or "fishing" still
    ///     match "password" / "phishing", so small typos do not break the bot.
    ///  4. Regular expressions - to find dates/timeframes such as "tomorrow",
    ///     "in 3 days", "next Friday" or "25 December", and to strip the command
    ///     words so only the real subject of the task is left.
    /// </summary>
    public static class NlpProcessor
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // =================================================================
        // 1. TOKENISING + FUZZY MATCHING
        // =================================================================

        public static List<string> Tokens(string lower)
        {
            return Regex.Matches(lower, @"[a-z0-9'\-]+")
                        .Select(m => m.Value.Trim('-', '\''))
                        .Where(t => t.Length > 0)
                        .ToList();
        }

        public static int Levenshtein(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }

        /// <summary>
        /// Does this word count as the keyword? Exact match always counts.
        /// For keywords of 5+ letters we also accept endings ("passwords"),
        /// and one or two typos ("pasword").
        /// </summary>
        public static bool WordMatches(string token, string keyword)
        {
            if (token == keyword) return true;
            if (keyword.Length < 5) return false;
            if (token.StartsWith(keyword, StringComparison.Ordinal)) return true;
            if (Math.Abs(token.Length - keyword.Length) > 2) return false;

            int allowedTypos = keyword.Length >= 8 ? 2 : 1;
            return Levenshtein(token, keyword) <= allowedTypos;
        }

        private static bool KeywordHit(string lower, List<string> tokens, string keyword)
        {
            // Multi-word / hyphenated keywords use string.Contains.
            if (keyword.Contains(' ') || keyword.Contains('-'))
                return lower.Contains(keyword);

            return tokens.Any(t => WordMatches(t, keyword));
        }

        // =================================================================
        // 2. INTENT DETECTION (what does the user want?)
        // =================================================================

        public static Intent DetectIntent(string lower)
        {
            List<string> words = Tokens(lower);

            bool Has(params string[] list) => words.Any(w => list.Contains(w));
            bool Stem(string stem) => words.Any(w => w.StartsWith(stem, StringComparison.Ordinal));

            // --- Activity log: "show activity log", "what have you done for me?" ---
            if (lower.Contains("activity log") ||
                lower.Contains("action log") ||
                lower.Contains("what have you done") ||
                lower.Contains("what did you do") ||
                lower.Contains("recent actions") ||
                (Has("log", "history") && Has("show", "view", "display", "see", "open", "get")))
            {
                return Intent.ShowLog;
            }

            bool remindVerb = Has("remind", "reminds");
            bool reminderNoun = Has("reminder", "reminders");
            bool task = Stem("task") || Has("todo", "to-do", "todos");

            bool adding = Has("add", "create", "make", "new", "schedule", "put");
            bool showing = Has("show", "view", "list", "see", "display", "open", "check");
            bool deleting = Has("delete", "remove", "erase", "discard", "cancel", "clear");
            bool completing = Has("complete", "completed", "finish", "finished", "done", "tick");

            // "Remind me to ...", "Can you remind me to ..."
            if (remindVerb)
                return Intent.SetReminder;

            if (reminderNoun || task)
            {
                if (adding || (reminderNoun && Has("set", "give", "get")))
                    return reminderNoun ? Intent.SetReminder : Intent.AddTask;

                if (deleting) return Intent.DeleteTask;
                if (completing) return Intent.CompleteTask;

                if (showing || Has("my", "all", "what", "which", "pending", "outstanding", "current", "have"))
                    return Intent.ShowTasks;
            }

            // --- Quiz ---
            if (Has("quiz", "quizzes", "quizz", "trivia", "game", "games", "mini-game", "minigame", "play") ||
                lower.Contains("test me") ||
                lower.Contains("test my") ||
                lower.Contains("challenge me"))
            {
                return Intent.StartQuiz;
            }

            return Intent.None;
        }

        public static bool IsYes(string lower)
        {
            List<string> words = Tokens(lower);

            return words.Any(w => new[] { "yes", "yeah", "yep", "yup", "sure", "ok", "okay", "please", "y", "definitely", "absolutely" }.Contains(w))
                   || lower.Contains("go ahead")
                   || lower.Contains("set one")
                   || lower.Contains("remind me");
        }

        public static bool IsNo(string lower)
        {
            List<string> words = Tokens(lower);

            return words.Any(w => new[] { "no", "nope", "nah", "n", "never", "skip", "cancel" }.Contains(w))
                   || lower.Contains("no thanks")
                   || lower.Contains("not now")
                   || lower.Contains("don't")
                   || lower.Contains("do not");
        }

        // =================================================================
        // 3. CYBERSECURITY TOPICS (tips + task templates)
        // =================================================================

        // Rows match the rows of the tips table in ChatBot.cs.
        private static readonly string[][] TipKeywords =
        {
            new[] { "password", "passphrase", "phishing", "scam", "scams", "email", "emails", "link", "links",
                    "2fa", "mfa", "two-factor", "two factor", "authentication", "login", "privacy" },
            new[] { "malware", "antivirus", "update", "updates", "patch", "wifi", "wi-fi", "virus",
                    "ransomware", "firewall", "vpn", "trojan", "spyware" },
            new[] { "backup", "backups", "back up", "permission", "permissions", "education" }
        };

        // Which of the 3 tips in the row best fits the keyword (0, 1 or 2).
        private static readonly Dictionary<string, int> TipIndexByKeyword = new Dictionary<string, int>
        {
            { "password", 0 }, { "passphrase", 0 }, { "login", 0 },
            { "2fa", 1 }, { "mfa", 1 }, { "two-factor", 1 }, { "two factor", 1 }, { "authentication", 1 },
            { "phishing", 2 }, { "scam", 2 }, { "scams", 2 }, { "email", 2 }, { "emails", 2 }, { "link", 2 }, { "links", 2 },
            { "update", 0 }, { "updates", 0 }, { "patch", 0 },
            { "antivirus", 1 }, { "malware", 1 }, { "virus", 1 }, { "ransomware", 1 },
            { "wifi", 2 }, { "wi-fi", 2 },
            { "backup", 0 }, { "backups", 0 }, { "back up", 0 },
            { "permission", 1 }, { "permissions", 1 },
            { "education", 2 }
        };

        /// <summary>Finds the tip row (0-2) for a message, or -1. Also returns the keyword that matched.</summary>
        public static int FindTipRow(string lower, out string keyword)
        {
            List<string> tokens = Tokens(lower);

            for (int row = 0; row < TipKeywords.Length; row++)
            {
                foreach (string kw in TipKeywords[row])
                {
                    if (KeywordHit(lower, tokens, kw))
                    {
                        keyword = kw;
                        return row;
                    }
                }
            }

            keyword = "";
            return -1;
        }

        /// <summary>Which of the 3 tips in the row to show for this keyword (-1 = pick randomly).</summary>
        public static int TipIndexFor(string keyword)
        {
            return TipIndexByKeyword.TryGetValue(keyword, out int index) ? index : -1;
        }

        private static readonly TaskTopic[] Topics =
        {
            new TaskTopic(
                new[] { "2fa", "mfa", "two-factor", "two factor", "authentication", "authenticator" },
                "Enable two-factor authentication",
                "Turn on two-factor authentication (2FA) for your important accounts to add a second layer of protection."),

            new TaskTopic(
                new[] { "privacy" },
                "Review privacy settings",
                "Review account privacy settings to ensure your data is protected."),

            new TaskTopic(
                new[] { "password", "passphrase" },
                "Update my password",
                "Update your password to a strong, unique one and never reuse it on other accounts."),

            new TaskTopic(
                new[] { "backup", "backups", "back up" },
                "Back up important data",
                "Back up your important files to an external drive or the cloud so you can recover from loss or ransomware."),

            new TaskTopic(
                new[] { "antivirus", "virus", "malware", "scan" },
                "Run an antivirus scan",
                "Run a full antivirus scan to check your device for malware."),

            new TaskTopic(
                new[] { "wifi", "wi-fi", "router" },
                "Secure my Wi-Fi",
                "Change the default router password and use WPA2/WPA3 encryption on your Wi-Fi network."),

            new TaskTopic(
                new[] { "phishing", "scam", "scams", "suspicious" },
                "Review phishing awareness",
                "Learn how to spot phishing emails and suspicious links before you click them."),

            new TaskTopic(
                new[] { "update", "updates", "patch", "software" },
                "Install software updates",
                "Install the latest operating system and app updates to fix known security weaknesses."),

            new TaskTopic(
                new[] { "permission", "permissions" },
                "Review app permissions",
                "Check which apps can use your camera, microphone and location, and remove access they do not need.")
        };

        /// <summary>Finds a known topic in the text (so "enable 2FA" becomes a full 2FA task).</summary>
        public static TaskTopic? FindTopic(string lower)
        {
            List<string> tokens = Tokens(lower);

            foreach (TaskTopic topic in Topics)
            {
                if (topic.Keywords.Any(kw => KeywordHit(lower, tokens, kw)))
                    return topic;
            }

            return null;
        }

        // =================================================================
        // 4. DATES AND TIMEFRAMES
        // =================================================================

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly string[] MonthNames =
        {
            "january", "february", "march", "april", "may", "june",
            "july", "august", "september", "october", "november", "december"
        };

        private static readonly Dictionary<string, int> NumberWords = new Dictionary<string, int>
        {
            { "a", 1 }, { "an", 1 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 },
            { "five", 5 }, { "six", 6 }, { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }
        };

        private static readonly Regex IsoRx = new Regex(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", Opt);
        private static readonly Regex SlashRx = new Regex(@"\b(\d{1,2})/(\d{1,2})/(\d{4})\b", Opt);

        private static readonly Regex DayMonthRx = new Regex(
            @"\b(\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\b(?:,?\s+(\d{4}))?",
            Opt);

        private static readonly Regex RelativeRx = new Regex(
            @"\b(?:in|after|within)?\s*(\d+|a|an|one|two|three|four|five|six|seven|eight|nine|ten)\s+(minute|min|hour|hr|day|week|month)s?\b",
            Opt);

        private static readonly Regex WeekdayRx = new Regex(
            @"\b(?:(?:next|on|this)\s+)?(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", Opt);

        private static bool TryMakeDate(int year, int month, int day, out DateTime date)
        {
            try
            {
                date = new DateTime(year, month, day);
                return date >= DateTime.Today;   // reminders must not be in the past
            }
            catch (ArgumentOutOfRangeException)
            {
                date = default;
                return false;
            }
        }

        /// <summary>
        /// Looks for a date or timeframe in the text.
        /// 'when'    = the date/time for the reminder,
        /// 'phrase'  = how to say it back ("in 3 days"),
        /// 'matched' = the exact text found (so it can be removed from the task title).
        /// </summary>
        public static bool TryParseTime(string text, out DateTime when, out string phrase, out string matched)
        {
            when = default;
            phrase = "";
            matched = "";

            string t = text.ToLowerInvariant();
            DateTime today = DateTime.Today;
            DateTime date;

            // 2026-11-20
            Match m = IsoRx.Match(t);
            if (m.Success && TryMakeDate(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), out date))
            {
                when = date.AddHours(9);
                phrase = "on " + date.ToString("dd MMM yyyy", Inv);
                matched = m.Value;
                return true;
            }

            // 20/11/2026  (day/month/year)
            m = SlashRx.Match(t);
            if (m.Success && TryMakeDate(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), out date))
            {
                when = date.AddHours(9);
                phrase = "on " + date.ToString("dd MMM yyyy", Inv);
                matched = m.Value;
                return true;
            }

            // 25 December  /  25th Dec 2026
            m = DayMonthRx.Match(t);
            if (m.Success)
            {
                int day = int.Parse(m.Groups[1].Value);
                string monthText = m.Groups[2].Value;
                int month = Array.FindIndex(MonthNames, n => n.StartsWith(monthText.Substring(0, 3), StringComparison.Ordinal)) + 1;
                int year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : today.Year;

                bool ok = TryMakeDate(year, month, day, out date);

                // No year given and the date has passed this year -> use next year.
                if (!ok && !m.Groups[3].Success)
                    ok = TryMakeDate(year + 1, month, day, out date);

                if (ok)
                {
                    when = date.AddHours(9);
                    phrase = "on " + date.ToString("dd MMM yyyy", Inv);
                    matched = m.Value.Trim();
                    return true;
                }
            }

            // in 3 days / 2 weeks / an hour / after 30 minutes
            m = RelativeRx.Match(t);
            if (m.Success)
            {
                string numberText = m.Groups[1].Value;
                int n = int.TryParse(numberText, out int parsed) ? parsed : NumberWords[numberText];
                string unit = m.Groups[2].Value;

                if (n >= 1 && n <= 1000)
                {
                    DateTime now = DateTime.Now;
                    string unitName;

                    switch (unit)
                    {
                        case "minute":
                        case "min":
                            when = now.AddMinutes(n); unitName = "minute"; break;
                        case "hour":
                        case "hr":
                            when = now.AddHours(n); unitName = "hour"; break;
                        case "week":
                            when = now.AddDays(7 * n); unitName = "week"; break;
                        case "month":
                            when = now.AddMonths(n); unitName = "month"; break;
                        default:
                            when = now.AddDays(n); unitName = "day"; break;
                    }

                    phrase = $"in {n} {unitName}{(n == 1 ? "" : "s")}";
                    matched = m.Value.Trim();
                    return true;
                }
            }

            // day after tomorrow
            m = Regex.Match(t, @"\bday after tomorrow\b", Opt);
            if (m.Success)
            {
                when = today.AddDays(2).AddHours(9);
                phrase = "the day after tomorrow";
                matched = m.Value;
                return true;
            }

            // tomorrow
            m = Regex.Match(t, @"\btomorrow\b", Opt);
            if (m.Success)
            {
                when = today.AddDays(1).AddHours(9);
                phrase = "tomorrow";
                matched = m.Value;
                return true;
            }

            // today / tonight
            m = Regex.Match(t, @"\b(today|tonight)\b", Opt);
            if (m.Success)
            {
                when = DateTime.Now.AddHours(1);
                phrase = m.Value;
                matched = m.Value;
                return true;
            }

            // next week / next month
            m = Regex.Match(t, @"\bnext (week|month)\b", Opt);
            if (m.Success)
            {
                when = m.Groups[1].Value == "week"
                    ? today.AddDays(7).AddHours(9)
                    : today.AddMonths(1).AddHours(9);
                phrase = m.Value;
                matched = m.Value;
                return true;
            }

            // next Friday / on Monday
            m = WeekdayRx.Match(t);
            if (m.Success)
            {
                DayOfWeek target = Enum.Parse<DayOfWeek>(m.Groups[1].Value, true);
                int diff = ((int)target - (int)today.DayOfWeek + 7) % 7;
                if (diff == 0) diff = 7;

                when = today.AddDays(diff).AddHours(9);
                phrase = "on " + target;
                matched = m.Value.Trim();
                return true;
            }

            return false;
        }

        /// <summary>How the bot says the time back to the user.</summary>
        public static string SpokenWhen(string phrase)
        {
            if (phrase == "tomorrow") return "on tomorrow's date";
            if (phrase == "today" || phrase == "tonight") return "later " + phrase;
            return phrase;
        }

        // =================================================================
        // 5. PULLING THE TASK OUT OF A SENTENCE
        // =================================================================

        /// <summary>
        /// "Can you remind me to update my password tomorrow?" -> "update my password"
        /// "Add task - Review privacy settings"                -> "Review privacy settings"
        /// </summary>
        public static string ExtractSubject(string original, string matchedTime)
        {
            string s = original;

            if (!string.IsNullOrWhiteSpace(matchedTime))
                s = Regex.Replace(s, Regex.Escape(matchedTime), " ", Opt);

            s = Regex.Replace(s, @"\s+", " ").Trim();

            // Polite openers: "hey, can you please ..."
            string previous;
            do
            {
                previous = s;
                s = Regex.Replace(s,
                    @"^(?:hey|hi|hello|please|pls|ok|okay|cyberbot|can you|could you|would you|will you|i want you to|i want to|i need to|i would like to|i'd like to|lets|let's)\b[,\s]*",
                    "", Opt).Trim();
            }
            while (s != previous);

            // "add a task to", "create a new reminder for", "set a reminder -"
            s = Regex.Replace(s,
                @"^(?:add|create|make|new|set|schedule|put|set up)\s+(?:me\s+)?(?:a\s+|an\s+|the\s+|my\s+)?(?:new\s+)?(?:task|reminder|to-?do|todo)s?\b\s*(?:for|to|about|called|named|:|-|–|—)?\s*",
                "", Opt);

            // "remind me to", "reminder about"
            s = Regex.Replace(s,
                @"^(?:remind\s+me|reminder)\s*(?:to|about|for|that i should|:|-)?\s*",
                "", Opt);

            // Left-over linking words
            s = Regex.Replace(s, @"^(?:to|for|about|:|-|–|—)\s*", "", Opt);

            // Trailing punctuation and filler
            s = Regex.Replace(s, @"\s*[,.?!]*\s*$", "");
            s = Regex.Replace(s, @"\s+(?:please|for me|on|at|by|in|after)$", "", Opt);

            return s.Trim();
        }

        public static string Capitalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // =================================================================
        // 6. FINDING WHICH TASK THE USER MEANS ("delete task 2", "done with the 2FA task")
        // =================================================================

        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            "task", "tasks", "delete", "remove", "complete", "completed", "finish", "finished", "done",
            "mark", "please", "that", "this", "from", "with", "about", "list", "reminder", "reminders",
            "have", "want", "could", "would", "your", "what", "erase", "discard", "cancel", "clear"
        };

        public static TaskItem? FindTaskReference(string message, List<TaskItem> tasks, out bool ambiguous)
        {
            ambiguous = false;
            string lower = message.ToLowerInvariant();

            // 1. A task number: "task 3", "#3", "delete 3"
            Match m = Regex.Match(lower, @"(?<![a-z0-9])#?(\d+)(?![a-z0-9])");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int id))
            {
                TaskItem? byId = tasks.FirstOrDefault(t => t.Id == id);
                if (byId != null) return byId;
            }

            // 2. A known topic: "the 2FA task", "my backup reminder"
            TaskTopic? topic = FindTopic(lower);
            if (topic != null)
            {
                List<TaskItem> hits = tasks.Where(t => FindTopic(t.Title.ToLowerInvariant())?.Title == topic.Title).ToList();
                if (hits.Count == 1) return hits[0];
                if (hits.Count > 1) { ambiguous = true; return null; }
            }

            // 3. Words from the title: "delete the privacy one"
            List<string> messageWords = Tokens(lower).Where(w => w.Length >= 4 && !StopWords.Contains(w)).ToList();
            var scored = tasks
                .Select(t => new
                {
                    Task = t,
                    Score = Tokens(t.Title.ToLowerInvariant())
                                .Where(w => w.Length >= 4)
                                .Count(w => messageWords.Any(mw => WordMatches(mw, w)))
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (scored.Count == 0) return null;

            if (scored.Count > 1 && scored[0].Score == scored[1].Score)
            {
                ambiguous = true;
                return null;
            }

            return scored[0].Task;
        }
    }
}
