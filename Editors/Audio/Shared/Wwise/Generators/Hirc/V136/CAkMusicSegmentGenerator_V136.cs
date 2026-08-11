using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators.Hirc.V136
{
    /// <summary>
    /// Builds the Music Segment for a piece of music, matching the vanilla shape recorded in
    /// <see cref="Wh3MusicHierarchyInformation"/>: one child track, and three cue markers whose ids
    /// are the same in every shipped segment.
    /// </summary>
    public class CAkMusicSegmentGenerator_V136 : IHircGeneratorService
    {
        public HircItem GenerateHirc(AudioProjectItem audioProjectItem, SoundBank soundBank = null)
        {
            var musicSegment = audioProjectItem as MusicSegment;

            var segmentHirc = new CAkMusicSegment_V136
            {
                Id = musicSegment.Id,
                HircType = musicSegment.HircType,
                Duration = musicSegment.DurationMs,
                MusicNodeParams = CreateMusicNodeParams(musicSegment)
            };

            // Entry and exit cues bracket the audio; the named cue sits with the entry cue, which
            // is where vanilla puts it on a segment that starts at zero.
            segmentHirc.ArrayMarkersList.Add(CreateMarker(Wh3MusicHierarchyInformation.EntryCueMarkerId, 0, null));
            segmentHirc.ArrayMarkersList.Add(CreateMarker(Wh3MusicHierarchyInformation.CustomCueMarkerId, 0, Wh3MusicHierarchyInformation.CustomCueName));
            segmentHirc.ArrayMarkersList.Add(CreateMarker(Wh3MusicHierarchyInformation.ExitCueMarkerId, musicSegment.DurationMs, null));

            segmentHirc.UpdateSectionSize();
            return segmentHirc;
        }

        private static MusicNodeParams_V136 CreateMusicNodeParams(MusicSegment musicSegment)
        {
            var musicNodeParams = new MusicNodeParams_V136();

            musicNodeParams.NodeBaseParams.DirectParentId = musicSegment.DirectParentId;
            musicNodeParams.NodeBaseParams.OverrideBusId = Wh3MusicHierarchyInformation.NoOverrideBusId;

            musicNodeParams.Children.ChildIds.Add(musicSegment.TrackId);

            musicNodeParams.AkMeterInfo.Tempo = musicSegment.Tempo;
            musicNodeParams.AkMeterInfo.TimeSigNumBeatsBar = 4;
            musicNodeParams.AkMeterInfo.TimeSigBeatValue = 4;

            return musicNodeParams;
        }

        private static CAkMusicSegment_V136.AkMusicMarkerWwise_V136 CreateMarker(uint id, double position, string name)
        {
            return new CAkMusicSegment_V136.AkMusicMarkerWwise_V136
            {
                Id = id,
                Position = position,
                MarkerName = name
            };
        }
    }
}
