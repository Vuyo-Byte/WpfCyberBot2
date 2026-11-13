using System;
using System.Collections.Generic;
using System.Linq;

namespace CyberChatBot
{
    public class ChatBot : ChatParticipant
    {
        private readonly Random _random = new Random();
        private readonly DatabaseHelper _db;
        private readonly ActivityLog _log;

        // The window listens to these so the bot can drive the GUI.
        public event Action<int>? ShowTabRequested;   // 0 = Tasks, 1 = Quiz, 2 = Activity Log
        public event Action? TasksChanged;            // refresh the task list
        public event Action? QuizRequested;           // start the quiz

        // ----- conversation memory for multi-step chats -----
        // e.g. "Add task ..."  ->  "Would you like a reminder?"  ->  "Yes, in 3 days"
        private enum Pending { None, ReminderDecision, ReminderTime, TaskTitle }

        private Pending _pending = Pending.None;
        private TaskItem? _pendingTask;
        private int _retries;
        private bool _pendingReminderRequest;
        private DateTime? _pendingWhen;
        private string _pendingPhrase = "";
        private bool _lastReplyWasLog;

        private readonly string[,] _tipsDatabase =
        {
            {
                "Tip 1: Use strong, unique passwords for every account.",
                "Tip 2: Enable two-factor authentication whenever possible.",
                "Tip 3: Watch out for phishing emails and suspicious links."
            },
            {
                "Tip 4: Keep your software and operating system updated.",
                "Tip 5: Use reputable antivirus software and scan regularly.",
                "Tip 6: Avoid sensitive transactions on public Wi-Fi."
            },
            {
                "Tip 7: Back up important data to an external drive or cloud.",
                "Tip 8: Only grant apps the permissions they actually need.",
                "Tip 9: Learn about cybersecurity threats and best practices."
            }
        };

        public ChatBot(string name, DatabaseHelper db, ActivityLog log) : base(name)
        {
            _db = db;
            _log = log;
        }

        // =====================================================================
        // MAIN ENTRY POINT - every message from the user comes through here
        // =====================================================================
        public void RespondTo(string input)
        {
            string message = input.Trim();
            string lower = message.ToLowerInvariant();

            // "show more" right after the activity log shows the full history.
            bool followUpOnLog = _lastReplyWasLog &&
                (lower == "show more" || lower == "more" || lower == "show all" || lower == "show full history");
            _lastReplyWasLog = false;

            if (followUpOnLog)
            {
                ShowActivityLog(true);
                return;
            }

            // Are we in the middle of a multi-step conversation?
            if (_pending != Pending.None && HandlePending(message, lower))
                return;

            if (lower == "exit" || lower == "quit")
            {
                Speak("Goodbye! Stay safe online.");
                return;
            }

            // ----- NLP: work out what the user wants from their words -----
            switch (NlpProcessor.DetectIntent(lower))
            {
                case Intent.ShowLog:
                    ShowActivityLog(NlpProcessor.Tokens(lower).Any(w => w == "all" || w == "full" || w == "more" || w == "everything"));
                    return;

                case Intent.AddTask:
                    AddTaskFromChat(message, false);
                    return;

                case Intent.SetReminder:
                    AddTaskFromChat(message, true);
                    return;

                case Intent.DeleteTask:
                    ChangeTaskFromChat(message, true);
                    return;

                case Intent.CompleteTask:
                    ChangeTaskFromChat(message, false);
                    return;

                case Intent.ShowTasks:
                    ShowTasks();
                    return;

                case Intent.StartQuiz:
                    _log.Add("NLP: understood your message as a request to start the quiz");
                    Speak("Let's test your cybersecurity knowledge! I've opened the Quiz tab for you.");
                    ShowTabRequested?.Invoke(1);
                    QuizRequested?.Invoke();
                    return;
            }

            // ----- Part 1 / Part 2 conversation (unchanged behaviour) -----
            if (lower.Contains("your name"))
            {
                Speak("I'm CyberBot, your cybersecurity assistant.");
                return;
            }

            if (lower == "hi" || lower == "hello" ||
                lower.StartsWith("hey"))
            {
                Speak("Hello! How can I help you stay safe online?");
                return;
            }

            if (lower.Contains("how are you"))
            {
                Speak("I'm running smoothly! What would you like to learn today?");
                return;
            }

            if (lower.Contains("thank"))
            {
                Speak("You're welcome! Staying safe online starts with good habits.");
                return;
            }

            if (lower.Contains("help") ||
                lower.Contains("what can you do"))
            {
                Speak("I can help with:\n" +
                      "- Cybersecurity tips: Passwords, Phishing, Malware, Updates, Antivirus, Wi-Fi, Backup, Permissions, Education\n" +
                      "- Tasks and reminders: 'Add task - Review privacy settings', 'Remind me to update my password tomorrow', " +
                      "'Show my tasks', 'Complete task 2', 'Delete task 2'\n" +
                      "- Quiz: say 'Start quiz'\n" +
                      "- Activity log: say 'Show activity log' or 'What have you done for me?'");
                return;
            }

            // Cybersecurity tips, found by keyword (typos and synonyms allowed).
            if (NlpProcessor.FindTipRow(lower, out string keyword) >= 0)
            {
                _log.Add($"NLP: detected the keyword '{keyword}' and gave a cybersecurity tip");
                GiveTip(message);
                return;
            }

            if (lower.Contains("worried") ||
                lower.Contains("scared") ||
                lower.Contains("hacked"))
            {
                Speak("If you think an account has been compromised, " +
                      "change its password from a trusted device, " +
                      "enable two-factor authentication, and check its " +
                      "official account recovery guidance.");
                return;
            }

            Speak("I didn't quite understand that. Could you rephrase? " +
                  "You can ask about a topic (e.g. passwords or phishing), " +
                  "say 'add a task', 'start the quiz' or 'show activity log', or type Help.");
        }

