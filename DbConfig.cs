using System;

namespace CyberChatBot
{
    /// <summary>
    /// MySQL connection settings.
    /// Change these to match YOUR MySQL installation (see README.md).
    ///
    /// XAMPP users:       User = "root", Password = "" (blank)
    /// MySQL Installer:   User = "root", Password = the one you chose during setup
    ///
    /// TIP: Do not commit a real password to GitHub. Instead, set a Windows
    /// environment variable called CYBERBOT_DB_PASSWORD and leave this blank.
    /// </summary>
    public static class DbConfig
    {
        public const string Server = "localhost";
        public const uint Port = 3306;
        public const string User = "root";
        public const string Database = "cyberbot_db";

        // Reads the environment variable first, otherwise uses the text after "??".
        public static string Password =>
            Environment.GetEnvironmentVariable("CYBERBOT_DB_PASSWORD") ?? "";
    }
}
