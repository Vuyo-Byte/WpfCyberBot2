using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CyberChatBot
{
    public partial class MainWindow : Window
    {
        // ----- Part 1 / 2 -----
        private readonly ChatBot bot;
        private User? user;
        private readonly AudioPlayer audioPlayer;

        private bool waitingForName = true;
        private bool conversationEnded = false;

        // ----- Part 3 -----
        private readonly DatabaseHelper db;
        private readonly ActivityLog activityLog;
        private readonly QuizEngine quiz = new QuizEngine();

        private readonly ObservableCollection<TaskItem> tasks = new ObservableCollection<TaskItem>();
        private readonly ObservableCollection<string> logLines = new ObservableCollection<string>();
        private readonly List<RadioButton> optionButtons = new List<RadioButton>();
        private readonly DispatcherTimer reminderTimer;

        public MainWindow()
        {
            InitializeComponent();

            // --- Database (MySQL) ---
            db = new DatabaseHelper();
            bool databaseReady = db.Initialise();

            // --- Core objects ---
            activityLog = new ActivityLog();
            bot = new ChatBot("CyberBot", db, activityLog);
            user = null;
            audioPlayer = new AudioPlayer();

            // --- Wire the bot to the GUI ---
            bot.MessageSpoken += Bot_MessageSpoken;
            bot.TasksChanged += RefreshTasks;
            bot.QuizRequested += () => StartQuiz(false);
            bot.ShowTabRequested += index => MainTabs.SelectedIndex = index;
            activityLog.Changed += RefreshLog;

            TaskList.ItemsSource = tasks;
            ActivityList.ItemsSource = logLines;
            ReminderCombo.SelectedIndex = 0;

            // --- Database status line on the Tasks tab ---
            if (databaseReady)
            {
                DbStatusText.Text = "● Connected to MySQL (" + DbConfig.Database + ")";
                DbStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xFC, 0x9A));
            }
            else
            {
                DbStatusText.Text = "⚠ Not connected to MySQL. Tasks are disabled. " + db.LastError;
                DbStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }

            // --- Welcome ---
            string soundPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "chatBot.wav");

            audioPlayer.PlaySound(soundPath);

            bot.Speak(
                "Welcome to the Cybersecurity Chatbot! What is your name?");

            if (!databaseReady)
            {
                bot.Speak(
                    "Heads up: I couldn't connect to MySQL, so task features are switched off. " +
                    "Tips, the quiz and the activity log still work. (Details: " + db.LastError + ")");
            }

            // --- Checks every 30 seconds for reminders that have come due ---
            reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            reminderTimer.Tick += (s, e) => CheckDueReminders();
            reminderTimer.Start();

            RefreshTasks();
            RefreshLog();
            MessageInput.Focus();
        }

        // =====================================================================
        // CHAT (Part 1 / 2)
        // =====================================================================

        private void Bot_MessageSpoken(string message)
        {
            AddMessage(message, false);
        }

        private void SendButton_Click(
            object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void MessageInput_KeyDown(
            object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void SendMessage()
        {
            if (conversationEnded)
                return;

            string message = MessageInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(message))
                return;

            AddMessage(
                (user == null ? "You" : user.Name) + ": " + message,
                true);

            MessageInput.Clear();

            if (waitingForName)
            {
                if (message.Length > 50)
                {
                    bot.Speak(
                        "Please enter a name with 50 characters or fewer.");
                    return;
                }

                user = new User(message);
                waitingForName = false;

                bot.Speak(
                    $"Hello, {user.Name}! Nice to meet you.");

                bot.Speak(
                    "I can give cybersecurity tips, manage your security tasks and reminders, " +
                    "run a quiz and keep an activity log. Type Help to see everything I can do.");

                CheckDueReminders();

                MessageInput.Focus();
                return;
            }

            if (message.Equals(
                    "exit", StringComparison.OrdinalIgnoreCase) ||
                message.Equals(
                    "quit", StringComparison.OrdinalIgnoreCase))
            {
                bot.Speak(
                    $"Goodbye, {user!.Name}! Stay safe online.");

                conversationEnded = true;
                MessageInput.IsEnabled = false;
                SendButton.IsEnabled = false;
                return;
            }

            bot.RespondTo(message);

            ChatScroll.ScrollToEnd();
            MessageInput.Focus();
        }

        private void AddMessage(string message, bool isUser)
        {
            Border bubble = new Border
            {
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(
                        isUser ? "#073D52" : "#292F49")),

                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),

                Margin = new Thickness(
                    isUser ? 70 : 0,
                    0,
                    isUser ? 0 : 70,
                    10),

                HorizontalAlignment = isUser
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,

                MaxWidth = 650
            };

            TextBlock text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };

            bubble.Child = text;
            ChatMessages.Children.Add(bubble);

            ChatScroll.ScrollToEnd();
        }

        // =====================================================================
        // TASK ASSISTANT (Task 1) - form, list, buttons
        // =====================================================================

        private void ReminderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only the last choice ("Pick a date...") needs the date picker.
            if (ReminderDate == null)
                return;

            ReminderDate.IsEnabled = ReminderCombo.SelectedIndex == 5;
        }

        // Works out the reminder date from the form. Returns null for "No reminder".
        private DateTime? ReadReminderFromForm(out string? error)
        {
            error = null;
            DateTime today = DateTime.Today;

            switch (ReminderCombo.SelectedIndex)
            {
                case 1: return today.AddDays(1).AddHours(9);
                case 2: return today.AddDays(3).AddHours(9);
                case 3: return today.AddDays(7).AddHours(9);
                case 4: return today.AddDays(14).AddHours(9);
                case 5:
                    if (ReminderDate.SelectedDate == null)
                    {
                        error = "Please pick a date for the reminder.";
                        return null;
                    }

                    if (ReminderDate.SelectedDate.Value.Date < today)
                    {
                        error = "The reminder date can't be in the past.";
                        return null;
                    }

                    return ReminderDate.SelectedDate.Value.Date.AddHours(9);

                default:
                    return null;
            }
        }

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            FormMessage.Text = "";

            if (!db.IsAvailable)
            {
                FormMessage.Text = "MySQL isn't connected, so tasks can't be saved. See the README for setup.";
                return;
            }

            string title = TitleBox.Text.Trim();
            string description = DescBox.Text.Trim();

            if (title.Length == 0)
            {
                FormMessage.Text = "Please enter a title for the task.";
                return;
            }

            if (description.Length == 0)
            {
                FormMessage.Text = "Please enter a description for the task.";
                return;
            }

            DateTime? reminder = ReadReminderFromForm(out string? error);

            if (error != null)
            {
                FormMessage.Text = error;
                return;
            }

            int id = db.AddTask(title, description, reminder);

            if (id < 0)
            {
                FormMessage.Text = "Could not save the task: " + db.LastError;
                return;
            }

            activityLog.Add(reminder.HasValue
                ? $"Task added: '{title}' (reminder set for {reminder.Value:dd MMM yyyy})"
                : $"Task added: '{title}' (no reminder set)");

            bot.Speak(reminder.HasValue
                ? $"Task added: '{title}'. I'll remind you on {reminder.Value:dd MMM yyyy}."
                : $"Task added: '{title}'. No reminder set.");

            TitleBox.Clear();
            DescBox.Clear();
            ReminderCombo.SelectedIndex = 0;
            ReminderDate.SelectedDate = null;

            RefreshTasks();
        }

        private void CompleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int id)
                return;

            TaskItem? task = tasks.FirstOrDefault(t => t.Id == id);

            if (task == null || !db.MarkCompleted(id))
            {
                FormMessage.Text = "Could not update the task: " + db.LastError;
                return;
            }

            activityLog.Add($"Task completed: '{task.Title}'");
            bot.Speak($"Nice work! I've marked '{task.Title}' as completed.");

            RefreshTasks();
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int id)
                return;

            TaskItem? task = tasks.FirstOrDefault(t => t.Id == id);

            if (task == null)
                return;

            MessageBoxResult answer = MessageBox.Show(
                $"Delete the task '{task.Title}'?",
                "Delete task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            if (!db.DeleteTask(id))
            {
                FormMessage.Text = "Could not delete the task: " + db.LastError;
                return;
            }

            activityLog.Add($"Task deleted: '{task.Title}'");
            bot.Speak($"Done! I've deleted the task '{task.Title}'.");

            RefreshTasks();
        }

        // Reloads the task list from MySQL.
        private void RefreshTasks()
        {
            tasks.Clear();

            if (db.IsAvailable)
            {
                foreach (TaskItem task in db.GetTasks())
                    tasks.Add(task);
            }

            int done = tasks.Count(t => t.IsCompleted);
            TaskCountText.Text = $"{tasks.Count - done} pending, {done} completed";
            NoTasksText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Runs every 30 seconds: announces reminders whose date/time has arrived.
        private void CheckDueReminders()
        {
            if (!db.IsAvailable || user == null)
                return;

            List<TaskItem> due = db.GetDueReminders(DateTime.Now);

            foreach (TaskItem task in due)
            {
                bot.Speak($"⏰ Reminder: '{task.Title}' - {task.Description}");
                db.MarkReminderNotified(task.Id);
                activityLog.Add($"Reminder triggered for '{task.Title}'");
            }

            if (due.Count > 0)
                RefreshTasks();
        }

        // =====================================================================
        // QUIZ (Task 2)
        // =====================================================================

        private void StartQuizButton_Click(object sender, RoutedEventArgs e)
        {
            StartQuiz(true);
        }

        private void StartQuiz(bool announceInChat)
        {
            quiz.Start();
            activityLog.Add($"Quiz started ({quiz.Total} questions)");

            if (announceInChat)
                bot.Speak("Quiz started! Answer each question in the Quiz tab. Good luck!");

            QuizStartPanel.Visibility = Visibility.Collapsed;
            QuizResultPanel.Visibility = Visibility.Collapsed;
            QuizQuestionPanel.Visibility = Visibility.Visible;

            ShowQuestion();
        }

        // Builds the current question on screen, one at a time.
        private void ShowQuestion()
        {
            QuizQuestion question = quiz.Current;

            QuizProgressText.Text = $"Question {quiz.CurrentNumber} of {quiz.Total}";
            QuizScoreText.Text = $"Score: {quiz.Score}";
            QuizTypeText.Text = question.IsTrueFalse ? "TRUE / FALSE" : "MULTIPLE CHOICE";
            QuizQuestionText.Text = question.Text;
            QuizFeedbackText.Text = "";

            OptionsPanel.Children.Clear();
            optionButtons.Clear();

            for (int i = 0; i < question.Options.Count; i++)
            {
                string label = question.IsTrueFalse
                    ? question.Options[i]
                    : $"{(char)('A' + i)}) {question.Options[i]}";

                RadioButton option = new RadioButton
                {
                    GroupName = "QuizAnswers",
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = Brushes.White,
                    Content = new TextBlock
                    {
                        Text = label,
                        FontSize = 14,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                optionButtons.Add(option);
                OptionsPanel.Children.Add(option);
            }

            SubmitAnswerButton.Visibility = Visibility.Visible;
            NextQuestionButton.Visibility = Visibility.Collapsed;
        }

        private void SubmitAnswer_Click(object sender, RoutedEventArgs e)
        {
            int selected = optionButtons.FindIndex(o => o.IsChecked == true);

            if (selected < 0)
            {
                QuizFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x6B));
                QuizFeedbackText.Text = "Please choose an answer first.";
                return;
            }

            QuizResult result = quiz.Answer(selected);

            foreach (RadioButton option in optionButtons)
                option.IsEnabled = false;

            if (result.IsCorrect)
            {
                QuizFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xFC, 0x9A));
                QuizFeedbackText.Text = "✔ Correct! " + result.Explanation;
                bot.Speak("Correct! " + result.Explanation);
            }
            else
            {
                QuizFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                QuizFeedbackText.Text =
                    $"✘ Not quite. The correct answer is: {result.CorrectAnswer}. {result.Explanation}";
                bot.Speak($"Not quite. The correct answer is: {result.CorrectAnswer}. {result.Explanation}");
            }

            QuizScoreText.Text = $"Score: {quiz.Score}";

            SubmitAnswerButton.Visibility = Visibility.Collapsed;
            NextQuestionButton.Visibility = Visibility.Visible;
            NextQuestionButton.Content = quiz.CurrentNumber == quiz.Total
                ? "See Results"
                : "Next Question";
        }

        private void NextQuestion_Click(object sender, RoutedEventArgs e)
        {
            if (quiz.Next())
                ShowQuestion();
            else
                FinishQuiz();
        }

        private void FinishQuiz()
        {
            QuizQuestionPanel.Visibility = Visibility.Collapsed;
            QuizResultPanel.Visibility = Visibility.Visible;

            string feedback = quiz.FinalFeedback();

            QuizFinalScoreText.Text = $"{quiz.Score} / {quiz.Total}";
            QuizFinalMessageText.Text = feedback;

            activityLog.Add(
                $"Quiz completed - {quiz.Total} questions answered, score {quiz.Score}/{quiz.Total}");

            bot.Speak($"Quiz finished! You scored {quiz.Score} out of {quiz.Total}. {feedback}");
        }

        // =====================================================================
        // ACTIVITY LOG (Task 4) - the tab on the right
        // =====================================================================

        private void ShowFullLog_Changed(object sender, RoutedEventArgs e)
        {
            RefreshLog();
        }

        private void RefreshLog()
        {
            // The check box does not exist yet while the window is still being built.
            if (ShowFullLogCheck == null)
                return;

            bool showAll = ShowFullLogCheck.IsChecked == true;
            List<ActivityEntry> entries = showAll ? activityLog.All() : activityLog.Recent(10);

            logLines.Clear();

            int number = 1;
            foreach (ActivityEntry entry in entries)
                logLines.Add($"{number++}. {entry.Display}");

            LogCountText.Text = showAll
                ? $"Showing all {activityLog.Count} actions"
                : $"Showing the last {entries.Count} of {activityLog.Count} actions";

            NoLogText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            reminderTimer?.Stop();
            audioPlayer?.StopSound();

            if (bot != null)
                bot.MessageSpoken -= Bot_MessageSpoken;

            base.OnClosed(e);
        }
    }
}
