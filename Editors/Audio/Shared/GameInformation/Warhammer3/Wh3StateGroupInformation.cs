using System.Collections.Generic;

namespace Editors.Audio.Shared.GameInformation.Warhammer3
{
    public static class Wh3StateGroupInformation
    {
        public static readonly List<string> VoStateGroups =
            ["VO_Actor", "VO_Culture", "VO_Faction_Leader", "VO_Battle_Selection", "VO_Battle_Special_Ability"];

        /// <summary>
        /// The State Groups vanilla music Action Events set. Every culture-specific music event in
        /// battle_music__core.bnk / campaign_music__core.bnk is a SetState against one of these -
        /// music_b_faction_empire sets Battle_Music_WH3_Culture to Empire, and so on. There are no
        /// Play_ music events, which is why music Action Events skip that prefix.
        /// </summary>
        public static readonly List<string> MusicStateGroups =
        [
            "Battle_Music_WH3_Culture",
            "WH3_Campaign_Subcultures",
            "WH3_Campaign_Music_AMS_Fragments_Faction",
            "WH3_AMS_Pulse_Percussion_Options",
            "WH3_AMS_Pulse_Pitched_Orchestral_Options",
            "WH3_AMS_Pulse_Pitched_Ethnic_Options"
        ];

        /// <summary>
        /// State Groups a mod can add its own States to. Adding a State only registers a name
        /// against its Wwise hash - nothing is written into the game's State Group definitions -
        /// so what makes a new State do anything is a SetState Action Event pointing at it and a
        /// music hierarchy that branches on it.
        ///
        /// Declared after the two lists it is built from: static fields initialise in declaration
        /// order, so putting this first leaves both spreads reading null.
        /// </summary>
        public static readonly List<string> ModdableStateGroups =
        [
            .. VoStateGroups,
            .. MusicStateGroups
        ];
    }
}
