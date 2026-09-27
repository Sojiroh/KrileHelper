namespace Translation.Core.Reference
{
    /// <summary>
    /// A translation somebody has already made by hand, looked up rather than
    /// produced.
    ///
    /// The game's own dialogue has been translated by the xivrus project, and
    /// for a line that is in there no machine is going to do better - nor as
    /// quickly, nor without asking a service for it.
    /// </summary>
    public interface IReferenceTranslationSource
    {
        /// <summary>The language these translations are in, or empty when none loaded.</summary>
        string LanguageCode { get; }

        /// <summary>
        /// The language the lines are keyed on, which is the one the game is
        /// played in. A line is read off the screen in that language, so an
        /// index keyed on another matches nothing at all.
        /// </summary>
        string SourceLanguageCode { get; }

        /// <summary>
        /// The commit of the translation project this was built from, or empty
        /// when it was built from a folder and there is nothing to compare
        /// against. Asked before an update, so an index that is already current
        /// costs one request rather than a download.
        /// </summary>
        string Revision { get; }

        /// <summary>How many lines are indexed, for telling the user what they have.</summary>
        int LineCount { get; }

        /// <summary>
        /// The parsing rules the index was built by, or zero for one built
        /// before they were written down. Older rules mean the index is out of
        /// date however current the translation in it is.
        /// </summary>
        int RulesVersion { get; }

        /// <summary>
        /// The character's name, which the game writes into lines addressed to
        /// them. Until it is known those lines cannot be recognised, since what
        /// is stored has the name punched out and what is read has it filled in.
        /// </summary>
        string PlayerName { get; set; }

        /// <summary>
        /// The character's gender, which the Russian agrees with. Null until
        /// known, and those lines are left to a translator until it is.
        /// </summary>
        bool? PlayerIsFeminine { get; set; }

        bool TryGetTranslation(string sentence, out string translation);

        /// <summary>A character's name as the translators render it.</summary>
        bool TryGetSpeakerName(string speaker, out string translated);
    }
}
