namespace Editors.Audio.Shared.GameInformation.Warhammer3
{
    /// <summary>
    /// The shape of a vanilla music branch, read out of the shipped banks with
    /// MusicEventShapeResearch.ProbeVanillaMusicEventShape rather than inferred.
    ///
    /// A music Action Event only sets a State. What turns that State into audio is a branch in one
    /// of the Music Switch containers, and a mod adds music by merging a branch of its own into
    /// that container's decision tree - the same way the Audio Editor already merges Dialogue Event
    /// decision trees into the vanilla ones.
    ///
    /// The branch vanilla builds for one culture, taken from WH3_Campaign_Subcultures / 'Dwarfs':
    ///
    ///   MusicSwitch 698158058           args=[State WH3_Campaign_Subcultures], depth 1
    ///     node key=hash('Dwarfs')       -> audioNodeId = the Music Random Sequence below
    ///       Music_Random_Sequence       parent = the switch container, one playlist root
    ///         Music_Segment             parent = the random sequence, one child, one track
    ///           3 markers               entry at the segment start, a named cue, exit at the end
    ///           Music_Track             parent = the segment, one source, one clip
    ///             source                Vorbis, streamed, pointing at the mod's wem
    ///             clip                  playAt / trim / duration in milliseconds
    ///
    /// Every segment observed reuses the same three marker ids, so they are fixed cue identifiers
    /// rather than per-segment values.
    /// </summary>
    public static class Wh3MusicHierarchyInformation
    {
        /// <summary>
        /// The Music Switch container whose decision tree branches on a given State Group, keyed by
        /// State Group name. These are vanilla hirc ids, so a merge has to re-emit the whole
        /// container the way the Dialogue Event merge re-emits a whole dialogue event.
        /// </summary>
        public static uint? GetMusicSwitchContainerId(string stateGroupName) => stateGroupName switch
        {
            "WH3_Campaign_Subcultures" => 698158058,
            _ => null
        };

        /// <summary>
        /// Cue markers. Every vanilla music segment carries these same three ids: an entry cue at
        /// the segment start, a named cue the campaign scripts sync against, and an exit cue at the
        /// end. Only the positions differ per segment.
        /// </summary>
        public const uint EntryCueMarkerId = 43573010;
        public const uint CustomCueMarkerId = 41514146;
        public const uint ExitCueMarkerId = 1539036744;

        /// <summary>The named cue's name, which is stored null terminated on disk.</summary>
        public const string CustomCueName = "Update_PreBattle\0";

        /// <summary>Vorbis. Every vanilla music source uses it, and it is what the compiler encodes
        /// wavs to, so a generated track has to declare the same plugin.</summary>
        public const uint VorbisPluginId = 262145;

        /// <summary>Normal (non-switch, non-sequence) track. Switch tracks are type 3.</summary>
        public const byte NormalTrackType = 0;

        /// <summary>Milliseconds of look ahead, the same on every vanilla music track.</summary>
        public const int TrackLookAheadTime = 100;

        /// <summary>
        /// Music nodes sit under their own parent in the hierarchy rather than under an actor mixer.
        /// Every vanilla segment and track leaves the bus alone; random sequences mostly do too, but
        /// not always - 'Dwarfs' routes to bus 3128400633 while the other subculture branches are
        /// zero. Zero means inherit, which is what a generated branch wants.
        /// </summary>
        public const uint NoOverrideBusId = 0;

        /// <summary>
        /// The transition rule on a Music Random Sequence. Every subculture branch carries exactly
        /// one, and it is byte for byte the same in all of them: any source to any destination
        /// (-1/-1), no fade, synchronised at the exit cue, playing through the pre-entry and
        /// post-exit regions.
        /// </summary>
        public const uint AnyTransitionId = 0xFFFFFFFF;
        public const uint TransitionFadeCurve = 4;
        public const uint TransitionSyncTypeExitCue = 7;

        /// <summary>
        /// The playlist root's type: continuous sequence when there is a single segment, random step
        /// when there is more than one. Vanilla uses exactly this split, pairing the random case
        /// with avoid-repeat and weighting so variations do not play twice in a row.
        /// </summary>
        public const uint PlaylistTypeSequenceContinuous = 0;
        public const uint PlaylistTypeRandomStep = 3;

        /// <summary>Leaf playlist nodes carry no type of their own.</summary>
        public const uint PlaylistTypeNone = 0xFFFFFFFF;

        /// <summary>The weight on every vanilla playlist node, root and leaf alike.</summary>
        public const uint PlaylistDefaultWeight = 50000;
    }
}