        public void GiveTip(string category)
        {
            int row = NlpProcessor.FindTipRow(category.ToLowerInvariant(), out string keyword);

            if (row == -1)
            {
                Speak("I didn't recognise that topic. Try Password, Phishing, " +
                      "Malware, Updates, Antivirus, Wi-Fi, Backup, " +
                      "Permissions or Education.");
                return;
            }

            Speak(_tipsDatabase[row, NextTipIndex(row)]);
        }

        // Shuffle-bag: every tip in a topic is shown once, in random order,
        // before any tip repeats. The same tip is never shown twice in a row.
        private readonly Dictionary<int, List<int>> _tipBags = new Dictionary<int, List<int>>();
        private int _lastTipRow = -1;
        private int _lastTipIndex = -1;

        private int NextTipIndex(int row)
        {
            if (!_tipBags.TryGetValue(row, out List<int>? bag) || bag.Count == 0)
            {
                bag = Enumerable.Range(0, 3).OrderBy(_ => _random.Next()).ToList();

                // A fresh bag must not start with the tip we just gave.
                if (_lastTipRow == row && bag[0] == _lastTipIndex)
                {
                    int last = bag.Count - 1;
                    (bag[0], bag[last]) = (bag[last], bag[0]);
                }

                _tipBags[row] = bag;
            }

            int index = bag[0];
            bag.RemoveAt(0);

            _lastTipRow = row;
            _lastTipIndex = index;
            return index;
        }

        // =====================================================================
        // TASK ASSISTANT (chat side)
        // =====================================================================

        private bool RequireDb()
        {
            if (_db.IsAvailable)
                return true;

            Speak("I can't reach the MySQL task database right now, so I can't manage tasks. " +
                  "Please start MySQL, check DbConfig.cs, then restart me (see the README).");
            return false;
        }

        private void AddTaskFromChat(string message, bool reminderRequest)
        {
            if (!RequireDb())
                return;

            bool hasTime = NlpProcessor.TryParseTime(message, out DateTime when, out string phrase, out string matched);
            string subject = NlpProcessor.ExtractSubject(message, hasTime ? matched : "");

            // "Remind me tomorrow" - no subject yet, so ask for it.
            if (subject.Length == 0)
            {
                _pending = Pending.TaskTitle;
                _pendingReminderRequest = reminderRequest;
                _pendingWhen = hasTime ? when : (DateTime?)null;
                _pendingPhrase = phrase;
                _retries = 0;

                Speak(reminderRequest
                    ? "Happy to set a reminder! What should I remind you about?"
                    : "Sure! What should the task be? For example: 'Review privacy settings'.");
                return;
            }

            CreateTask(subject, hasTime ? when : (DateTime?)null, phrase, reminderRequest);
        }

