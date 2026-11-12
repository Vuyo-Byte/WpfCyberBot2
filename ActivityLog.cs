using System;
using System.Collections.Generic;
using System.Linq;

namespace CyberChatBot
{
    /// <summary>One thing the chatbot did, with the time it happened.</summary>
    public class ActivityEntry
    {
        public DateTime Time { get; set; }
        public string Description { get; set; } = "";

        public string Display => $"[{Time:dd MMM HH:mm:ss}] {Description}";
    }

    /// <summary>
    /// Task 4: the activity log. Stores every significant action in a list
    /// (tasks, reminders, quiz, NLP interpretations) with a timestamp.
    /// </summary>
    public class ActivityLog
    {
        private readonly List<ActivityEntry> _entries = new List<ActivityEntry>();

        // The GUI listens to this so the Activity Log tab updates by itself.
        public event Action? Changed;

        public int Count => _entries.Count;

        public void Add(string description)
        {
            _entries.Add(new ActivityEntry
            {
                Time = DateTime.Now,
                Description = description
            });

            Changed?.Invoke();
        }

        /// <summary>The last 'count' actions, newest first.</summary>
        public List<ActivityEntry> Recent(int count)
        {
            return Enumerable.Reverse(_entries).Take(count).ToList();
        }

        /// <summary>Every action, newest first.</summary>
        public List<ActivityEntry> All()
        {
            return Enumerable.Reverse(_entries).ToList();
        }
    }
}
