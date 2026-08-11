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
        ///
        /// Every Music Switch container in the game was enumerated with
        /// MusicEventShapeResearch.DumpEveryMusicSwitchContainer, and the multi argument ones dumped
        /// in full. Of the six State Groups the music events target, two can be served:
        ///
        ///   WH3_Campaign_Subcultures                     698158058, depth 1              -> listed
        ///   Battle_Music_WH3_Culture                     26264058,  result x culture     -> listed
        ///   WH3_Campaign_Subcultures                     145953291, subculture x resolution
        ///   Battle_Music_WH3_Culture                     67383790,  six arguments deep
        ///   WH3_Campaign_Music_AMS_Fragments_Faction     read elsewhere, see below
        ///   WH3_AMS_Pulse_Percussion_Options             read elsewhere, see below
        ///   WH3_AMS_Pulse_Pitched_Orchestral_Options     read elsewhere, see below
        ///   WH3_AMS_Pulse_Pitched_Ethnic_Options         read elsewhere, see below
        ///
        /// 67383790 is left out although it names the culture, because it names it as the default:
        /// the culture level of that tree is a single key 0 node, and the three levels below it are
        /// musical key, segment and dynamic state. That is the adaptive battle music, which is
        /// authored as a set of stems per key and mix rather than as one piece of music, so a branch
        /// there would need a whole stem set rather than a file.
        ///
        /// 145953291 is left out for a different reason: it is reachable, but it is post battle
        /// campaign music rather than the subculture's theme, and giving it the same audio as the
        /// theme is a decision for whoever wires it up rather than a consequence of this table.
        ///
        /// The four with no container are the adaptive music system. They are not unused and they are
        /// not all the same: MusicEventShapeResearch.DumpWhatReadsTheAmsStateGroups counted what
        /// actually reads each one, and it is two different mechanisms, neither of them a tree.
        ///
        ///   the three Pulse Groups    9 switch tracks each, one per music track that carries pulses.
        ///                             A switch track holds a sub-track per culture with its own
        ///                             source - 16 for percussion, 11 orchestral, 6 ethnic - and
        ///                             picks between them on the State. Adding a culture means adding
        ///                             a sub-track, a source and an association to nine vanilla
        ///                             Music_Tracks, three times over.
        ///
        ///   AMS_Fragments_Faction     one plain Switch container, 418295225 in campaign_music__core,
        ///                             with 17 switches over 16 children. Not in the music hierarchy
        ///                             at all - the ambient fragments are sounds laid over the music.
        ///
        /// Setting a State none of them lists is harmless rather than broken: each falls back to its
        /// own default, which is 'Cathay' for percussion and 'Off' / 'None' for the other three. So a
        /// new culture that skips these still gets Cathay's percussion pulse and no pitched pulses or
        /// ambient fragments, which is a thinner mix rather than a failure.
        /// </summary>
        public static uint? GetMusicSwitchContainerId(string stateGroupName) => stateGroupName switch
        {
            "WH3_Campaign_Subcultures" => 698158058,
            "Battle_Music_WH3_Culture" => 26264058,
            _ => null
        };

        /// <summary>
        /// Whether a mod can give a State Group's States their own audio by merging a branch. False
        /// means an Action Event setting one of its States still works and still fires, but has to
        /// select music that already exists rather than bring its own.
        /// </summary>
        public static bool CanCarryOwnAudio(string stateGroupName) => GetMusicSwitchContainerId(stateGroupName) != null;

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

        /// <summary>The weight and probability on every branch of a vanilla music decision tree.
        /// They are the same on all of them, including the default branch.</summary>
        public const ushort BranchWeight = 50;
        public const ushort BranchProbability = 100;
    }
}
