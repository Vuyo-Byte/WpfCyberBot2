using System;

namespace CyberChatBot
{
    /// <summary>
    /// One cybersecurity task stored in the MySQL "tasks" table.
    /// The read-only properties at the bottom are used by the XAML task cards.
    /// </summary>
    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime? ReminderDate { get; set; }
        public bool IsCompleted { get; set; }
        public bool ReminderNotified { get; set; }
        public DateTime CreatedAt { get; set; }

        public string IdLabel => "#" + Id;

        public string ReminderText => ReminderDate.HasValue
            ? "⏰ Reminder: " + ReminderDate.Value.ToString("dd MMM yyyy, HH:mm")
            : "No reminder set";

        public string StatusText => IsCompleted ? "✔ Completed" : "● Pending";

        public bool CanComplete => !IsCompleted;
    }
}
