using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise.Generators.Hirc.V136;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;

namespace Test.Audio
{
    // The shape asserted here is the vanilla one, read out of the shipped banks with
    // MusicEventShapeResearch and recorded in Wh3MusicHierarchyInformation. The round-trip checks
    // matter as much as the field checks: a generated hirc has no parsed SectionSize to fall back
    // on, so if the size arithmetic is wrong the bank is corrupt from the next hirc onwards.
    internal class MusicHierarchyGenerationTests
    {
        const uint SegmentId = 1000;
        const uint TrackId = 1001;
        const uint RanSeqId = 1002;
        const uint SourceId = 555444333;
        const double DurationMs = 28800.02267573696;

        [Test]
        public void GeneratedSegmentCarriesTheThreeVanillaCueMarkers()
        {
            var segment = (CAkMusicSegment_V136)new CAkMusicSegmentGenerator_V136().GenerateHirc(CreateMusicSegment());

            Assert.Multiple(() =>
            {
                Assert.That(segment.ArrayMarkersList, Has.Count.EqualTo(3));

                Assert.That(segment.ArrayMarkersList[0].Id, Is.EqualTo(Wh3MusicHierarchyInformation.EntryCueMarkerId));
                Assert.That(segment.ArrayMarkersList[0].Position, Is.EqualTo(0));

                Assert.That(segment.ArrayMarkersList[1].Id, Is.EqualTo(Wh3MusicHierarchyInformation.CustomCueMarkerId));
                Assert.That(segment.ArrayMarkersList[1].MarkerName, Is.EqualTo(Wh3MusicHierarchyInformation.CustomCueName));

                // The exit cue has to sit at the end of the audio, not at some fixed value, or the
                // segment ends before or after the file does.
                Assert.That(segment.ArrayMarkersList[2].Id, Is.EqualTo(Wh3MusicHierarchyInformation.ExitCueMarkerId));
                Assert.That(segment.ArrayMarkersList[2].Position, Is.EqualTo(DurationMs));
            });
        }

        [Test]
        public void GeneratedSegmentIsParentedInTheMusicHierarchyAndDoesNotOverrideTheBus()
        {
            var segment = (CAkMusicSegment_V136)new CAkMusicSegmentGenerator_V136().GenerateHirc(CreateMusicSegment());
            var nodeParams = segment.MusicNodeParams;

            Assert.Multiple(() =>
            {
                Assert.That(nodeParams.NodeBaseParams.DirectParentId, Is.EqualTo(RanSeqId));
                Assert.That(nodeParams.NodeBaseParams.OverrideBusId, Is.EqualTo(0), "vanilla music nodes never override the bus");
                Assert.That(nodeParams.Children.ChildIds, Is.EqualTo(new[] { TrackId }));
                Assert.That(segment.Duration, Is.EqualTo(DurationMs));
            });
        }

        [Test]
        public void GeneratedTrackNamesTheAudioAsAStreamedVorbisSource()
        {
            var track = (CAkMusicTrack_V136)new CAkMusicTrackGenerator_V136().GenerateHirc(CreateMusicSegment());

            Assert.Multiple(() =>
            {
                Assert.That(track.Id, Is.EqualTo(TrackId));
                Assert.That(track.TrackType, Is.EqualTo(Wh3MusicHierarchyInformation.NormalTrackType));
                Assert.That(track.LookAheadTime, Is.EqualTo(Wh3MusicHierarchyInformation.TrackLookAheadTime));
                Assert.That(track.NodeBaseParams.DirectParentId, Is.EqualTo(SegmentId));

                Assert.That(track.SourceList, Has.Count.EqualTo(1));
                Assert.That(track.SourceList[0].PluginId, Is.EqualTo(Wh3MusicHierarchyInformation.VorbisPluginId));
                Assert.That(track.SourceList[0].StreamType, Is.EqualTo(AKBKSourceType.Streaming));
                Assert.That(track.SourceList[0].AkMediaInformation.SourceId, Is.EqualTo(SourceId));

                Assert.That(track.PlaylistList, Has.Count.EqualTo(1));
                Assert.That(track.PlaylistList[0].SourceId, Is.EqualTo(SourceId));
                Assert.That(track.PlaylistList[0].SrcDuration, Is.EqualTo(DurationMs));
                Assert.That(track.NumSubTrack, Is.EqualTo(1), "the sub-track count is only written when there is a playlist");
            });
        }

        [Test]
        public void TheGeneratedSegmentSurvivesAWriteReadRoundTrip()
        {
            var segment = (CAkMusicSegment_V136)new CAkMusicSegmentGenerator_V136().GenerateHirc(CreateMusicSegment());

            var reloaded = new CAkMusicSegment_V136();
            reloaded.ReadHirc(new ByteChunk(segment.WriteData()));

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(SegmentId));
                Assert.That(reloaded.Duration, Is.EqualTo(DurationMs));
                Assert.That(reloaded.ArrayMarkersList, Has.Count.EqualTo(3));
                Assert.That(reloaded.ArrayMarkersList[1].MarkerName, Is.EqualTo(Wh3MusicHierarchyInformation.CustomCueName));
                Assert.That(reloaded.MusicNodeParams.Children.ChildIds, Is.EqualTo(new[] { TrackId }));
            });
        }

        [Test]
        public void TheGeneratedTrackSurvivesAWriteReadRoundTrip()
        {
            var track = (CAkMusicTrack_V136)new CAkMusicTrackGenerator_V136().GenerateHirc(CreateMusicSegment());

            var reloaded = new CAkMusicTrack_V136();
            reloaded.ReadHirc(new ByteChunk(track.WriteData()));

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(TrackId));
                Assert.That(reloaded.SourceList, Has.Count.EqualTo(1));
                Assert.That(reloaded.SourceList[0].AkMediaInformation.SourceId, Is.EqualTo(SourceId));
                Assert.That(reloaded.PlaylistList, Has.Count.EqualTo(1));
                Assert.That(reloaded.PlaylistList[0].SrcDuration, Is.EqualTo(DurationMs));
                Assert.That(reloaded.LookAheadTime, Is.EqualTo(Wh3MusicHierarchyInformation.TrackLookAheadTime));
            });
        }

        static MusicSegment CreateMusicSegment()
        {
            return new MusicSegment
            {
                Id = SegmentId,
                TrackId = TrackId,
                DirectParentId = RanSeqId,
                SourceId = SourceId,
                InMemoryMediaSize = 4238,
                DurationMs = DurationMs,
                Language = Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx)
            };
        }
    }
}
