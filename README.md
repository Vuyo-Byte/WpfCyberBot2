# Cybersecurity Awareness Chatbot (PROG6221 – Part 3 / POE)

A WPF (XAML) desktop chatbot that teaches cybersecurity. Part 3 builds on Parts 1 and 2 (greeting, name, tips, dynamic responses) and adds a **MySQL-backed task assistant**, a **quiz mini-game**, **NLP-style keyword detection** and an **activity log**.







> Part 3 is GUI-only (WPF/XAML). Built with C# on .NET 8.

## Features

| Task | What it does |
|------|--------------|
| **1. Task Assistant + Reminders (MySQL)** | Add tasks with a title, description and optional reminder (form or chat). View, mark completed and delete tasks. Everything is stored in MySQL. The bot announces reminders when they fall due. |
| **2. Quiz mini-game** | 12 questions (multiple choice + true/false), one at a time, instant right/wrong feedback with an explanation, running score, final score with feedback. |
| **3. NLP simulation** | Keyword detection, synonyms, typo tolerance (Levenshtein distance) and regular expressions understand many phrasings and dates ("in 3 days", "tomorrow", "next Friday", "25 December"). |
| **4. Activity Log** | Timestamped list of actions (tasks, reminders, quiz, NLP interpretations). Shows the last 10, with "show more" / full history. |

## Setup

### 1. Install MySQL (pick ONE)
* **XAMPP (easiest):** install XAMPP, open the XAMPP Control Panel and **Start** MySQL. Default user `root`, **blank** password.
* **MySQL Installer:** install MySQL Server (+ Workbench). Remember the root password you choose.

### 2. Configure the connection
Open `DbConfig.cs`. Defaults are `localhost`, port `3306`, user `root`, database `cyberbot_db`. If your root user has a password, set a Windows environment variable `CYBERBOT_DB_PASSWORD` (recommended – keeps it out of GitHub) or type it into `DbConfig.cs`.

### 3. Run
1. Open `WpfCyberBot.csproj` in Visual Studio 2022 (.NET 8 SDK).
2. Visual Studio restores the **MySqlConnector** NuGet package automatically.
3. Press **F5**. The app creates the `cyberbot_db` database and the `tasks` table by itself on first run. (`Database/schema.sql` is the same SQL, for reference.)

If MySQL is not running the app still opens; tips, quiz and log work and the Tasks tab shows a warning.

## Things to try in the chat
```
Add task - Review privacy settings          -> "Yes, remind me in 3 days"
Remind me to update my password tomorrow.
Can you remind me to update my password?    -> "next Friday"
Add a task to enable 2FA
Show my tasks / Complete task 2 / Delete task 2
Start quiz   (or "test my knowledge")
Show activity log / What have you done for me?   -> "show more"
How do I make a pasword?   (typos still work)
Help
```

## Database
Table `tasks`: `id`, `title`, `description`, `reminder_date` (nullable), `is_completed`, `reminder_notified`, `created_at`. All queries use parameters (no SQL injection).

## Project structure
```
MainWindow.xaml / .xaml.cs   GUI: chat + Tasks / Quiz / Activity Log tabs
ChatBot.cs                   Conversation logic, task commands, multi-step chats
NlpProcessor.cs              Keyword detection, fuzzy match, date parsing (Task 3)
DatabaseHelper.cs, DbConfig.cs   MySQL integration (Task 1)
TaskItem.cs                  Task model
QuizEngine.cs                Questions, scoring, feedback (Task 2)
ActivityLog.cs               Activity log (Task 4)
ChatParticipants.cs, User.cs, AudioPlayer.cs   From Parts 1 and 2
Database/schema.sql          Table definition
```

## Author
ST10499177 – PROG6221 POE Part 3
