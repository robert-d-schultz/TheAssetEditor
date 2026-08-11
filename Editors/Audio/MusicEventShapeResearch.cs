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

