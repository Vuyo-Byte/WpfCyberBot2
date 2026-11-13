using System;
using System.Collections.Generic;
using System.Linq;

namespace CyberChatBot
{
    /// <summary>One quiz question: multiple choice (4 options) or true/false (2 options).</summary>
    public class QuizQuestion
    {
        public string Text { get; }
        public List<string> Options { get; }
        public int CorrectIndex { get; }
        public string Explanation { get; }

        public bool IsTrueFalse => Options.Count == 2;

        public QuizQuestion(string text, string[] options, int correctIndex, string explanation)
        {
            Text = text;
            Options = options.ToList();
            CorrectIndex = correctIndex;
            Explanation = explanation; 
        }

        /// <summary>Same question with the multiple-choice answers in a random order.</summary>
        public QuizQuestion WithShuffledOptions(Random random)
        {
            if (IsTrueFalse)
                return this;

            string correct = Options[CorrectIndex];
            string[] shuffled = Options.OrderBy(_ => random.Next()).ToArray();

            return new QuizQuestion(Text, shuffled, Array.IndexOf(shuffled, correct), Explanation);
        }
    }

    public class QuizResult
    {
        public bool IsCorrect { get; set; }
        public string CorrectAnswer { get; set; } = "";
        public string Explanation { get; set; } = "";
    }

    /// <summary>
    /// Task 2: the cybersecurity mini-game. Holds the question list, asks one
    /// question at a time, checks answers and keeps the score.
    /// </summary>
    public class QuizEngine
    {
        private readonly Random _random = new Random();
        private List<QuizQuestion> _questions = new List<QuizQuestion>();
        private int _index;

        public int Score { get; private set; }
        public bool IsRunning { get; private set; }

        public int Total => _questions.Count;
        public int CurrentNumber => _index + 1;
        public QuizQuestion Current => _questions[_index];

        /// <summary>Starts (or restarts) the quiz with the questions in a fresh random order.</summary>
        public void Start()
        {
            _questions = BuildQuestionBank()
                .OrderBy(_ => _random.Next())
                .Select(q => q.WithShuffledOptions(_random))
                .ToList();

            _index = 0;
            Score = 0;
            IsRunning = true;
        }

        public QuizResult Answer(int selectedIndex)
        {
            QuizQuestion question = Current;
            bool correct = selectedIndex == question.CorrectIndex;

            if (correct)
                Score++;

            return new QuizResult
            {
                IsCorrect = correct,
                CorrectAnswer = question.Options[question.CorrectIndex],
                Explanation = question.Explanation
            };
        }

        /// <summary>Moves on. Returns false when there are no more questions.</summary>
        public bool Next()
        {
            _index++;

            if (_index >= _questions.Count)
            {
                IsRunning = false;
                return false;
            }

            return true;
        }

        public string FinalFeedback()
        {
            int percent = Total == 0 ? 0 : Score * 100 / Total;

            if (percent >= 80)
                return "Great job! You're a cybersecurity pro!";

            if (percent >= 50)
                return "Good effort! You know the basics. Keep practising to sharpen your skills.";

            return "Keep learning to stay safe online!";
        }

        private static List<QuizQuestion> BuildQuestionBank()
        {
            return new List<QuizQuestion>
            {
                // ---------- Multiple choice ----------
                new QuizQuestion(
                    "What should you do if you receive an email asking for your password?",
                    new[] { "Reply with your password", "Delete the email", "Report the email as phishing", "Ignore it" },
                    2,
                    "Reporting phishing emails helps prevent scams. Real companies never ask for your password by email."),

                new QuizQuestion(
                    "Which of these is the strongest password?",
                    new[] { "password123", "MyName1990", "Qwerty!", "Tr33-House#Moon82" },
                    3,
                    "Long passwords that mix letters, numbers and symbols, and contain no personal info, are the hardest to guess."),

                new QuizQuestion(
                    "What does two-factor authentication (2FA) add to your account?",
                    new[] { "A faster login", "A second proof of identity on top of your password", "A password that changes every day", "Free antivirus software" },
                    1,
                    "2FA means a stolen password alone is not enough to get into your account."),

                new QuizQuestion(
                    "Social engineering is best described as...",
                    new[] { "Software that guesses passwords", "A type of firewall", "Tricking people into giving up information or access", "Updating your social media settings" },
                    2,
                    "Social engineering attacks target people, not computers. Always verify who is really asking."),

                new QuizQuestion(
                    "You get an unexpected message with a link. What is the safest thing to do?",
                    new[] { "Click it quickly to see where it goes", "Hover over it to check the real address, or visit the site directly", "Forward it to your friends", "Reply and ask if it is safe" },
                    1,
                    "Links can hide their real destination. Checking the address or typing the site yourself avoids fake pages."),

                new QuizQuestion(
                    "What does ransomware do?",
                    new[] { "Speeds up your computer", "Blocks pop-up adverts", "Deletes your browser history", "Locks or encrypts your files and demands payment" },
                    3,
                    "Ransomware holds your files hostage. Regular backups mean you never have to pay."),

                new QuizQuestion(
                    "What is the best way to protect your files from ransomware or a broken hard drive?",
                    new[] { "Keep one copy on your desktop", "Make regular backups to an external drive or the cloud", "Email the files to yourself once a year", "Rename the files" },
                    1,
                    "Backups let you restore your data after an attack, theft or hardware failure."),

                // ---------- True / False -----------
                new QuizQuestion(
                    "True or false: Using the same password on all your accounts is safe as long as it is long.",
                    new[] { "True", "False" },
                    1,
                    "If one site is breached, attackers try that password everywhere. Use a unique password for each account."),

                new QuizQuestion(
                    "True or false: Public Wi-Fi is always safe for online banking.",
                    new[] { "True", "False" },
                    1,
                    "Public networks can be monitored or faked. Use mobile data or a VPN for sensitive transactions."),

                new QuizQuestion(
                    "True or false: A padlock icon in the browser means a website is definitely trustworthy.",
                    new[] { "True", "False" },
                    1,
                    "The padlock only means the connection is encrypted. Scam sites can have one too, so check the address."),

                new QuizQuestion(
                    "True or false: Software updates often contain important security fixes.",
                    new[] { "True", "False" },
                    0,
                    "Updates patch known weaknesses that attackers actively exploit. Install them promptly."),

                new QuizQuestion(
                    "True or false: Phishing attacks only happen through email.",
                    new[] { "True", "False" },
                    1,
                    "Phishing also happens by SMS, phone calls and social media messages.")
            };
        }
    }
}
