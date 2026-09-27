using System;
using System.Globalization;
using System.IO;

using Microsoft.Data.Sqlite;


namespace Translation.Core.Reference
{
    /// <summary>
    /// Settles which index of hand-made translations is read.
    ///
    /// There are two: the one the application ships with, and the one the user
    /// fetched. The shipped one is never written to - it belongs to the
    /// installation, and an update replaces the folder it sits in - so updates
    /// go beside the user's settings, where nothing but the user disturbs them.
    /// </summary>
    public static class ReferenceIndexLocation
    {
        private const string ProductDirectory = "krile-helper";
        private const string ReferenceDirectory = "reference";

        /// <summary>
        /// The persistent location for indexes downloaded by the user.
        /// Linux follows the XDG data directory specification and keeps the
        /// large generated database out of the installation directory.
        /// </summary>
        public static string DefaultDataDirectory =>
            Path.Combine(XdgRoot("XDG_DATA_HOME", ".local/share"), ProductDirectory, ReferenceDirectory);

        /// <summary>
        /// The cache location reserved for transient reference work. The
        /// updater currently streams the archive and only needs a temporary
        /// SQLite file, but exposing this root keeps all platform paths in one
        /// place for callers that need a staging file.
        /// </summary>
        public static string DefaultCacheDirectory =>
            Path.Combine(XdgRoot("XDG_CACHE_HOME", ".cache"), ProductDirectory, ReferenceDirectory);

        public static string DefaultDatabasePath(string sourceLanguage, string targetLanguage = "ru")
        {
            var source = NormalizeLanguage(sourceLanguage);
            var target = NormalizeLanguage(targetLanguage);
            return Path.Combine(DefaultDataDirectory, source + "-" + target + ".sqlite");
        }

        public static string DefaultShippedPath(string sourceLanguage, string targetLanguage = "ru")
        {
            var source = NormalizeLanguage(sourceLanguage);
            var target = NormalizeLanguage(targetLanguage);
            return Path.Combine(AppContext.BaseDirectory, ReferenceDirectory, source + "-" + target + ".sqlite");
        }

        private static string NormalizeLanguage(string language) =>
            string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

        private static string XdgRoot(string variable, string fallbackSuffix)
        {
            var configured = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
            {
                return Path.GetFullPath(configured);
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home, fallbackSuffix);
        }

        /// <summary>
        /// The file to read: whichever of the two was built later.
        ///
        /// Not simply "the user's if there is one". Somebody who fetched an
        /// index in March and installed a release built in June would go on
        /// reading March, and would have no way of telling: both say the same
        /// thing on the General page except for a revision nobody memorises.
        /// An index from before this was recorded counts as the older one,
        /// which it almost certainly is.
        /// </summary>
        public static string Choose(string userPath, string shippedPath, Action<string>? logger = null)
        {
            var user = SqliteReferenceTranslationSource.Resolve(userPath);
            var shipped = SqliteReferenceTranslationSource.Resolve(shippedPath);

            var hasUser = user.Length > 0 && File.Exists(user);
            var hasShipped = shipped.Length > 0 && File.Exists(shipped) &&
                             !string.Equals(user, shipped, StringComparison.OrdinalIgnoreCase);

            if (!hasUser)
            {
                return hasShipped ? shipped : user;
            }

            if (!hasShipped)
            {
                return user;
            }

            var userBuilt = BuiltAt(user);
            var shippedBuilt = BuiltAt(shipped);

            if (shippedBuilt <= userBuilt)
            {
                return user;
            }

            logger?.Invoke($"The installed translations ({shippedBuilt:u}) are newer than the fetched ones ({userBuilt:u}); using those.");
            return shipped;
        }

        /// <summary>
        /// When an index was built, or the beginning of time when it does not
        /// say - which is what every index built before this was recorded says.
        /// </summary>
        private static DateTime BuiltAt(string path)
        {
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());

                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM meta WHERE key = 'built'";

                return DateTime.TryParse(command.ExecuteScalar() as string, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var built)
                    ? built
                    : DateTime.MinValue;
            }
            catch (Exception)
            {
                // Unreadable counts as oldest: the other one is at least known
                // to open, and choosing a file that cannot be read helps nobody.
                return DateTime.MinValue;
            }
        }
    }
}
