using System.Collections.Generic;

namespace Editors.Audio.Shared.AudioProject.Models
{
    /// <summary>
    /// Audio contributed to the adaptive music system's pulse layers for one State.
    ///
    /// These State Groups are not read by a decision tree, so a State set against one selects no
    /// branch and there is nothing for a <see cref="MusicRandomSequence"/> to be. They are read by
    /// switch tracks: a vanilla Music Track that holds one sub-track per culture, each with its own
    /// clip, and picks between them on the State. Serving a new culture means adding a sub-track to
    /// every one of those vanilla tracks rather than adding a hirc of our own - which is why this
    /// carries clips and no ids, and why nothing here generates a hirc.
    ///
    /// Vanilla gives each culture a different stem in each of the nine tracks, since each track sits
    /// under a different segment of the loop. A mod supplying fewer than nine has them cycled, so one
    /// file is a valid answer - the same pulse over every segment - and nine is the full treatment.
    /// </summary>
    public class AmsPulse
    {
        /// <summary>One of the three pulse State Groups - see
        /// <see cref="GameInformation.Warhammer3.Wh3MusicHierarchyInformation.IsAmsPulseStateGroup"/>.</summary>
        public string StateGroupName { get; set; }

        /// <summary>The State the Action Event sets, which is what the switch track matches on.</summary>
        public string StateName { get; set; }

        public List<AmsPulseClip> Clips { get; set; } = [];
    }

    /// <summary>
    /// One clip handed to a switch track. This is deliberately not a <see cref="MusicSegment"/>: a
    /// segment owns a Music Track hirc of its own, whereas this is only ever a row in a vanilla
    /// track's source and playlist lists.
    /// </summary>
    public class AmsPulseClip
    {
        public uint SourceId { get; set; }
        public long InMemoryMediaSize { get; set; }
        public string Language { get; set; }

        /// <summary>Length of the audio in milliseconds. The clip is trimmed against the segment it
        /// is dropped into rather than played whole, so this is what the trim is computed from.</summary>
        public double DurationMs { get; set; }
    }
}
