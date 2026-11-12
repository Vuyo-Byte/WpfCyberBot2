using System;
using System.Collections.Generic;
using MySqlConnector;

namespace CyberChatBot
{
    /// <summary>
    /// Talks to MySQL. Every database action in the app goes through this class.
    /// On first run it creates the database and the "tasks" table by itself,
    /// so you do not need to run any SQL manually (Database/schema.sql is
    /// included only as documentation / a manual alternative).
    ///
    /// None of the methods throw: if MySQL is down they return false / -1 /
    /// an empty list and put the reason in LastError, so the chatbot keeps
    /// working for tips, the quiz and the activity log.
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string _serverConnectionString;
        private readonly string _databaseConnectionString;

        public bool IsAvailable { get; private set; }
        public string LastError { get; private set; } = "";

        public DatabaseHelper()
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = DbConfig.Server,
                Port = DbConfig.Port,
                UserID = DbConfig.User,
                Password = DbConfig.Password,
                ConnectionTimeout = 5
            };

            // Connection WITHOUT a database (used to create the database).
            _serverConnectionString = builder.ConnectionString;

            // Connection WITH the database (used for everything else).
            builder.Database = DbConfig.Database;
            _databaseConnectionString = builder.ConnectionString;
        }

        /// <summary>Creates the database and table if needed. Call once at startup.</summary>
        public bool Initialise()
        {
            try
            {
                using (var connection = new MySqlConnection(_serverConnectionString))
                {
                    connection.Open();
                    using var command = new MySqlCommand(
                        "CREATE DATABASE IF NOT EXISTS `" + DbConfig.Database +
                        "` CHARACTER SET utf8mb4;", connection);
                    command.ExecuteNonQuery();
                }

                using (var connection = new MySqlConnection(_databaseConnectionString))
                {
                    connection.Open();

                    const string createTable = @"
                        CREATE TABLE IF NOT EXISTS tasks (
                            id                INT AUTO_INCREMENT PRIMARY KEY,
                            title             VARCHAR(150) NOT NULL,
                            description       VARCHAR(500) NOT NULL,
                            reminder_date     DATETIME NULL,
                            is_completed      TINYINT(1) NOT NULL DEFAULT 0,
                            reminder_notified TINYINT(1) NOT NULL DEFAULT 0,
                            created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                        );";

                    using var command = new MySqlCommand(createTable, connection);
                    command.ExecuteNonQuery();
                }

                IsAvailable = true;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LastError = ex.Message;
                return false;
            }
        }

        private MySqlConnection Open()
        {
            var connection = new MySqlConnection(_databaseConnectionString);
            connection.Open();
            return connection;
        }

        // ---------------------------------------------------------------
        // CREATE
        // ---------------------------------------------------------------

        /// <summary>Adds a task and returns its new id (or -1 if it failed).</summary>
        public int AddTask(string title, string description, DateTime? reminder)
        {
            try
            {
                using var connection = Open();
                using var command = new MySqlCommand(
                    "INSERT INTO tasks (title, description, reminder_date) " +
                    "VALUES (@title, @description, @reminder);", connection);

                command.Parameters.AddWithValue("@title", title);
                command.Parameters.AddWithValue("@description", description);
                command.Parameters.AddWithValue("@reminder", (object?)reminder ?? DBNull.Value);

                command.ExecuteNonQuery();
                return (int)command.LastInsertedId;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return -1;
            }
        }

        // ---------------------------------------------------------------
        // READ
        // ---------------------------------------------------------------

        /// <summary>All tasks: pending first, newest first.</summary>
        public List<TaskItem> GetTasks()
        {
            var tasks = new List<TaskItem>();

            try
            {
                using var connection = Open();
                using var command = new MySqlCommand(
                    "SELECT id, title, description, reminder_date, is_completed, " +
                    "reminder_notified, created_at FROM tasks " +
                    "ORDER BY is_completed ASC, id DESC;", connection);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    tasks.Add(ReadTask(reader));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            return tasks;
        }

        /// <summary>Tasks whose reminder time has passed and have not been announced yet.</summary>
        public List<TaskItem> GetDueReminders(DateTime now)
        {
            var tasks = new List<TaskItem>();

            try
            {
                using var connection = Open();
                using var command = new MySqlCommand(
                    "SELECT id, title, description, reminder_date, is_completed, " +
                    "reminder_notified, created_at FROM tasks " +
                    "WHERE reminder_date IS NOT NULL AND reminder_date <= @now " +
                    "AND is_completed = 0 AND reminder_notified = 0;", connection);

                command.Parameters.AddWithValue("@now", now);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    tasks.Add(ReadTask(reader));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            return tasks;
        }

        private static TaskItem ReadTask(MySqlDataReader reader)
        {
            int reminderColumn = reader.GetOrdinal("reminder_date");

            return new TaskItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Description = reader.GetString(reader.GetOrdinal("description")),
                ReminderDate = reader.IsDBNull(reminderColumn)
                    ? null
                    : reader.GetDateTime(reminderColumn),
                IsCompleted = Convert.ToBoolean(
                    reader.GetValue(reader.GetOrdinal("is_completed"))),
                ReminderNotified = Convert.ToBoolean(
                    reader.GetValue(reader.GetOrdinal("reminder_notified"))),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
            };
        }

        // ---------------------------------------------------------------
        // UPDATE / DELETE
        // ---------------------------------------------------------------

        /// <summary>Sets (or changes) the reminder on a task.</summary>
        public bool SetReminder(int id, DateTime reminder)
        {
            return Execute(
                "UPDATE tasks SET reminder_date = @reminder, reminder_notified = 0 WHERE id = @id;",
                command =>
                {
                    command.Parameters.AddWithValue("@reminder", reminder);
                    command.Parameters.AddWithValue("@id", id);
                });
        }

        public bool MarkCompleted(int id)
        {
            return Execute(
                "UPDATE tasks SET is_completed = 1 WHERE id = @id;",
                command => command.Parameters.AddWithValue("@id", id));
        }

        public bool MarkReminderNotified(int id)
        {
            return Execute(
                "UPDATE tasks SET reminder_notified = 1 WHERE id = @id;",
                command => command.Parameters.AddWithValue("@id", id));
        }

        public bool DeleteTask(int id)
        {
            return Execute(
                "DELETE FROM tasks WHERE id = @id;",
                command => command.Parameters.AddWithValue("@id", id));
        }

        // Runs one INSERT/UPDATE/DELETE and says whether a row was affected.
        private bool Execute(string sql, Action<MySqlCommand> addParameters)
        {
            try
            {
                using var connection = Open();
                using var command = new MySqlCommand(sql, connection);
                addParameters(command);
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }
}