        private void CreateTask(string subject, DateTime? when, string phrase, bool reminderRequest)
        {
            // Known topics (2FA, privacy, password...) get a proper title and description.
            TaskTopic? topic = NlpProcessor.FindTopic(subject.ToLowerInvariant());

            // Short requests ("enable 2FA") get the full topic title; longer, more specific
            // requests ("Report suspicious emails") keep the user's own words as the title.
            bool useTopicTitle = topic != null && subject.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3;

            string title = useTopicTitle ? topic!.Title : NlpProcessor.Capitalise(subject);
            string description = topic?.Description ?? $"Cybersecurity task: {title}.";

            if (title.Length > 150) title = title.Substring(0, 150);
            if (description.Length > 500) description = description.Substring(0, 500);

            int id = _db.AddTask(title, description, when);

            if (id < 0)
            {
                Speak("Sorry, I couldn't save that task: " + _db.LastError);
                return;
            }

            TasksChanged?.Invoke();
            ShowTabRequested?.Invoke(0);

            // A time was given in the same sentence, so the reminder is set straight away.
            if (when.HasValue)
            {
                _log.Add($"Task added: '{title}' (reminder set for {when.Value:dd MMM yyyy})");

                if (reminderRequest)
                    Speak($"Reminder set for '{title}' {NlpProcessor.SpokenWhen(phrase)}.");
                else
                    Speak($"Task added: '{title}'. Got it! I'll remind you {NlpProcessor.SpokenWhen(phrase)}.");

                return;
            }

            _log.Add($"Task added: '{title}' (no reminder set)");

            _pendingTask = new TaskItem { Id = id, Title = title };
            _retries = 0;

            if (reminderRequest)
            {
                _pending = Pending.ReminderTime;
                Speak($"Okay! I've added '{title}' to your tasks. When should I remind you? " +
                      "(try 'tomorrow', 'in 3 days' or 'next week')");
            }
            else
            {
                _pending = Pending.ReminderDecision;
                Speak($"Task added with the description \"{description}\" Would you like a reminder?");
            }
        }

        // Handles the user's answer to "Would you like a reminder?" / "When?" / "What should the task be?"
        // Returns false if the user changed the subject (so normal processing carries on).
        private bool HandlePending(string message, string lower)
        {
            switch (_pending)
            {
                case Pending.ReminderDecision:
                    if (NlpProcessor.TryParseTime(message, out DateTime w1, out string p1, out _))
                    {
                        ApplyReminder(w1, p1);
                        return true;
                    }

                    if (NlpProcessor.IsNo(lower))
                    {
                        Speak($"No problem. '{_pendingTask?.Title}' is saved without a reminder.");
                        ClearPending();
                        return true;
                    }

                    if (NlpProcessor.IsYes(lower))
                    {
                        _pending = Pending.ReminderTime;
                        _retries = 0;
                        Speak("When should I remind you? Try 'tomorrow', 'in 3 days' or 'next week'.");
                        return true;
                    }

                    ClearPending();   // something else entirely - handle it normally
                    return false;

                case Pending.ReminderTime:
                    if (NlpProcessor.TryParseTime(message, out DateTime w2, out string p2, out _))
                    {
                        ApplyReminder(w2, p2);
                        return true;
                    }

                    if (NlpProcessor.IsNo(lower))
                    {
                        Speak($"Okay, '{_pendingTask?.Title}' stays without a reminder.");
                        ClearPending();
                        return true;
                    }

                    if (NlpProcessor.DetectIntent(lower) != Intent.None ||
                        NlpProcessor.FindTipRow(lower, out _) >= 0)
                    {
                        ClearPending();
                        return false;
                    }

                    _retries++;
                    if (_retries >= 2)
                    {
                        Speak($"I couldn't work out the date, so '{_pendingTask?.Title}' is saved without a reminder. " +
                              "You can say 'remind me to ... in 3 days' any time.");
                        ClearPending();
                        return true;
                    }

                    Speak("Sorry, I didn't catch when. Try 'tomorrow', 'in 3 days', 'next Friday' or a date like 2026-12-01.");
                    return true;

                case Pending.TaskTitle:
                    if (NlpProcessor.IsNo(lower))
                    {
                        Speak("Okay, cancelled.");
                        ClearPending();
                        return true;
                    }

                    if (NlpProcessor.DetectIntent(lower) != Intent.None)
                    {
                        ClearPending();
                        return false;
                    }

                    bool hasTime = NlpProcessor.TryParseTime(message, out DateTime w3, out string p3, out string matched);
                    string subject = NlpProcessor.ExtractSubject(message, hasTime ? matched : "");

                    if (subject.Length == 0)
                    {
                        _retries++;
                        if (_retries >= 2)
                        {
                            Speak("No worries, we can do this later.");
                            ClearPending();
                        }
                        else
                        {
                            Speak("Please tell me what the task is about, e.g. 'Update my password'.");
                        }
                        return true;
                    }

                    DateTime? whenToUse = hasTime ? w3 : _pendingWhen;
                    string phraseToUse = hasTime ? p3 : _pendingPhrase;
                    bool reminderRequest = _pendingReminderRequest;

                    ClearPending();
                    CreateTask(subject, whenToUse, phraseToUse, reminderRequest);
                    return true;
            }

            ClearPending();
            return false;
        }

