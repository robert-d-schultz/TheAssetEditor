using System.Collections.Generic;

namespace Editors.Audio.Shared.AudioProject.Models
{
    /// <summary>
    /// Audio contributed to the adaptive music system's ambient fragments for one State.
    ///
    /// The odd one out among the four AMS State Groups. The other three are read by switch tracks
    /// inside the music; this one is read by a plain Switch container - the fragments are Sounds laid
    /// over the music rather than part of it - and that container's children are themselves Switch
    /// containers keyed on a second Group, the musical key the score is currently in.
    ///
    /// So a culture here is a small hierarchy rather than a clip: a Switch container on the key Group
    /// that the vanilla faction container points at, and the audio below it. The key switch is built
    /// by mirroring the keys an existing culture uses rather than assuming what they are, and every
    /// key is pointed at the same audio - authoring a fragment per musical key is something only the
    /// composer of the piece can meaningfully do.
    /// </summary>
    public class AmsFragment
    {
        public string StateGroupName { get; set; }
        public string StateName { get; set; }

        /// <summary>The vanilla Switch container this culture is added to, which is the one that
        /// branches on <see cref="StateGroupName"/>.</summary>
        public uint FactionSwitchContainerId { get; set; }

        /// <summary>The generated Switch container on the musical key Group. This is what the vanilla
        /// container's new switch points at, and it is a new id so it ships in the mod's own .bnk.</summary>
        public uint KeySwitchContainerId { get; set; }

        /// <summary>What every key on that switch resolves to - a Random Sequence container when the
        /// modder picked several files, or the single Sound when they picked one.</summary>
        public uint TargetHircId { get; set; }

        /// <summary>The Sounds underneath, by id. Held here so removing the Event can find them
        /// again; the Sounds themselves live in the SoundBank's own list.</summary>
        public List<uint> SoundIds { get; set; } = [];
    }
}
