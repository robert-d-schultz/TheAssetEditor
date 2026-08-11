using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    // Research probe, not a behaviour assertion: dumps how vanilla builds the per-culture music
    // Action Events that the MusicDat wizard has to emulate, so the generated audio project can
    // be shaped to match rather than guessed at. Composes the app's own services rather than
    // re-reading packs by hand, so what it reports is what the Audio Explorer would show.
    [Explicit("Research probe, needs a WH3 install, run manually")]
    internal class MusicEventShapeResearch
    {
        const string GameDirectory = @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III";

        static readonly string[] EventsOfInterest =
        [
            "music_b_faction_empire",
            "music_b_faction_cathay",
            "music_c_subculture_empire",
            "music_c_ams_empire",
            "music_c_ams_pulse_perc_cathay",
            "music_c_ams_pulse_orch_cathay",
            "music_c_ams_pulse_ethnic_cathay",
        ];

        [Test]
        public void ProbeVanillaMusicEventShape()
        {
            var services = new ServiceCollection();
            new Shared.Core.DependencyInjectionContainer().Register(services);
            new Editors.Audio.DependencyInjectionContainer().Register(services);

            // The pack loader wants the UI dialog service for its error/progress reporting,
            // which has no meaning in a test - stubbed rather than pulling in the WPF layer.
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowWaitCursor()).Returns(new Mock<IWaitCursor>().Object);
            services.AddSingleton(dialogs.Object);
            services.AddSingleton<IFileSystemAccess, FileSystemAccess>();

            using var provider = services.BuildServiceProvider();

            var settings = provider.GetRequiredService<ApplicationSettingsService>();
            settings.CurrentSettings.CurrentGame = GameTypeEnum.Warhammer3;
            settings.CurrentSettings.GameDirectories.Clear();
            settings.CurrentSettings.GameDirectories.Add(
                new ApplicationSettings.GamePathPair(GameTypeEnum.Warhammer3, GameDirectory));

            var containerLoader = provider.GetRequiredService<IPackFileContainerLoader>();
            var packFileService = provider.GetRequiredService<IPackFileService>();

            // Only the audio packs, not the whole CA manifest: the bank data is all that matters
            // here and loading everything costs minutes. Marked as CA so the repository treats
            // the hircs as vanilla, which is what CreateFromGameEnum would have done.
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
            {
                var container = containerLoader.CreateFromPackFile(
                    PackFileContainerType.Normal, Path.Combine(GameDirectory, "data", packName), true);
                container.IsCaPackFile = true;
                packFileService.AddContainer(container);
            }

            var repository = provider.GetRequiredService<IAudioRepository>();
            repository.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx)]);

            foreach (var eventName in EventsOfInterest)
                DumpEvent(repository, eventName);

            // The other half of the picture: what the State an event sets actually selects. A
            // branch has to be merged into one of these trees for a new State to play anything,
            // so this walks a couple of vanilla branches end to end as the template to generate.
            DumpMusicSwitchBranches(repository, 698158058, "WH3_Campaign_Subcultures");
            DumpMusicSwitchBranches(repository, 26264058, "Battle_Music_WH3_Culture");

            DumpEveryMusicSwitchContainer(repository);

            // The multi argument trees, in full. A branch there is a path with a key per level, not
            // a single node, so generating one means knowing what every level expects - including
            // the levels that have nothing to do with the culture.
            DumpWholeDecisionTree(repository, 26264058);
            DumpWholeDecisionTree(repository, 145953291);
            DumpWholeDecisionTree(repository, 67383790);

            DumpWhatReadsTheAmsStateGroups(repository);
            DumpEveryPulseSwitchTrack(repository);
            DumpTheFragmentsSwitchContainer(repository);
        }

        /// <summary>
        /// The ambient fragments Switch container and one of its children in full. This one is not
        /// in the music hierarchy - it is a plain Switch container over Sounds - so serving it means
        /// generating a Sound parented and bussed the way its siblings are, not a music segment.
        /// </summary>
        static void DumpTheFragmentsSwitchContainer(IAudioRepository repository)
        {
            TestContext.Out.WriteLine("\n=== ambient fragments Switch container ===");

            if (Find(repository, 418295225) is not CAkSwitchCntr_V136 container)
            {
                TestContext.Out.WriteLine("  NOT FOUND");
                return;
            }

            TestContext.Out.WriteLine(
                $"  {container.Id} in {Path.GetFileName(container.BnkFilePath)} " +
                $"parent={container.NodeBaseParams.DirectParentId} " +
                $"bus={container.NodeBaseParams.OverrideBusId} " +
                $"children={container.Children.ChildIds.Count}");

            foreach (var switchPackage in container.SwitchList.OfType<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
                TestContext.Out.WriteLine(
                    $"  switch '{repository.GetNameFromId(switchPackage.SwitchId)}' -> {string.Join(", ", switchPackage.NodeIdList)}");

            foreach (var childId in container.Children.ChildIds.Take(2))
            {
                var child = Find(repository, childId);
                TestContext.Out.WriteLine($"  child {childId} is {child?.HircType.ToString() ?? "<missing>"}");

                if (child is CAkSwitchCntr_V136 nested)
                {
                    TestContext.Out.WriteLine(
                        $"    nested switch groupType={nested.EGroupType} group={nested.GroupId} " +
                        $"'{repository.GetNameFromId(nested.GroupId)}' default='{repository.GetNameFromId(nested.DefaultSwitch)}' " +
                        $"parent={nested.NodeBaseParams.DirectParentId} bus={nested.NodeBaseParams.OverrideBusId} " +
                        $"children={nested.Children.ChildIds.Count}");

                    foreach (var nestedPackage in nested.SwitchList.OfType<CAkSwitchCntr_V136.CAkSwitchPackage_V136>().Take(4))
                        TestContext.Out.WriteLine(
                            $"      '{repository.GetNameFromId(nestedPackage.SwitchId)}' -> {string.Join(", ", nestedPackage.NodeIdList)}");

                    foreach (var grandChildId in nested.Children.ChildIds.Take(2))
                    {
                        var grandChild = Find(repository, grandChildId);
                        TestContext.Out.WriteLine($"      grandchild {grandChildId} is {grandChild?.HircType.ToString() ?? "<missing>"}");

                        if (grandChild is CAkRanSeqCntr_V136 grandRanSeq)
                            TestContext.Out.WriteLine(
                                $"        ranseq children={grandRanSeq.Children.ChildIds.Count} " +
                                $"bus={grandRanSeq.NodeBaseParams.OverrideBusId}");

                        if (grandChild is CAkSound_V136 grandSound)
                            TestContext.Out.WriteLine(
                                $"        sound source={grandSound.AkBankSourceData.AkMediaInformation.SourceId} " +
                                $"streamType={grandSound.AkBankSourceData.StreamType} bus={grandSound.NodeBaseParams.OverrideBusId}");
                    }
                }

                if (child is CAkSound_V136 sound)
                    TestContext.Out.WriteLine(
                        $"    sound parent={sound.NodeBaseParams.DirectParentId} bus={sound.NodeBaseParams.OverrideBusId} " +
                        $"plugin={sound.AkBankSourceData.PluginId} streamType={sound.AkBankSourceData.StreamType} " +
                        $"source={sound.AkBankSourceData.AkMediaInformation.SourceId}");

                if (child is CAkRanSeqCntr_V136 ranSeq)
                    TestContext.Out.WriteLine(
                        $"    ranseq parent={ranSeq.NodeBaseParams.DirectParentId} bus={ranSeq.NodeBaseParams.OverrideBusId} " +
                        $"children={ranSeq.Children.ChildIds.Count}");
            }
        }

        /// <summary>
        /// Every switch track carrying the pulses, in full. The question this answers is what a new
        /// culture actually owes: there are nine tracks per Group, and whether that is nine separate
        /// stems or the same stem reached nine ways decides whether serving these Groups is a
        /// reasonable ask of a modder at all.
        /// </summary>
        static void DumpEveryPulseSwitchTrack(IAudioRepository repository)
        {
            string[] pulseGroups =
            [
                "WH3_AMS_Pulse_Percussion_Options",
                "WH3_AMS_Pulse_Pitched_Orchestral_Options",
                "WH3_AMS_Pulse_Pitched_Ethnic_Options"
            ];

            TestContext.Out.WriteLine("=== every pulse switch track ===");

            foreach (var stateGroupName in pulseGroups)
            {
                var groupId = WwiseHash.Compute(stateGroupName);
                TestContext.Out.WriteLine($"\n--- {stateGroupName} ---");

                var tracks = repository.GetHircs(AkBkHircType.Music_Track)
                    .OfType<CAkMusicTrack_V136>()
                    .Where(track => track.SwitchParams?.GroupId == groupId)
                    .ToList();

                foreach (var track in tracks)
                {
                    var parentSegment = Find(repository, track.NodeBaseParams.DirectParentId) as CAkMusicSegment_V136;
                    var grandParentId = parentSegment?.MusicNodeParams.NodeBaseParams.DirectParentId ?? 0;

                    TestContext.Out.WriteLine(
                        $"\n  track {track.Id} in {Path.GetFileName(track.BnkFilePath)}");
                    TestContext.Out.WriteLine(
                        $"    segment {track.NodeBaseParams.DirectParentId} -> ranseq/switch {grandParentId} " +
                        $"({repository.GetNameFromId(grandParentId)}) segmentDuration={parentSegment?.Duration:0}");
                    TestContext.Out.WriteLine(
                        $"    subTracks={track.NumSubTrack} sources={track.SourceList.Count} clips={track.PlaylistList.Count} " +
                        $"default='{repository.GetNameFromId(track.SwitchParams!.DefaultSwitch)}'");

                    // The association array is indexed by sub-track, so this is the mapping that has
                    // to stay aligned when a culture is added.
                    for (var subTrack = 0; subTrack < track.SwitchParams.SwitchAssoc.Count; subTrack++)
                    {
                        var clip = track.PlaylistList.FirstOrDefault(item => item.TrackId == subTrack);
                        TestContext.Out.WriteLine(
                            $"      sub {subTrack} '{repository.GetNameFromId(track.SwitchParams.SwitchAssoc[subTrack])}' " +
                            $"source={clip?.SourceId.ToString() ?? "<none>"} duration={clip?.SrcDuration ?? 0:0} " +
                            $"playAt={clip?.PlayAt ?? 0:0} beginTrim={clip?.BeginTrimOffset ?? 0:0} endTrim={clip?.EndTrimOffset ?? 0:0}");

                        var source = track.SourceList.FirstOrDefault(s => s.AkMediaInformation.SourceId == clip?.SourceId);
                        if (source != null)
                            TestContext.Out.WriteLine(
                                $"        source plugin={source.PluginId} streamType={source.StreamType} " +
                                $"inMemorySize={source.AkMediaInformation.InMemoryMediaSize}");
                    }
                }
            }
        }

        /// <summary>
        /// What actually consumes the four AMS State Groups. No Music Switch container branches on
        /// them, so a State set against one selects nothing at the tree level - but the events are
        /// vanilla and clearly do something, and the question is what. A Music Track can be a switch
        /// track, which picks between its own sub-tracks on a Group, and that is the other place a
        /// State is read.
        /// </summary>
        static void DumpWhatReadsTheAmsStateGroups(IAudioRepository repository)
        {
            string[] amsStateGroups =
            [
                "WH3_Campaign_Music_AMS_Fragments_Faction",
                "WH3_AMS_Pulse_Percussion_Options",
                "WH3_AMS_Pulse_Pitched_Orchestral_Options",
                "WH3_AMS_Pulse_Pitched_Ethnic_Options"
            ];

            var switchTracks = repository.GetHircs(AkBkHircType.Music_Track)
                .OfType<CAkMusicTrack_V136>()
                .Where(track => track.SwitchParams != null)
                .ToList();

            var switchContainers = repository.GetHircs(AkBkHircType.Music_Switch)
                .OfType<CAkMusicSwitchCntr_V136>()
                .ToList();

            TestContext.Out.WriteLine($"=== what reads the AMS State Groups ({switchTracks.Count} switch tracks in the game) ===");

            foreach (var stateGroupName in amsStateGroups)
            {
                var groupId = WwiseHash.Compute(stateGroupName);

                var containers = switchContainers
                    .Where(container => container.Arguments.Any(argument => argument.GroupId == groupId))
                    .ToList();

                var tracks = switchTracks
                    .Where(track => track.SwitchParams!.GroupId == groupId)
                    .ToList();

                TestContext.Out.WriteLine($"\n{stateGroupName} ({groupId})");
                TestContext.Out.WriteLine($"  Music Switch containers branching on it: {containers.Count}");
                TestContext.Out.WriteLine($"  switch tracks reading it: {tracks.Count}");

                // A State is read in more places than the music hierarchy: a plain Switch container
                // can be driven by a State Group, and any node can carry a State chunk that changes
                // its properties. A Group nothing in the music hierarchy reads may still be doing
                // one of those, so absence there is not absence full stop.
                var switchContainerCount = repository.GetHircs(AkBkHircType.SwitchContainer)
                    .OfType<CAkSwitchCntr_V136>()
                    .Count(container => container.GroupId == groupId);

                AkBkHircType[] stateAwareTypes =
                [
                    AkBkHircType.Sound, AkBkHircType.RandomSequenceContainer, AkBkHircType.SwitchContainer,
                    AkBkHircType.LayerContainer, AkBkHircType.ActorMixer, AkBkHircType.Music_Track,
                    AkBkHircType.Music_Segment, AkBkHircType.Music_Random_Sequence, AkBkHircType.Music_Switch
                ];

                var stateChunkCount = stateAwareTypes
                    .SelectMany(repository.GetHircs)
                    .Count(hirc => HircReadsStateGroup(hirc, groupId));

                TestContext.Out.WriteLine($"  plain Switch containers driven by it: {switchContainerCount}");

                foreach (var plainSwitch in repository.GetHircs(AkBkHircType.SwitchContainer)
                    .OfType<CAkSwitchCntr_V136>()
                    .Where(plainSwitch => plainSwitch.GroupId == groupId))
                {
                    TestContext.Out.WriteLine(
                        $"    switch container {plainSwitch.Id} in {Path.GetFileName(plainSwitch.BnkFilePath)} " +
                        $"groupType={plainSwitch.EGroupType} default='{repository.GetNameFromId(plainSwitch.DefaultSwitch)}' " +
                        $"children={plainSwitch.Children.ChildIds.Count} switches={plainSwitch.SwitchList.Count}");

                    foreach (var switchPackage in plainSwitch.SwitchList.OfType<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
                        TestContext.Out.WriteLine(
                            $"      '{repository.GetNameFromId(switchPackage.SwitchId)}' -> {switchPackage.NodeIdList.Count} node(s)");
                }
                TestContext.Out.WriteLine($"  nodes carrying a State chunk for it: {stateChunkCount}");

                foreach (var track in tracks.Take(3))
                {
                    TestContext.Out.WriteLine(
                        $"    track {track.Id} parent={track.NodeBaseParams.DirectParentId} " +
                        $"groupType={track.SwitchParams!.GroupType} default={track.SwitchParams.DefaultSwitch} " +
                        $"assoc={track.SwitchParams.SwitchAssoc.Count} subTracks={track.NumSubTrack} sources={track.SourceList.Count}");

                    foreach (var switchId in track.SwitchParams.SwitchAssoc)
                        TestContext.Out.WriteLine($"      assoc '{repository.GetNameFromId(switchId)}' ({switchId})");
                }
            }
        }

        /// <summary>Whether this hirc carries a State chunk for the Group - the mechanism by which a
        /// State changes a node's properties rather than selecting between children.</summary>
        static bool HircReadsStateGroup(HircItem hirc, uint groupId)
        {
            var nodeBaseParams = hirc switch
            {
                CAkSound_V136 sound => sound.NodeBaseParams,
                CAkRanSeqCntr_V136 ranSeq => ranSeq.NodeBaseParams,
                CAkSwitchCntr_V136 switchCntr => switchCntr.NodeBaseParams,
                CAkLayerCntr_V136 layer => layer.NodeBaseParams,
                CAkActorMixer_V136 actorMixer => actorMixer.NodeBaseParams,
                CAkMusicTrack_V136 track => track.NodeBaseParams,
                CAkMusicSegment_V136 segment => segment.MusicNodeParams.NodeBaseParams,
                CAkMusicRanSeqCntr_V136 musicRanSeq => musicRanSeq.MusicTransNodeParams.MusicNodeParams.NodeBaseParams,
                CAkMusicSwitchCntr_V136 musicSwitch => musicSwitch.MusicTransNodeParams.MusicNodeParams.NodeBaseParams,
                _ => null
            };

            return nodeBaseParams != null
                && nodeBaseParams.StateChunk.StateChunks.Any(stateChunk => stateChunk.StateGroupId == groupId);
        }

        static void DumpWholeDecisionTree(IAudioRepository repository, uint switchContainerId)
        {
            TestContext.Out.WriteLine($"=== whole tree {switchContainerId} ===");

            if (Find(repository, switchContainerId) is not CAkMusicSwitchCntr_V136 container)
            {
                TestContext.Out.WriteLine("  NOT FOUND");
                return;
            }

            for (var index = 0; index < container.Arguments.Count; index++)
                TestContext.Out.WriteLine($"  level {index}: {repository.GetNameFromId(container.Arguments[index].GroupId)}");

            DumpTreeNode(repository, container.AkDecisionTree.DecisionTree, "  ", 0);
        }

        static void DumpTreeNode(IAudioRepository repository, AkDecisionTree_V136.Node_V136 node, string indent, int level)
        {
            foreach (var child in node.Nodes)
            {
                var keyName = child.Key == 0 ? "<default>" : repository.GetNameFromId(child.Key);
                TestContext.Out.WriteLine(
                    $"{indent}L{level} key={child.Key} '{keyName}' audioNodeId={child.AudioNodeId} " +
                    $"children={child.Nodes.Count} weight={child.Weight} probability={child.Probability}");

                DumpTreeNode(repository, child, indent + "  ", level + 1);
            }
        }

        /// <summary>
        /// Every Music Switch container in the game with the State Groups it branches on. The wizard
        /// emits events against six State Groups and a State only plays anything if the container
        /// branching on that Group gets a branch merged in, so this is what says which of the six can
        /// be served at all - and which are too deep for a single keyed node to be a branch.
        /// </summary>
        static void DumpEveryMusicSwitchContainer(IAudioRepository repository)
        {
            TestContext.Out.WriteLine("=== every MusicSwitch container ===");

            foreach (var hircItem in repository.GetHircs(AkBkHircType.Music_Switch).Where(hirc => hirc.IsCA))
            {
                if (hircItem is not CAkMusicSwitchCntr_V136 container)
                    continue;

                var argumentNames = container.Arguments
                    .Select(argument => $"{argument.GroupType}:{repository.GetNameFromId(argument.GroupId)}");

                TestContext.Out.WriteLine(
                    $"  id={container.Id} depth={container.TreeDepth} mode={container.Mode} " +
                    $"branches={container.AkDecisionTree.DecisionTree.Nodes.Count} " +
                    $"args=[{string.Join(", ", argumentNames)}] bnk={Path.GetFileName(container.BnkFilePath)}");
            }
        }

        static void DumpMusicSwitchBranches(IAudioRepository repository, uint switchContainerId, string label)
        {
            TestContext.Out.WriteLine($"=== MusicSwitch {switchContainerId} ({label}) ===");

            if (Find(repository, switchContainerId) is not CAkMusicSwitchCntr_V136 container)
            {
                TestContext.Out.WriteLine("  NOT FOUND");
                return;
            }

            foreach (var argument in container.Arguments)
                TestContext.Out.WriteLine($"  arg {argument.GroupType} {argument.GroupId} '{repository.GetNameFromId(argument.GroupId)}'");

            foreach (var node in container.AkDecisionTree.DecisionTree.Nodes.Take(3))
            {
                TestContext.Out.WriteLine(
                    $"  node key={node.Key} '{repository.GetNameFromId(node.Key)}' -> audioNodeId={node.AudioNodeId} " +
                    $"weight={node.Weight} probability={node.Probability}");
                DumpMusicTarget(repository, node.AudioNodeId, "    ", depth: 0);
            }
        }

        static void DumpMusicTarget(IAudioRepository repository, uint id, string indent, int depth)
        {
            if (depth > 2 || id == 0)
                return;

            var target = Find(repository, id);
            if (target == null)
            {
                TestContext.Out.WriteLine($"{indent}{id}: NOT FOUND");
                return;
            }

            TestContext.Out.WriteLine($"{indent}{target.HircType} id={target.Id}");

            if (target is CAkMusicRanSeqCntr_V136 ranSeq)
            {
                var roots = ranSeq.PlayList;
                var nodeParams = ranSeq.MusicTransNodeParams.MusicNodeParams;
                TestContext.Out.WriteLine(
                    $"{indent}  playlistRoots={roots.Count} parent={nodeParams.NodeBaseParams.DirectParentId} " +
                    $"bus={nodeParams.NodeBaseParams.OverrideBusId} children={nodeParams.Children.ChildIds.Count} " +
                    $"tempo={nodeParams.AkMeterInfo.Tempo} rules={ranSeq.MusicTransNodeParams.PlayList.Count} " +
                    $"numPlaylistItems={ranSeq.NumPlaylistItems}");

                // Every field of the playlist nodes, because a generated container has to fill all
                // of them and none of them can be inferred from the structure alone.
                foreach (var root in roots)
                    DumpPlaylistItem(root, indent + "    ");

                foreach (var rule in ranSeq.MusicTransNodeParams.PlayList)
                    DumpTransitionRule(rule, indent + "    ");

                foreach (var item in roots.SelectMany(root => root.PlayList).Take(2))
                    DumpMusicTarget(repository, item.SegmentId, indent + "    ", depth + 1);
            }

            if (target is CAkMusicSegment_V136 segment)
            {
                var nodeParams = segment.MusicNodeParams;
                TestContext.Out.WriteLine(
                    $"{indent}  duration={segment.Duration} markers={segment.ArrayMarkersList.Count} " +
                    $"children={nodeParams.Children.ChildIds.Count} parent={nodeParams.NodeBaseParams.DirectParentId} " +
                    $"bus={nodeParams.NodeBaseParams.OverrideBusId} tempo={nodeParams.AkMeterInfo.Tempo}");

                foreach (var marker in segment.ArrayMarkersList)
                    TestContext.Out.WriteLine($"{indent}    marker id={marker.Id} pos={marker.Position} name='{marker.MarkerName}'");

                foreach (var childId in nodeParams.Children.ChildIds.Take(2))
                    DumpMusicTarget(repository, childId, indent + "    ", depth + 1);
            }

            if (target is CAkMusicTrack_V136 track)
            {
                TestContext.Out.WriteLine(
                    $"{indent}  trackType={track.TrackType} sources={track.SourceList.Count} playlist={track.PlaylistList.Count} " +
                    $"subTracks={track.NumSubTrack} lookAhead={track.LookAheadTime} parent={track.NodeBaseParams.DirectParentId} " +
                    $"bus={track.NodeBaseParams.OverrideBusId}");

                foreach (var source in track.SourceList)
                    TestContext.Out.WriteLine(
                        $"{indent}    source pluginId={source.PluginId} streamType={source.StreamType} " +
                        $"sourceId={source.AkMediaInformation.SourceId} inMemorySize={source.AkMediaInformation.InMemoryMediaSize}");

                foreach (var clip in track.PlaylistList)
                    TestContext.Out.WriteLine(
                        $"{indent}    clip track={clip.TrackId} source={clip.SourceId} playAt={clip.PlayAt} " +
                        $"beginTrim={clip.BeginTrimOffset} endTrim={clip.EndTrimOffset} duration={clip.SrcDuration}");
            }
        }

        static void DumpPlaylistItem(CAkMusicRanSeqCntr_V136.AkMusicRanSeqPlaylistItem_V136 item, string indent)
        {
            TestContext.Out.WriteLine(
                $"{indent}playlistItem segment={item.SegmentId} itemId={item.PlaylistItemId} children={item.NumChildren} " +
                $"rsType={item.RsType} loop={item.Loop} loopMin={item.LoopMin} loopMax={item.LoopMax} weight={item.Weight} " +
                $"avoidRepeat={item.AvoidRepeatCount} usingWeight={item.IsUsingWeight} shuffle={item.IsShuffle}");

            foreach (var child in item.PlayList)
                DumpPlaylistItem(child, indent + "  ");
        }

        static void DumpTransitionRule(MusicTransNodeParams_V136.AkMusicTransitionRule_V136 rule, string indent)
        {
            var src = rule.AkMusicTransSrcRule;
            var dst = rule.AkMusicTransDstRule;

            TestContext.Out.WriteLine(
                $"{indent}rule src=[{string.Join(",", rule.SrcIdList)}] dst=[{string.Join(",", rule.DstIdList)}] " +
                $"stateGroupCustom={rule.StateGroupIdCustom} stateCustom={rule.StateIdCustom} " +
                $"transObj={(rule.AkMusicTransitionObject == null ? "none" : "present")}");
            TestContext.Out.WriteLine(
                $"{indent}  srcRule time={src.TransitionTime} curve={src.FadeCurve} offset={src.FadeOffset} " +
                $"sync={src.SyncType} cueFilter={src.CueFilterHash} playPostExit={src.PlayPostExit}");
            TestContext.Out.WriteLine(
                $"{indent}  dstRule time={dst.TransitionTime} curve={dst.FadeCurve} offset={dst.FadeOffset} " +
                $"cueFilter={dst.CueFilterHash} jumpTo={dst.JumpToId} jumpToType={dst.JumpToType} " +
                $"entryType={dst.EntryType} playPreEntry={dst.PlayPreEntry} destMatchSourceCueName={dst.DestMatchSourceCueName}");
        }

        static void DumpEvent(IAudioRepository repository, string eventName)
        {
            var id = WwiseHash.Compute(eventName);
            TestContext.Out.WriteLine($"=== {eventName} (id {id}) ===");

            if (!repository.HircsById.TryGetValue(id, out var hircs) || hircs.Count == 0)
            {
                TestContext.Out.WriteLine("  NOT FOUND");
                return;
            }

            foreach (var hirc in hircs)
            {
                TestContext.Out.WriteLine($"  {hirc.HircType} in {hirc.BnkFilePath} (isCA={hirc.IsCA})");
                if (hirc is not ICAkEvent akEvent)
                    continue;

                foreach (var actionId in akEvent.GetActionIds())
                    DumpAction(repository, actionId, "    ");
            }
        }

        static void DumpAction(IAudioRepository repository, uint actionId, string indent)
        {
            var action = Find(repository, actionId);
            if (action is not ICAkAction akAction)
            {
                TestContext.Out.WriteLine($"{indent}action {actionId}: NOT FOUND");
                return;
            }

            var groupId = akAction.GetStateGroupId();
            TestContext.Out.WriteLine(
                $"{indent}Action {akAction.GetActionType()} -> child {akAction.GetChildId()} '{repository.GetNameFromId(akAction.GetChildId())}' " +
                $"| stateGroup {groupId} '{repository.GetNameFromId(groupId)}'");

            if (akAction.GetActionType() is AkActionType.SetState)
            {
                // Everything a generator has to reproduce byte for byte. There is no sound target
                // to walk, so this is the whole of the action.
                if (action is CAkAction_V136 v136)
                {
                    TestContext.Out.WriteLine(
                        $"{indent}  IdExt={v136.IdExt} IdExt4={v136.IdExt4} " +
                        $"stateGroupId={v136.StateActionParams!.StateGroupId} targetStateId={v136.StateActionParams.TargetStateId} " +
                        $"sectionSize={v136.SectionSize}");
                    TestContext.Out.WriteLine($"{indent}  props0=[{DescribeProps(v136.AkPropBundle0)}] props1=[{DescribeProps(v136.AkPropBundle1)}]");
                }
                return;
            }

            DumpTarget(repository, akAction.GetChildId(), indent + "  ");
        }

        static void DumpTarget(IAudioRepository repository, uint targetId, string indent)
        {
            var target = Find(repository, targetId);
            if (target == null)
            {
                TestContext.Out.WriteLine($"{indent}target {targetId}: NOT FOUND");
                return;
            }

            TestContext.Out.WriteLine($"{indent}{target.HircType} id={target.Id}");

            if (target is ICAkSound sound)
                TestContext.Out.WriteLine($"{indent}  Sound source={sound.GetSourceId()} stream={sound.GetStreamType()} parent={sound.GetDirectParentId()}");

            if (target is ICAkRanSeqCntr container)
            {
                var children = container.GetChildren();
                TestContext.Out.WriteLine($"{indent}  RanSeqCntr parent={container.GetDirectParentId()} children={children.Count}");
                foreach (var child in children.Take(4))
                    DumpTarget(repository, child, indent + "    ");
            }

            if (target is ICAkLayerCntr layer)
                TestContext.Out.WriteLine($"{indent}  LayerCntr parent={layer.GetDirectParentId()} children={layer.GetChildren().Count}");

            if (target is ICAkSwitchCntr sw)
                TestContext.Out.WriteLine($"{indent}  SwitchCntr parent={sw.GetDirectParentId()} group={sw.GroupId} packages={sw.SwitchList.Count}");

            if (target is ICAkMusicTrack track)
                TestContext.Out.WriteLine($"{indent}  MusicTrack children={track.GetChildren().Count}");
        }

        static string DescribeProps(Shared.GameFormats.Wwise.Hirc.V136.Shared.AkPropBundle_V136 bundle) =>
            string.Join(", ", bundle.PropsList.Select(prop => $"{prop.Id}={prop.Value}"));

        static HircItem? Find(IAudioRepository repository, uint id) =>
            repository.HircsById.TryGetValue(id, out var items) ? items.FirstOrDefault() : null;
    }
}

