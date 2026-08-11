using System;
using System.Collections.Generic;
using System.Linq;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators
{
    /// <summary>One State's clip for one vanilla switch track.</summary>
    public record AmsPulseBranch(string StateName, AmsPulseClip Clip);

    public interface IAmsPulseTrackMergeService
    {
        CAkMusicTrack_V136 AddSubTracks(CAkMusicTrack_V136 vanillaTrack, IReadOnlyList<AmsPulseBranch> branches, double segmentDurationMs);
        CAkMusicTrack_V136 MergeTracks(CAkMusicTrack_V136 baseTrack, CAkMusicTrack_V136 vanillaTrack, CAkMusicTrack_V136 moddedTrack);
    }

    /// <summary>
    /// Adds a culture's pulse layer to a vanilla switch track.
    ///
    /// A switch track is one Music Track holding a sub-track per culture: the source list and the
    /// playlist both grow by one row, and the association table - which is indexed by sub-track -
    /// gets the State that selects it. There is no hirc of our own anywhere in this; the track keeps
    /// its vanilla id and is re-emitted whole, exactly as the Music Switch container merge does,
    /// which is why the result has to go in the testing and merging .bnks rather than the mod's own.
    ///
    /// Sub-tracks are appended rather than inserted. The association array is positional, so
    /// inserting in the middle would silently re-point every culture below the insertion at the
    /// wrong clip.
    /// </summary>
    public class AmsPulseTrackMergeService : IAmsPulseTrackMergeService
    {
        public CAkMusicTrack_V136 AddSubTracks(
            CAkMusicTrack_V136 vanillaTrack, IReadOnlyList<AmsPulseBranch> branches, double segmentDurationMs)
        {
            ArgumentNullException.ThrowIfNull(vanillaTrack);
            ArgumentNullException.ThrowIfNull(branches);

            if (vanillaTrack.TrackType != CAkMusicTrack_V136.SwitchTrackType || vanillaTrack.SwitchParams == null)
                throw new NotSupportedException(
                    $"Music Track {vanillaTrack.Id} is not a switch track, so it has no sub-tracks to add a culture to.");

            var mergedTrack = CopyTrack(vanillaTrack);

            foreach (var branch in branches)
            {
                var stateId = WwiseHash.Compute(branch.StateName);

                // Re-adding a State would give the track two sub-tracks answering to it, and only
                // the first would ever be reached.
                var existingSubTrack = mergedTrack.SwitchParams!.SwitchAssoc.IndexOf(stateId);
                if (existingSubTrack != -1)
                {
                    ReplaceClip(mergedTrack, existingSubTrack, branch.Clip, segmentDurationMs);
                    continue;
                }

                var subTrackIndex = (uint)mergedTrack.SwitchParams.SwitchAssoc.Count;
                mergedTrack.SwitchParams.SwitchAssoc.Add(stateId);
                mergedTrack.SourceList.Add(CreateSource(branch.Clip));
                mergedTrack.PlaylistList.Add(CreateClip(subTrackIndex, branch.Clip, segmentDurationMs));
                mergedTrack.NumSubTrack = (uint)mergedTrack.SwitchParams.SwitchAssoc.Count;
            }

            mergedTrack.UpdateSectionSize();
            return mergedTrack;
        }

        /// <summary>
        /// Folds one mod's track into another's, for the merger combining several mods.
        ///
        /// A decision tree can be merged by key, but a switch track cannot: its association array is
        /// indexed by sub-track, so two mods that each appended a culture both claim the same index
        /// and the lists cannot simply be concatenated. What is stable is the vanilla track, so this
        /// works out what a mod added by diffing its associations against vanilla's and re-appends
        /// those onto the base - which gives every mod an index of its own however many came before.
        ///
        /// The base takes priority where two mods claim the same State, the same tie break the
        /// Dialogue Event and Music Switch container merges use.
        /// </summary>
        public CAkMusicTrack_V136 MergeTracks(
            CAkMusicTrack_V136 baseTrack, CAkMusicTrack_V136 vanillaTrack, CAkMusicTrack_V136 moddedTrack)
        {
            ArgumentNullException.ThrowIfNull(baseTrack);
            ArgumentNullException.ThrowIfNull(vanillaTrack);
            ArgumentNullException.ThrowIfNull(moddedTrack);

            var mergedTrack = CopyTrack(baseTrack);
            var vanillaStateIds = vanillaTrack.SwitchParams!.SwitchAssoc.ToHashSet();

            for (var subTrack = 0; subTrack < moddedTrack.SwitchParams!.SwitchAssoc.Count; subTrack++)
            {
                var stateId = moddedTrack.SwitchParams.SwitchAssoc[subTrack];
                if (vanillaStateIds.Contains(stateId) || mergedTrack.SwitchParams!.SwitchAssoc.Contains(stateId))
                    continue;

                var moddedClip = moddedTrack.PlaylistList.FirstOrDefault(item => item.TrackId == subTrack);
                if (moddedClip == null)
                    continue;

                var moddedSource = moddedTrack.SourceList
                    .FirstOrDefault(source => source.AkMediaInformation.SourceId == moddedClip.SourceId);

                if (moddedSource == null)
                    continue;

                var newSubTrackIndex = (uint)mergedTrack.SwitchParams!.SwitchAssoc.Count;
                mergedTrack.SwitchParams.SwitchAssoc.Add(stateId);
                mergedTrack.SourceList.Add(moddedSource);

                // The clip is re-indexed rather than copied as-is: it carried the sub-track number it
                // had in the mod's own track, which is not the one it has here.
                mergedTrack.PlaylistList.Add(new CAkMusicTrack_V136.AkTrackSrcInfo_V136
                {
                    TrackId = newSubTrackIndex,
                    SourceId = moddedClip.SourceId,
                    EventId = moddedClip.EventId,
                    PlayAt = moddedClip.PlayAt,
                    BeginTrimOffset = moddedClip.BeginTrimOffset,
                    EndTrimOffset = moddedClip.EndTrimOffset,
                    SrcDuration = moddedClip.SrcDuration
                });

                mergedTrack.NumSubTrack = (uint)mergedTrack.SwitchParams.SwitchAssoc.Count;
            }

            mergedTrack.UpdateSectionSize();
            return mergedTrack;
        }

        private static void ReplaceClip(CAkMusicTrack_V136 track, int subTrackIndex, AmsPulseClip clip, double segmentDurationMs)
        {
            var existingClip = track.PlaylistList.FirstOrDefault(item => item.TrackId == subTrackIndex);
            if (existingClip == null)
            {
                // The 'Off' sub-track has an association but no clip, so a State landing on it is a
                // sub-track that exists and plays nothing until one is added.
                track.PlaylistList.Add(CreateClip((uint)subTrackIndex, clip, segmentDurationMs));
                return;
            }

            track.SourceList.RemoveAll(source => source.AkMediaInformation.SourceId == existingClip.SourceId);
            track.PlaylistList.Remove(existingClip);

            track.SourceList.Add(CreateSource(clip));
            track.PlaylistList.Add(CreateClip((uint)subTrackIndex, clip, segmentDurationMs));
        }

        /// <summary>
        /// A new track carrying the vanilla one's fields. The lists are rebuilt rather than shared so
        /// merging cannot edit the repository's copy - the same reason the Music Switch container
        /// merge copies rather than mutates.
        /// </summary>
        private static CAkMusicTrack_V136 CopyTrack(CAkMusicTrack_V136 vanillaTrack)
        {
            var copy = new CAkMusicTrack_V136
            {
                Id = vanillaTrack.Id,
                HircType = vanillaTrack.HircType,
                Flags = vanillaTrack.Flags,
                NodeBaseParams = vanillaTrack.NodeBaseParams,
                TrackType = vanillaTrack.TrackType,
                LookAheadTime = vanillaTrack.LookAheadTime,
                NumSubTrack = vanillaTrack.NumSubTrack,
                TransRule = vanillaTrack.TransRule,
                SwitchParams = new CAkMusicTrack_V136.AkMusicTrackSwitchParams_V136
                {
                    GroupType = vanillaTrack.SwitchParams!.GroupType,
                    GroupId = vanillaTrack.SwitchParams.GroupId,
                    DefaultSwitch = vanillaTrack.SwitchParams.DefaultSwitch,
                    SwitchAssoc = [.. vanillaTrack.SwitchParams.SwitchAssoc]
                }
            };

            copy.SourceList.AddRange(vanillaTrack.SourceList);
            copy.PlaylistList.AddRange(vanillaTrack.PlaylistList);
            copy.ItemsList.AddRange(vanillaTrack.ItemsList);

            return copy;
        }

        private static AkBankSourceData_V136 CreateSource(AmsPulseClip clip)
        {
            return new AkBankSourceData_V136
            {
                PluginId = Wh3MusicHierarchyInformation.VorbisPluginId,
                StreamType = AKBKSourceType.Streaming,
                AkMediaInformation = new AkBankSourceData_V136.AkMediaInformation_V136
                {
                    SourceId = clip.SourceId,
                    InMemoryMediaSize = (uint)clip.InMemoryMediaSize,
                    SourceBits = (byte)(clip.Language == Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx) ? 0x00 : 0x01)
                }
            };
        }

        /// <summary>
        /// The clip, trimmed to the segment it is dropped into. Every vanilla pulse clip is trimmed
        /// this way - the stems are recorded longer than the segment and the tail is cut with a
        /// negative end trim - because a clip running past the segment would overlap the next one.
        /// Audio shorter than the segment is left whole and simply stops early, which is the only
        /// thing that can be done without inventing material.
        /// </summary>
        private static CAkMusicTrack_V136.AkTrackSrcInfo_V136 CreateClip(uint subTrackIndex, AmsPulseClip clip, double segmentDurationMs)
        {
            var overhang = clip.DurationMs - segmentDurationMs;

            return new CAkMusicTrack_V136.AkTrackSrcInfo_V136
            {
                TrackId = subTrackIndex,
                SourceId = clip.SourceId,
                EventId = 0,
                PlayAt = 0,
                BeginTrimOffset = 0,
                EndTrimOffset = overhang > 0 ? -overhang : 0,
                SrcDuration = clip.DurationMs
            };
        }
    }
}