        private void ApplyReminder(DateTime when, string phrase)
        {
            if (_pendingTask == null)
            {
                ClearPending();
                return;
            }

            string title = _pendingTask.Title;

            if (!_db.SetReminder(_pendingTask.Id, when))
            {
                Speak("Sorry, I couldn't save that reminder: " + _db.LastError);
                ClearPending();
                return;
            }

            _log.Add($"Reminder set for '{title}' on {when:dd MMM yyyy} (task updated)");
            Speak($"Got it! I'll remind you {NlpProcessor.SpokenWhen(phrase)}.");

            ClearPending();
            TasksChanged?.Invoke();
        }

        private void ClearPending()
        {
            _pending = Pending.None;
            _pendingTask = null;
            _pendingWhen = null;
            _pendingPhrase = "";
            _retries = 0;
        }

        private void ShowTasks()
        {
            if (!RequireDb())
                return;

            _log.Add("NLP: understood your message as a request to view your tasks");
            ShowTabRequested?.Invoke(0);

            List<TaskItem> tasks = _db.GetTasks();

            if (tasks.Count == 0)
            {
                Speak("You don't have any tasks yet. Try: 'Add task - Review privacy settings'.");
                return;
            }

            Speak("Here are your tasks:\n" + string.Join("\n", tasks.Select(DescribeTask)));
        }

        // delete = true -> delete the task; delete = false -> mark it completed.
        private void ChangeTaskFromChat(string message, bool delete)
        {
            if (!RequireDb())
                return;

            List<TaskItem> tasks = _db.GetTasks();

            if (tasks.Count == 0)
            {
                Speak("You don't have any tasks yet.");
                return;
            }

            // When completing, only look at tasks that are still pending.
            List<TaskItem> searchIn = tasks;
            if (!delete && tasks.Any(t => !t.IsCompleted))
                searchIn = tasks.Where(t => !t.IsCompleted).ToList();

            TaskItem? task = NlpProcessor.FindTaskReference(message, searchIn, out bool ambiguous);

            if (task == null)
            {
                ShowTabRequested?.Invoke(0);
                Speak((ambiguous
                          ? "More than one task matches that. "
                          : "Which task do you mean? ") +
                      "Tell me its number, e.g. '" + (delete ? "delete" : "complete") + " task 2'.\n" +
                      string.Join("\n", tasks.Select(DescribeTask)));
                return;
            }

            if (delete)
            {
                if (!_db.DeleteTask(task.Id))
                {
                    Speak("Sorry, I couldn't delete that task: " + _db.LastError);
                    return;
                }

                _log.Add($"Task deleted: '{task.Title}'");
                Speak($"Done! I've deleted the task '{task.Title}'.");
            }
            else
            {
                if (task.IsCompleted)
                {
                    Speak($"'{task.Title}' is already marked as completed.");
                    return;
                }

                if (!_db.MarkCompleted(task.Id))
                {
                    Speak("Sorry, I couldn't update that task: " + _db.LastError);
                    return;
                }

                _log.Add($"Task completed: '{task.Title}'");
                Speak($"Nice work! I've marked '{task.Title}' as completed.");
            }

            TasksChanged?.Invoke();
            ShowTabRequested?.Invoke(0);
        }

        private static string DescribeTask(TaskItem t)
        {
            string reminder = t.ReminderDate.HasValue
                ? $", reminder {t.ReminderDate.Value:dd MMM yyyy}"
                : "";

            return $"#{t.Id} {t.Title} - \"{t.Description}\" [{(t.IsCompleted ? "completed" : "pending")}{reminder}]";
        }

        // =====================================================================
        // ACTIVITY LOG (chat side)
        // =====================================================================

        private void ShowActivityLog(bool showAll)
        {
            ShowTabRequested?.Invoke(2);

            List<ActivityEntry> entries = showAll ? _log.All() : _log.Recent(10);

            if (entries.Count == 0)
            {
                Speak("I haven't done anything yet. Try adding a task or starting the quiz!");
                return;
            }

            var lines = entries.Select((e, i) => $"{i + 1}. {e.Description} ({e.Time:dd MMM HH:mm})");
            string reply = "Here's a summary of recent actions:\n" + string.Join("\n", lines);

            if (!showAll && _log.Count > 10)
            {
                reply += "\nSay 'show more' to see the full history.";
                _lastReplyWasLog = true;
            }

            Speak(reply);
        }
    }
}
