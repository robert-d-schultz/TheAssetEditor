using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.GameFormats.MusicDat;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    // Why the shipped pack is silent in game. Everything here reads what is actually on disk -
    // the vanilla packs and the pack the end to end run wrote - rather than the in memory model.
    [Explicit("Diagnosis, needs a WH3 install and the end to end run's output pack")]
    internal class MusicShippedPackDiagnosis
    {
        const string GameDirectory = @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III";
        static string ModPackPath => Path.Combine(Path.GetTempPath(), "ArabyMusicRun", "araby_music.pack");

        /// <summary>Which vanilla .bnk holds each hirc the mod re-emits. The testing .bnk only
        /// overrides vanilla if it is named after the .bnk the hirc actually came from.</summary>
        [Test]
        public void WhichVanillaBankHoldsWhat()
        {
            uint[] wanted = [698158058, 26264058, 418295225];

            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                foreach (var (path, bytes) in VanillaBankReader.ReadMusicBanks(packPath))
                {
                    List<uint> found;
                    try
                    {
                        var bnk = BnkFile.CreateFromBytes(bytes, path, false);
                        found = bnk.HircChunk.HircItems
                            .Where(hirc => wanted.Contains(hirc.Id))
                            .Select(hirc => hirc.Id)
                            .ToList();
                    }
                    catch
                    {
                        continue;
                    }

                    if (found.Count != 0)
                        Console.WriteLine($"{packName}  {path}  ->  {string.Join(", ", found)}");
                }
            }
        }

        /// <summary>The new culture's branch walked to the wem, next to a vanilla culture's, out of
        /// the .bnk bytes that actually shipped. Anything the mod gets structurally wrong shows up
        /// as a difference against the vanilla column.</summary>
        [Test]
        public void CompareTheArabyChainWithVanilla()
        {
            var hircs = new Dictionary<uint, HircItem>();

            // Vanilla first, then the mod, so the mod's copy of a shared id wins - the same as the
            // load order the .bnk names buy.
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (File.Exists(packPath))
                    IndexBanks(hircs, VanillaBankReader.ReadMusicBanks(packPath));
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            // The merging .bnk never ships and carries the mod's branches alone, so indexing it here
            // would shadow the fully merged container in the testing .bnk and hide vanilla's branches.
            IndexBanks(hircs, modPack.GetAllFiles()
                .Where(file => file.Key.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)
                    && !file.Key.Contains("_for_merging", StringComparison.OrdinalIgnoreCase))
                .Select(file => (file.Key, file.Value.DataSource.ReadData()))
                .ToList());

            foreach (var (containerId, label) in new[] { (698158058u, "campaign subculture"), (26264058u, "battle culture") })
            {
                var container = (CAkMusicSwitchCntr_V136)hircs[containerId];
                Console.WriteLine($"\n################ {label} container {containerId} ################");
                Console.WriteLine($"tree depth {container.TreeDepth}, arguments {string.Join(", ", container.Arguments.Select(a => a.GroupId))}");

                Console.WriteLine($"top level keys ({container.AkDecisionTree.DecisionTree.Nodes.Count}): " +
                    string.Join(", ", container.AkDecisionTree.DecisionTree.Nodes.Select(node => node.Key)));

                foreach (var culture in new[] { "araby", "cathay" })
                {
                    Console.WriteLine($"\n---- {culture} ----");
                    DumpLeaves(hircs, container.AkDecisionTree.DecisionTree, WwiseHash.Compute(culture), "", []);
                }
            }
        }

        /// <summary>Every branch key in the two containers, named where the shipped banks name the
        /// hash. What the game sets the State to has to be one of these, or the tree falls through
        /// to its default and vanilla plays.</summary>
        [Test]
        public void WhatTheContainersBranchOn()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
                VanillaBankReader.OpenPack(provider, Path.Combine(GameDirectory, "data", packName));

            var repository = provider.GetRequiredService<Editors.Audio.Shared.Storage.IAudioRepository>();
            repository.Load(["sfx"]);

            foreach (var containerId in new uint[] { 698158058, 26264058 })
            {
                var container = repository.GetHircs(containerId).OfType<CAkMusicSwitchCntr_V136>().Single(x => x.IsCA);
                Console.WriteLine($"\n################ {containerId} ################");
                Console.WriteLine("arguments: " + string.Join(", ",
                    container.Arguments.Select(argument => $"{Name(repository, argument.GroupId)}({argument.GroupId})")));

                DumpKeys(repository, container.AkDecisionTree.DecisionTree, "  ");
            }
        }

        static void DumpKeys(Editors.Audio.Shared.Storage.IAudioRepository repository, AkDecisionTree_V136.Node_V136 node, string indent)
        {
            foreach (var child in node.Nodes)
            {
                Console.WriteLine($"{indent}{Name(repository, child.Key)}({child.Key})" +
                    (child.Nodes.Count == 0 ? $" -> {child.AudioNodeId}" : ""));
                DumpKeys(repository, child, indent + "  ");
            }
        }

        static string Name(Editors.Audio.Shared.Storage.IAudioRepository repository, uint hash)
        {
            var name = repository.GetNameFromId(hash, out var found);
            return found ? name : "?";
        }

        /// <summary>The State each event sets, next to the branch key that State has to hit. These
        /// are generated from two different strings, so a case difference between them is silence.</summary>
        [Test]
        public void WhatTheEventsSetAgainstWhatTheTreeExpects()
        {
            Console.WriteLine($"hash('araby')  = {WwiseHash.Compute("araby")}");
            Console.WriteLine($"hash('Araby')  = {WwiseHash.Compute("Araby")}");
            Console.WriteLine($"hash('cathay') = {WwiseHash.Compute("cathay")}");
            Console.WriteLine($"hash('Cathay') = {WwiseHash.Compute("Cathay")}  (vanilla tree key is 2752566535)");

            var hircs = new Dictionary<uint, HircItem>();
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (File.Exists(packPath))
                    IndexBanks(hircs, VanillaBankReader.ReadMusicBanks(packPath));
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            IndexBanks(hircs, modPack.GetAllFiles()
                .Where(file => file.Key.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                .Select(file => (file.Key, file.Value.DataSource.ReadData()))
                .ToList());

            foreach (var eventName in new[]
            {
                "music_c_subculture_cathay", "music_c_subculture_araby",
                "music_b_faction_cathay", "music_b_faction_araby",
                "music_c_ams_pulse_perc_cathay", "music_c_ams_pulse_perc_araby",
            })
            {
                Console.WriteLine($"\n{eventName} ({WwiseHash.Compute(eventName)})");
                if (!hircs.TryGetValue(WwiseHash.Compute(eventName), out var hirc) || hirc is not CAkEvent_V136 akEvent)
                {
                    Console.WriteLine("  !! not in any .bnk");
                    continue;
                }

                foreach (var actionId in akEvent.GetActionIds())
                {
                    if (!hircs.TryGetValue(actionId, out var actionHirc) || actionHirc is not CAkAction_V136 action)
                    {
                        Console.WriteLine($"  !! action {actionId} missing");
                        continue;
                    }

                    Console.WriteLine($"  {action.ActionType} stateGroup {action.StateActionParams?.StateGroupId} -> state {action.IdExt}");
                }
            }
        }

        /// <summary>Each .bnk the mod ships, and what it says about the two vanilla containers. The
        /// testing bank has to carry vanilla's branches; the merging bank is only the mod's.</summary>
        [Test]
        public void WhatEachShippedBankSaysAboutTheContainers()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            foreach (var file in modPack.GetAllFiles().Where(x => x.Key.EndsWith(".bnk")).OrderBy(x => x.Key))
            {
                var bnk = BnkFile.CreateFromBytes(file.Value.DataSource.ReadData(), file.Key, false);
                Console.WriteLine($"\n================ {file.Key} ================");
                Console.WriteLine("hircs: " + string.Join(", ", bnk.HircChunk.HircItems
                    .GroupBy(hirc => hirc.HircType)
                    .Select(group => $"{group.Key} x{group.Count()}")));

                foreach (var container in bnk.HircChunk.HircItems.OfType<CAkMusicSwitchCntr_V136>())
                {
                    var keys = container.AkDecisionTree.DecisionTree.Nodes;
                    Console.WriteLine($"  Music_Switch {container.Id}: depth {container.TreeDepth}, " +
                        $"{keys.Count} top level keys -> {string.Join(", ", keys.Select(node => node.Key))}");

                    var children = container.MusicTransNodeParams.MusicNodeParams.Children;
                    Console.WriteLine($"    children ({children.NumChilds} declared, {children.ChildIds.Count} listed): " +
                        string.Join(", ", children.ChildIds));

                    var leaves = new List<uint>();
                    CollectLeaves(container.AkDecisionTree.DecisionTree, leaves);
                    var orphans = leaves.Distinct().Where(id => id != 0 && !children.ChildIds.Contains(id)).ToList();
                    Console.WriteLine(orphans.Count == 0
                        ? "    every leaf is a declared child"
                        : $"    !! leaves that are NOT declared children: {string.Join(", ", orphans)}");
                }

                foreach (var container in bnk.HircChunk.HircItems.OfType<CAkSwitchCntr_V136>())
                    Console.WriteLine($"  SwitchContainer {container.Id}: {container.SwitchList.Count} switches, " +
                        $"{container.Children.ChildIds.Count} children");

                foreach (var track in bnk.HircChunk.HircItems.OfType<CAkMusicTrack_V136>().Where(x => x.TrackType == CAkMusicTrack_V136.SwitchTrackType))
                    Console.WriteLine($"  switch Music_Track {track.Id}: {track.SwitchParams!.SwitchAssoc.Count} sub-tracks");
            }
        }

        static void CollectLeaves(AkDecisionTree_V136.Node_V136 node, List<uint> leaves)
        {
            foreach (var child in node.Nodes)
            {
                if (child.Nodes.Count == 0)
                    leaves.Add(child.AudioNodeId);
                else
                    CollectLeaves(child, leaves);
            }
        }

        static void IndexBanks(Dictionary<uint, HircItem> hircs, List<(string Path, byte[] Bytes)> banks)
        {
            foreach (var (path, bytes) in banks)
            {
                try
                {
                    foreach (var hirc in BnkFile.CreateFromBytes(bytes, path, false).HircChunk.HircItems)
                        hircs[hirc.Id] = hirc;
                }
                catch
                {
                }
            }
        }

        /// <summary>Every leaf reachable through a node keyed on the culture, dumped from the leaf
        /// down. A branch can sit at any depth, so the key is looked for on the way down.</summary>
        static void DumpLeaves(Dictionary<uint, HircItem> hircs, AkDecisionTree_V136.Node_V136 node, uint key, string path, HashSet<uint> seen)
        {
            foreach (var child in node.Nodes)
            {
                var matched = child.Key == key || seen.Count != 0;
                var childPath = $"{path}/{child.Key}";

                if (child.Nodes.Count == 0)
                {
                    if (matched)
                    {
                        Console.WriteLine($"  leaf {childPath} -> {child.AudioNodeId}");
                        DumpTarget(hircs, child.AudioNodeId, "    ", 0);
                    }
                    continue;
                }

                DumpLeaves(hircs, child, key, childPath, matched ? [key] : seen);
            }
        }

        static void DumpTarget(Dictionary<uint, HircItem> hircs, uint id, string indent, int depth)
        {
            if (depth > 6)
                return;

            if (!hircs.TryGetValue(id, out var hirc))
            {
                Console.WriteLine($"{indent}!! {id} is in no .bnk");
                return;
            }

            Console.WriteLine($"{indent}{hirc.HircType} {id}");

            switch (hirc)
            {
                case CAkMusicRanSeqCntr_V136 ranSeq:
                    Console.WriteLine($"{indent}  children: {string.Join(", ", ranSeq.MusicTransNodeParams.MusicNodeParams.Children.ChildIds)}");
                    DumpPlaylist(hircs, ranSeq.PlayList, indent + "  ", depth);
                    break;

                case CAkMusicSegment_V136 segment:
                    Console.WriteLine($"{indent}  duration {segment.Duration}ms, markers {segment.ArrayMarkersList.Count}: " +
                        string.Join(", ", segment.ArrayMarkersList.Select(marker => $"{marker.Position}ms '{marker.MarkerName}'")));
                    foreach (var childId in segment.MusicNodeParams.Children.ChildIds)
                        DumpTarget(hircs, childId, indent + "  ", depth + 1);
                    break;

                case CAkMusicTrack_V136 track:
                    Console.WriteLine($"{indent}  trackType {track.TrackType}, subTracks {track.NumSubTrack}, lookAhead {track.LookAheadTime}");
                    foreach (var source in track.SourceList)
                        Console.WriteLine($"{indent}  source {source.AkMediaInformation.SourceId} " +
                            $"plugin {source.PluginId} stream {source.StreamType} inMemory {source.AkMediaInformation.InMemoryMediaSize}");
                    foreach (var clip in track.PlaylistList)
                        Console.WriteLine($"{indent}  clip sub{clip.TrackId} src {clip.SourceId} " +
                            $"playAt {clip.PlayAt} begin {clip.BeginTrimOffset} end {clip.EndTrimOffset} dur {clip.SrcDuration}");
                    break;
            }
        }

        static void DumpPlaylist(Dictionary<uint, HircItem> hircs, List<CAkMusicRanSeqCntr_V136.AkMusicRanSeqPlaylistItem_V136> items, string indent, int depth)
        {
            foreach (var item in items)
            {
                Console.WriteLine($"{indent}playlist segment {item.SegmentId} rsType {item.RsType} loop {item.Loop} weight {item.Weight} children {item.NumChildren}");
                if (item.SegmentId != 0)
                    DumpTarget(hircs, item.SegmentId, indent + "  ", depth + 1);
                DumpPlaylist(hircs, item.PlayList, indent + "  ", depth);
            }
        }

        /// <summary>Which .bnk holds each child of the two containers, in vanilla. If vanilla always
        /// keeps a music child in the same .bnk as its parent then a mod that splits them across two
        /// .bnks is not doing what the game does, however correct the ids look.</summary>
        [Test]
        public void WhetherVanillaKeepsMusicChildrenInTheParentsBank()
        {
            var bankByHirc = new Dictionary<uint, string>();
            var containersByBank = new Dictionary<uint, (string Bank, List<uint> Children)>();

            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                foreach (var (path, bytes) in VanillaBankReader.ReadMusicBanks(packPath))
                {
                    BnkFile bnk;
                    try
                    {
                        bnk = BnkFile.CreateFromBytes(bytes, path, false);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var hirc in bnk.HircChunk?.HircItems ?? [])
                    {
                        bankByHirc[hirc.Id] = path;
                        if (hirc is CAkMusicSwitchCntr_V136 container && (container.Id == 698158058 || container.Id == 26264058))
                            containersByBank[container.Id] = (path, container.MusicTransNodeParams.MusicNodeParams.Children.ChildIds);
                    }
                }
            }

            foreach (var (containerId, (bank, children)) in containersByBank)
            {
                Console.WriteLine($"\n################ {containerId} lives in {bank} ################");
                foreach (var group in children.GroupBy(childId => bankByHirc.TryGetValue(childId, out var b) ? b : "!! nowhere"))
                    Console.WriteLine($"  {group.Count()} of {children.Count} children in {group.Key}");
            }
        }

        /// <summary>Where vanilla keeps its music media, next to where the mod put its own. A track
        /// that streams is silent unless the game finds the .wem where it expects it, and the mod
        /// only ever writes to the sfx path the sound banks use.</summary>
        [Test]
        public void WhereTheMusicMediaLives()
        {
            foreach (var packName in new[] { "audio_base_m.pack", "audio_base.pack", "audio_base_bnk.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                using var vanillaProvider = VanillaBankReader.CreateProvider(GameDirectory);
                var pack = VanillaBankReader.OpenPack(vanillaProvider, packPath);

                var wems = pack.GetAllFiles()
                    .Where(file => file.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"\n================ {packName} ================");
                Console.WriteLine($"{wems.Count} wem(s)");
                foreach (var folder in wems.GroupBy(file => Path.GetDirectoryName(file.Key)).OrderByDescending(group => group.Count()))
                    Console.WriteLine($"  {folder.Key}  x{folder.Count()}   e.g. {folder.First().Key}");
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            Console.WriteLine($"\n================ the mod's pack ================");
            foreach (var file in modPack.GetAllFiles().OrderBy(file => file.Key))
                Console.WriteLine($"  {file.Key}  ({file.Value.DataSource.Size} bytes)");
        }

        /// <summary>A vanilla music track's source, so the mod's can be read against it rather than
        /// against an assumption about how music is packaged.</summary>
        [Test]
        public void HowVanillaMusicTracksCarryTheirAudio()
        {
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                foreach (var (path, bytes) in VanillaBankReader.ReadMusicBanks(packPath))
                {
                    BnkFile bnk;
                    try
                    {
                        bnk = BnkFile.CreateFromBytes(bytes, path, false);
                    }
                    catch
                    {
                        continue;
                    }

                    var tracks = (bnk.HircChunk?.HircItems ?? []).OfType<CAkMusicTrack_V136>().ToList();
                    if (tracks.Count == 0)
                        continue;

                    var sources = tracks.SelectMany(track => track.SourceList).ToList();
                    Console.WriteLine($"\n{packName}  {path}: {tracks.Count} track(s), {sources.Count} source(s)");
                    foreach (var group in sources.GroupBy(source => (source.StreamType, source.PluginId)))
                        Console.WriteLine($"  {group.Key.StreamType} plugin {group.Key.PluginId} x{group.Count()}" +
                            $"   e.g. source {group.First().AkMediaInformation.SourceId} inMemory {group.First().AkMediaInformation.InMemoryMediaSize}");
                }
            }
        }

        /// <summary>The size a music source declares in the .bnk against the size of the .wem it
        /// names. The mod writes the whole file size there; if vanilla writes something much smaller
        /// then the field is a prefetch buffer, and claiming a prefetch the .bnk does not carry is a
        /// reason for a segment to select and then play nothing.</summary>
        [Test]
        public void WhatMusicSourcesDeclareAgainstTheWemTheyName()
        {
            var wemSizeById = new Dictionary<uint, long>();
            foreach (var packName in new[] { "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                using var vanillaProvider = VanillaBankReader.CreateProvider(GameDirectory);
                foreach (var file in VanillaBankReader.OpenPack(vanillaProvider, packPath).GetAllFiles())
                {
                    if (file.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
                        && uint.TryParse(Path.GetFileNameWithoutExtension(file.Key), out var sourceId))
                        wemSizeById[sourceId] = file.Value.DataSource.Size;
                }
            }

            Console.WriteLine($"{wemSizeById.Count} vanilla wems indexed");

            foreach (var bankName in new[] { "global_music__core.bnk", "campaign_music__core.bnk", "battle_music__core.bnk" })
            {
                var bytes = VanillaBankReader.ReadMusicBanks(Path.Combine(GameDirectory, "data", "audio_base_bnk.pack"))
                    .FirstOrDefault(bank => bank.Path.EndsWith(bankName, StringComparison.OrdinalIgnoreCase));
                if (bytes.Bytes == null)
                    continue;

                var sources = (BnkFile.CreateFromBytes(bytes.Bytes, bytes.Path, false).HircChunk?.HircItems ?? [])
                    .OfType<CAkMusicTrack_V136>()
                    .SelectMany(track => track.SourceList)
                    .Where(source => wemSizeById.ContainsKey(source.AkMediaInformation.SourceId))
                    .ToList();

                if (sources.Count == 0)
                    continue;

                var wholeFile = sources.Count(source => source.AkMediaInformation.InMemoryMediaSize == wemSizeById[source.AkMediaInformation.SourceId]);

                Console.WriteLine($"\n{bankName}: {sources.Count} source(s) whose wem was found");
                Console.WriteLine($"  declared == whole wem: {wholeFile}");
                Console.WriteLine($"  declared <  whole wem: {sources.Count - wholeFile}");

                foreach (var source in sources.Take(6))
                    Console.WriteLine($"    source {source.AkMediaInformation.SourceId}: " +
                        $"declared {source.AkMediaInformation.InMemoryMediaSize}, wem {wemSizeById[source.AkMediaInformation.SourceId]}");
            }

            Console.WriteLine($"\nthe mod declares 817726 for source 121350730, whose wem is 817726 bytes (the whole file)");

            // Sfx is the pattern the editor already ships and modders already hear, so whether
            // vanilla sounds declare the whole file decides if this is a music-only mistake or just
            // how the editor has always written the field.
            var soundBanks = VanillaBankReader.ReadMusicBanks(Path.Combine(GameDirectory, "data", "audio_base_bnk.pack"));
            var checkedBanks = 0;

            foreach (var (sfxBankName, bankBytes) in soundBanks)
            {
                if (checkedBanks >= 3)
                    break;

                List<CAkSound_V136> sounds;
                try
                {
                    sounds = (BnkFile.CreateFromBytes(bankBytes, sfxBankName, false).HircChunk?.HircItems ?? [])
                        .OfType<CAkSound_V136>()
                        .Where(sound => wemSizeById.ContainsKey(sound.AkBankSourceData.AkMediaInformation.SourceId))
                        .ToList();
                }
                catch
                {
                    continue;
                }

                if (sounds.Count == 0)
                    continue;

                checkedBanks++;
                var wholeFile = sounds.Count(sound =>
                    sound.AkBankSourceData.AkMediaInformation.InMemoryMediaSize == wemSizeById[sound.AkBankSourceData.AkMediaInformation.SourceId]);

                Console.WriteLine($"\n{sfxBankName}: {sounds.Count} sound(s), declared == whole wem: {wholeFile}");
                foreach (var sound in sounds.Take(4))
                    Console.WriteLine($"    {sound.AkBankSourceData.StreamType} source {sound.AkBankSourceData.AkMediaInformation.SourceId}: " +
                        $"declared {sound.AkBankSourceData.AkMediaInformation.InMemoryMediaSize}, " +
                        $"wem {wemSizeById[sound.AkBankSourceData.AkMediaInformation.SourceId]}");
            }
        }

        /// <summary>The mod's chain against Cathay's, field by field. Cathay plays in game under the
        /// same conditions the mod is silent under, so it is the control: anything the mod sets
        /// differently below the decision tree is a candidate, and anything it sets the same is not.</summary>
        [Test]
        public void TheArabyChainFieldByFieldAgainstCathay()
        {
            var hircs = new Dictionary<uint, HircItem>();
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (File.Exists(packPath))
                    IndexBanks(hircs, VanillaBankReader.ReadMusicBanks(packPath));
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            IndexBanks(hircs, modPack.GetAllFiles()
                .Where(file => file.Key.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)
                    && !file.Key.Contains("_for_merging", StringComparison.OrdinalIgnoreCase))
                .Select(file => (file.Key, file.Value.DataSource.ReadData()))
                .ToList());

            var container = (CAkMusicSwitchCntr_V136)hircs[698158058];

            foreach (var culture in new[] { "cathay", "araby" })
            {
                var key = WwiseHash.Compute(culture);
                var leaf = container.AkDecisionTree.DecisionTree.Nodes.FirstOrDefault(node => node.Key == key);
                Console.WriteLine($"\n################ {culture} ################");

                if (leaf == null)
                {
                    Console.WriteLine("  no branch");
                    continue;
                }

                DumpChainFields(hircs, leaf.AudioNodeId, "  ", 0);
            }
        }

        static void DumpChainFields(Dictionary<uint, HircItem> hircs, uint id, string indent, int depth)
        {
            if (depth > 4 || !hircs.TryGetValue(id, out var hirc))
            {
                Console.WriteLine($"{indent}!! {id} not found");
                return;
            }

            switch (hirc)
            {
                case CAkMusicRanSeqCntr_V136 ranSeq:
                    DumpNodeBaseParams($"{indent}RanSeq {id}", ranSeq.MusicTransNodeParams.MusicNodeParams);
                    foreach (var childId in ranSeq.MusicTransNodeParams.MusicNodeParams.Children.ChildIds)
                        DumpChainFields(hircs, childId, indent + "  ", depth + 1);
                    break;

                case CAkMusicSegment_V136 segment:
                    DumpNodeBaseParams($"{indent}Segment {id}", segment.MusicNodeParams);
                    Console.WriteLine($"{indent}  duration {segment.Duration:F0}ms, markers " +
                        string.Join(" | ", segment.ArrayMarkersList.Select(marker => $"{marker.Id}@{marker.Position:F0}'{marker.MarkerName?.Replace("\0", "\\0")}'")));
                    foreach (var childId in segment.MusicNodeParams.Children.ChildIds)
                        DumpChainFields(hircs, childId, indent + "  ", depth + 1);
                    break;

                case CAkMusicTrack_V136 track:
                    var baseParams = track.NodeBaseParams;
                    Console.WriteLine($"{indent}Track {id}: bus {baseParams.OverrideBusId}, parent {baseParams.DirectParentId}, " +
                        $"bitVector {baseParams.BitVector}, overrideAttachment {baseParams.OverrideAttachmentParams}");
                    Console.WriteLine($"{indent}  trackType {track.TrackType}, lookAhead {track.LookAheadTime}, " +
                        $"numSubTrack {track.NumSubTrack}");
                    foreach (var source in track.SourceList)
                        Console.WriteLine($"{indent}  source {source.AkMediaInformation.SourceId}: {source.StreamType}, " +
                            $"plugin {source.PluginId}, inMemory {source.AkMediaInformation.InMemoryMediaSize}, " +
                            $"sourceBits {source.AkMediaInformation.SourceBits}");
                    foreach (var clip in track.PlaylistList)
                        Console.WriteLine($"{indent}  clip sub{clip.TrackId} src {clip.SourceId} eventId {clip.EventId} " +
                            $"playAt {clip.PlayAt:F0} begin {clip.BeginTrimOffset:F0} end {clip.EndTrimOffset:F0} dur {clip.SrcDuration:F0}");
                    break;
            }
        }

        static void DumpNodeBaseParams(string label, MusicNodeParams_V136 musicNodeParams)
        {
            var baseParams = musicNodeParams.NodeBaseParams;
            Console.WriteLine($"{label}: bus {baseParams.OverrideBusId}, parent {baseParams.DirectParentId}, " +
                $"bitVector {baseParams.BitVector}, overrideAttachment {baseParams.OverrideAttachmentParams}, " +
                $"flags {musicNodeParams.Flags}, children {musicNodeParams.Children.ChildIds.Count}, " +
                $"meter {musicNodeParams.MeterInfoFlag}, stingers {musicNodeParams.NumStingers}");
        }

        /// <summary>The chunks a vanilla music .bnk is made of, against the mod's. A source that
        /// declares a prefetch has to have somewhere to read it from, and DIDX/DATA is that
        /// somewhere - so whether vanilla music .bnks carry one decides what the small size means.</summary>
        [Test]
        public void WhichChunksVanillaMusicBanksCarry()
        {
            foreach (var (path, bytes) in VanillaBankReader.ReadMusicBanks(Path.Combine(GameDirectory, "data", "audio_base_bnk.pack")))
            {
                if (!path.EndsWith("global_music__core.bnk") && !path.EndsWith("campaign_music__core.bnk")
                    && !path.EndsWith("battle_music__core.bnk"))
                    continue;

                Console.WriteLine($"\n{path} ({bytes.Length} bytes): {string.Join(", ", ReadChunkTags(bytes))}");
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            foreach (var file in VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false).GetAllFiles()
                .Where(file => file.Key.EndsWith(".bnk"))
                .OrderBy(file => file.Key))
            {
                var bytes = file.Value.DataSource.ReadData();
                Console.WriteLine($"\n{file.Key} ({bytes.Length} bytes): {string.Join(", ", ReadChunkTags(bytes))}");
            }
        }

        /// <summary>The .bnk chunk table, walked by the length each chunk declares.</summary>
        static List<string> ReadChunkTags(byte[] bytes)
        {
            var tags = new List<string>();
            var offset = 0;

            while (offset + 8 <= bytes.Length)
            {
                var tag = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                var length = BitConverter.ToUInt32(bytes, offset + 4);
                tags.Add($"{tag}({length})");

                if (length == 0 || offset + 8 + length > (uint)bytes.Length)
                    break;

                offset += 8 + (int)length;
            }

            return tags;
        }

        /// <summary>What the declared size actually measures, tested against the RIFF layout of the
        /// wem it names. Vanilla music .bnks carry no DIDX/DATA, so the number cannot be media held
        /// in the .bnk - the hypothesis under test is that it is the header the game loads before it
        /// streams the rest, which would make it the offset at which the audio payload starts.</summary>
        [Test]
        public void WhatTheDeclaredSizeMeasuresInTheWem()
        {
            var wemById = new Dictionary<uint, byte[]>();
            var wanted = new HashSet<uint>();

            var bank = VanillaBankReader.ReadMusicBanks(Path.Combine(GameDirectory, "data", "audio_base_bnk.pack"))
                .First(x => x.Path.EndsWith("global_music__core.bnk"));

            var sources = (BnkFile.CreateFromBytes(bank.Bytes, bank.Path, false).HircChunk?.HircItems ?? [])
                .OfType<CAkMusicTrack_V136>()
                .SelectMany(track => track.SourceList)
                .GroupBy(source => source.AkMediaInformation.SourceId)
                .Select(group => group.First())
                .Take(8)
                .ToList();

            foreach (var source in sources)
                wanted.Add(source.AkMediaInformation.SourceId);

            foreach (var packName in new[] { "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                using var vanillaProvider = VanillaBankReader.CreateProvider(GameDirectory);
                foreach (var file in VanillaBankReader.OpenPack(vanillaProvider, packPath).GetAllFiles())
                {
                    if (file.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
                        && uint.TryParse(Path.GetFileNameWithoutExtension(file.Key), out var sourceId)
                        && wanted.Contains(sourceId))
                        wemById[sourceId] = file.Value.DataSource.ReadData();
                }
            }

            foreach (var source in sources)
            {
                var sourceId = source.AkMediaInformation.SourceId;
                if (!wemById.TryGetValue(sourceId, out var bytes))
                    continue;

                Console.WriteLine($"\nsource {sourceId}: declared {source.AkMediaInformation.InMemoryMediaSize}, wem {bytes.Length}");
                foreach (var (tag, start, length) in ReadRiffChunks(bytes))
                    Console.WriteLine($"    {tag} at {start}, {length} bytes, payload starts {start + 8}");

                ReportTheFormula(bytes, source.AkMediaInformation.InMemoryMediaSize);
            }

            // The mod's own wem, laid out the same way, so the value it should be declaring can be
            // read straight off it.
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modWem = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false).GetAllFiles()
                .First(file => file.Key.EndsWith(".wem", StringComparison.OrdinalIgnoreCase));

            var modBytes = modWem.Value.DataSource.ReadData();
            Console.WriteLine($"\n{modWem.Key}: declared 817726, wem {modBytes.Length}");
            foreach (var (tag, start, length) in ReadRiffChunks(modBytes))
                Console.WriteLine($"    {tag} at {start}, {length} bytes, payload starts {start + 8}");

            ReportTheFormula(modBytes, 817726);
        }

        /// <summary>The candidate rule - everything up to the first audio packet stays resident -
        /// checked against what the .bnk declares.</summary>
        static void ReportTheFormula(byte[] wemBytes, uint declared)
        {
            var dataChunk = ReadRiffChunks(wemBytes).FirstOrDefault(chunk => chunk.Tag == "data");
            if (dataChunk.Tag == null)
                return;

            Shared.GameFormats.Wwise.Wem.V132.WemFile wemFile;
            try
            {
                wemFile = Shared.GameFormats.Wwise.Wem.V132.WemFile.CreateFromWemBytes(wemBytes);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"    could not parse the wem: {exception.Message}");
                return;
            }

            var dataPayloadStart = (uint)dataChunk.Start + 8;
            var predicted = dataPayloadStart + wemFile.FmtChunk.FirstAudioPacketOffset;

            Console.WriteLine($"    dataPayloadStart {dataPayloadStart} + firstAudioPacketOffset {wemFile.FmtChunk.FirstAudioPacketOffset}" +
                $" = {predicted}   (declared {declared}) {(predicted == declared ? "MATCH" : "no")}");
        }

        /// <summary>The RIFF chunk table of a wem, as (tag, offset of the tag, declared length).</summary>
        static List<(string Tag, int Start, uint Length)> ReadRiffChunks(byte[] bytes)
        {
            var chunks = new List<(string, int, uint)>();
            var offset = 12;

            while (offset + 8 <= bytes.Length)
            {
                var tag = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                var length = BitConverter.ToUInt32(bytes, offset + 4);
                chunks.Add((tag, offset, length));

                if (length == 0 || offset + 8 + (long)length > bytes.Length)
                    break;

                offset += 8 + (int)length;
            }

            return chunks;
        }

        /// <summary>The meter and transition rules every vanilla sibling of the mod's node declares.
        /// A music transition is scheduled against a musical grid, so a node that declares no meter
        /// where all of its siblings do is a node the container may never manage to start.</summary>
        [Test]
        public void WhatTheVanillaSiblingsDeclareForMeterAndRules()
        {
            var hircs = new Dictionary<uint, HircItem>();
            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (File.Exists(packPath))
                    IndexBanks(hircs, VanillaBankReader.ReadMusicBanks(packPath));
            }

            foreach (var containerId in new uint[] { 698158058, 26264058 })
            {
                var container = (CAkMusicSwitchCntr_V136)hircs[containerId];
                Console.WriteLine($"\n################ children of {containerId} ################");

                foreach (var childId in container.MusicTransNodeParams.MusicNodeParams.Children.ChildIds)
                {
                    if (!hircs.TryGetValue(childId, out var child) || child is not CAkMusicRanSeqCntr_V136 ranSeq)
                        continue;

                    var musicNodeParams = ranSeq.MusicTransNodeParams.MusicNodeParams;
                    Console.WriteLine($"  RanSeq {childId}: meterFlag {musicNodeParams.MeterInfoFlag}, " +
                        $"rules {ranSeq.MusicTransNodeParams.NumRules}, " +
                        $"tempo {musicNodeParams.AkMeterInfo?.Tempo}, " +
                        $"beat {musicNodeParams.AkMeterInfo?.TimeSigBeatValue}/{musicNodeParams.AkMeterInfo?.TimeSigNumBeatsBar}, " +
                        $"gridPeriod {musicNodeParams.AkMeterInfo?.GridPeriod}, gridOffset {musicNodeParams.AkMeterInfo?.GridOffset}");
                }
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            var modHircs = new Dictionary<uint, HircItem>();
            IndexBanks(modHircs, modPack.GetAllFiles()
                .Where(file => file.Key.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)
                    && !file.Key.Contains("_for_merging", StringComparison.OrdinalIgnoreCase))
                .Select(file => (file.Key, file.Value.DataSource.ReadData()))
                .ToList());

            Console.WriteLine("\n################ the mod's ################");
            foreach (var ranSeq in modHircs.Values.OfType<CAkMusicRanSeqCntr_V136>())
            {
                var musicNodeParams = ranSeq.MusicTransNodeParams.MusicNodeParams;
                Console.WriteLine($"  RanSeq {ranSeq.Id}: meterFlag {musicNodeParams.MeterInfoFlag}, " +
                    $"rules {ranSeq.MusicTransNodeParams.NumRules}, " +
                    $"tempo {musicNodeParams.AkMeterInfo?.Tempo}, " +
                    $"beat {musicNodeParams.AkMeterInfo?.TimeSigBeatValue}/{musicNodeParams.AkMeterInfo?.TimeSigNumBeatsBar}, " +
                    $"gridPeriod {musicNodeParams.AkMeterInfo?.GridPeriod}, gridOffset {musicNodeParams.AkMeterInfo?.GridOffset}");
            }

            Console.WriteLine("\n################ the mod's segments ################");
            foreach (var segment in modHircs.Values.OfType<CAkMusicSegment_V136>())
                Console.WriteLine($"  Segment {segment.Id}: meterFlag {segment.MusicNodeParams.MeterInfoFlag}, " +
                    $"tempo {segment.MusicNodeParams.AkMeterInfo?.Tempo}, gridPeriod {segment.MusicNodeParams.AkMeterInfo?.GridPeriod}");
        }

        /// <summary>
        /// A pack whose Araby branch points at Cathay's own vanilla node instead of the mod's.
        ///
        /// Three fixes aimed at fields of the generated hierarchy have each left the branch silent,
        /// so this stops guessing at fields and splits the problem in half instead. Cathay plays, so
        /// its node is known good; if Araby playing Cathay's music is audible then everything above
        /// the node - the .dat scripts, the State, the container, the decision tree, the .bnk load
        /// order - is proven, and the fault is entirely inside the hircs the editor generates. If it
        /// is still silent then the branch is never reached at all, and every field of the generated
        /// hierarchy is beside the point.
        ///
        /// Nothing of the mod's hierarchy is removed, only the leaf's target is repointed, so the
        /// two packs differ by exactly the thing under test.
        /// </summary>
        [Test]
        public void BuildAPackWhoseArabyBranchPlaysCathaysNode()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            var arabyKey = WwiseHash.Compute("araby");
            var cathayKey = WwiseHash.Compute("cathay");

            var testingBank = modPack.GetAllFiles()
                .Single(file => file.Key.EndsWith("global_music_1_music_araby_for_testing.bnk", StringComparison.OrdinalIgnoreCase));

            var bnk = BnkFile.CreateFromBytes(testingBank.Value.DataSource.ReadData(), testingBank.Key, false);
            var hircItems = bnk.HircChunk.HircItems;

            foreach (var container in hircItems.OfType<CAkMusicSwitchCntr_V136>())
            {
                var arabyLeaves = new List<AkDecisionTree_V136.Node_V136>();
                var cathayTargets = new List<uint>();
                CollectLeavesFor(container.AkDecisionTree.DecisionTree, arabyKey, arabyLeaves);
                CollectTargetsFor(container.AkDecisionTree.DecisionTree, cathayKey, cathayTargets);

                var cathayTarget = cathayTargets.FirstOrDefault(target => target != 0);
                Console.WriteLine($"\ncontainer {container.Id}: {arabyLeaves.Count} araby leaf/leaves, cathay node {cathayTarget}");

                if (cathayTarget == 0)
                {
                    Console.WriteLine("  !! no cathay node to borrow, leaving this container alone");
                    continue;
                }

                foreach (var leaf in arabyLeaves)
                {
                    Console.WriteLine($"  repointing araby leaf from {leaf.AudioNodeId} to {cathayTarget}");
                    leaf.AudioNodeId = cathayTarget;
                }

                // A leaf only resolves to a node the container also claims, so the borrowed node has
                // to join the child list the same way the mod's own node did.
                var children = container.MusicTransNodeParams.MusicNodeParams.Children;
                var childIds = new SortedSet<uint>(children.ChildIds) { cathayTarget };
                children.ChildIds = [.. childIds];
                children.NumChilds = (uint)childIds.Count;

                container.AkDecisionTree.Nodes = AkDecisionTree_V136.FlattenDecisionTree(container.AkDecisionTree.DecisionTree);
                container.TreeDataSize = container.AkDecisionTree.GetSize();
                container.UpdateSectionSize();
            }

            var bkhdChunkBytes = Shared.GameFormats.Wwise.Bkhd.BkhdChunk.WriteData(bnk.BkhdChunk);
            var hircChunkBytes = HircChunk.WriteData(Editors.Audio.Shared.Wwise.Generators.Hirc.HircChunkGenerator.GenerateHircChunk(hircItems), bnk.BkhdChunk.AkBankHeader.BankGeneratorVersion);

            using var memStream = new MemoryStream();
            memStream.Write(bkhdChunkBytes);
            memStream.Write(hircChunkBytes);
            var rebuilt = memStream.ToArray();

            // Read it straight back, so a pack that cannot be parsed is caught here rather than by
            // being silent in game and looking like the very thing under test.
            var reparsed = BnkFile.CreateFromBytes(rebuilt, testingBank.Key, false);
            Assert.That(reparsed.HircChunk.HircItems, Has.Count.EqualTo(hircItems.Count));

            testingBank.Value.DataSource = new Shared.Core.PackFiles.Models.FileSources.MemorySource(rebuilt);

            modPack.IsReadOnly = false;
            var outputPath = Path.Combine(Path.GetDirectoryName(ModPackPath)!, "araby_music_cathay_node.pack");
            provider.GetRequiredService<IPackFileService>().SavePackContainer(modPack, outputPath, false, Shared.Core.Settings.GameInformationDatabase.GetGameById(Shared.Core.Settings.GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nWrote {outputPath}");
        }

        /// <summary>
        /// A pack where Cathay's branch points at the mod's own node, as a control on the two things
        /// the mod depends on that nothing has ever confirmed: that the testing .bnk overrides the
        /// vanilla .bnk it is named after, and that the mod's node can produce sound at all.
        ///
        /// Repointing the new branch at a node known to play was still silent, so the branch is never
        /// reached and nothing inside the .bnk can be judged through it. Cathay is reached, so this
        /// drives the mod's node through Cathay's branch instead and leaves everything else alone.
        ///
        /// Playing as Cathay separates three cases by ear, which is the point of choosing the modder's
        /// own wav as the target rather than another culture's music:
        ///   the supplied wav  - the testing .bnk overrides vanilla and the mod's node plays, so only
        ///                       the selection of the new branch is broken;
        ///   silence           - the testing .bnk overrides vanilla but the mod's node is silent, so
        ///                       the fault is in the generated hierarchy after all;
        ///   Cathay's music    - the testing .bnk never overrides vanilla, the game has been reading
        ///                       vanilla's container throughout, and no change to the generated hircs
        ///                       could ever have been audible.
        /// </summary>
        [Test]
        public void BuildAPackWhereCathayPlaysTheModsNode()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            var testingBank = modPack.GetAllFiles()
                .Single(file => file.Key.EndsWith("global_music_1_music_araby_for_testing.bnk", StringComparison.OrdinalIgnoreCase));

            var bnk = BnkFile.CreateFromBytes(testingBank.Value.DataSource.ReadData(), testingBank.Key, false);
            var hircItems = bnk.HircChunk.HircItems;

            foreach (var container in hircItems.OfType<CAkMusicSwitchCntr_V136>())
            {
                var cathayLeaves = new List<AkDecisionTree_V136.Node_V136>();
                var arabyTargets = new List<uint>();
                CollectLeavesFor(container.AkDecisionTree.DecisionTree, WwiseHash.Compute("cathay"), cathayLeaves);
                CollectTargetsFor(container.AkDecisionTree.DecisionTree, WwiseHash.Compute("araby"), arabyTargets);

                var arabyTarget = arabyTargets.FirstOrDefault(target => target != 0);
                Console.WriteLine($"\ncontainer {container.Id}: {cathayLeaves.Count} cathay leaf/leaves, mod node {arabyTarget}");

                if (arabyTarget == 0)
                {
                    Console.WriteLine("  !! no mod node to point at, leaving this container alone");
                    continue;
                }

                foreach (var leaf in cathayLeaves)
                {
                    Console.WriteLine($"  repointing cathay leaf from {leaf.AudioNodeId} to {arabyTarget}");
                    leaf.AudioNodeId = arabyTarget;
                }

                // The mod's node is already a declared child, since the araby branch names it, so
                // this only has to keep the list consistent rather than add anything.
                var children = container.MusicTransNodeParams.MusicNodeParams.Children;
                var childIds = new SortedSet<uint>(children.ChildIds) { arabyTarget };
                children.ChildIds = [.. childIds];
                children.NumChilds = (uint)childIds.Count;

                container.AkDecisionTree.Nodes = AkDecisionTree_V136.FlattenDecisionTree(container.AkDecisionTree.DecisionTree);
                container.TreeDataSize = container.AkDecisionTree.GetSize();
                container.UpdateSectionSize();
            }

            var bkhdChunkBytes = Shared.GameFormats.Wwise.Bkhd.BkhdChunk.WriteData(bnk.BkhdChunk);
            var hircChunkBytes = HircChunk.WriteData(
                Editors.Audio.Shared.Wwise.Generators.Hirc.HircChunkGenerator.GenerateHircChunk(hircItems),
                bnk.BkhdChunk.AkBankHeader.BankGeneratorVersion);

            using var memStream = new MemoryStream();
            memStream.Write(bkhdChunkBytes);
            memStream.Write(hircChunkBytes);
            var rebuilt = memStream.ToArray();

            var reparsed = BnkFile.CreateFromBytes(rebuilt, testingBank.Key, false);
            Assert.That(reparsed.HircChunk.HircItems, Has.Count.EqualTo(hircItems.Count));

            testingBank.Value.DataSource = new Shared.Core.PackFiles.Models.FileSources.MemorySource(rebuilt);

            modPack.IsReadOnly = false;
            var outputPath = Path.Combine(Path.GetDirectoryName(ModPackPath)!, "araby_music_cathay_plays_mod_node.pack");
            provider.GetRequiredService<IPackFileService>().SavePackContainer(modPack, outputPath, false,
                Shared.Core.Settings.GameInformationDatabase.GetGameById(Shared.Core.Settings.GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nWrote {outputPath}");
        }

        /// <summary>
        /// A testing .bnk holding nothing but vanilla's two containers, with Cathay's branches taken
        /// out, to answer one question on its own: does a testing .bnk override the vanilla .bnk it
        /// is named after?
        ///
        /// The previous pack conflated two causes. Hearing Cathay's usual music through it could mean
        /// the override never happens, or it could mean the override happens and Wwise rejects the
        /// .bnk over something in the mod's own hircs and falls back to vanilla. Both look identical
        /// from the sofa, so this removes every generated hirc from the .bnk and edits nothing but
        /// vanilla's decision trees.
        ///
        /// Cathay going silent means a testing .bnk does override vanilla, and the fault is in what
        /// the mod puts inside one. Cathay still playing means the override itself never happens, and
        /// the whole testing .bnk scheme does not work for music.
        ///
        /// The campaign container's default branch goes too. Removing only Cathay's would drop it
        /// through to the default and play something, which is not distinguishable by ear from the
        /// override having failed.
        /// </summary>
        [Test]
        public void BuildAPackThatSilencesCathayWithVanillaContentOnly()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            var testingBank = modPack.GetAllFiles()
                .Single(file => file.Key.EndsWith("global_music_1_music_araby_for_testing.bnk", StringComparison.OrdinalIgnoreCase));

            var bnk = BnkFile.CreateFromBytes(testingBank.Value.DataSource.ReadData(), testingBank.Key, false);

            // Vanilla's containers alone. Every generated hirc is dropped so that nothing the editor
            // produces can be blamed for the .bnk failing to load.
            var containers = bnk.HircChunk.HircItems.OfType<CAkMusicSwitchCntr_V136>().Cast<HircItem>().ToList();
            Console.WriteLine($"keeping {containers.Count} container(s), dropping {bnk.HircChunk.HircItems.Count - containers.Count} generated hirc(s)");

            foreach (var container in containers.Cast<CAkMusicSwitchCntr_V136>())
            {
                var removed = RemoveBranches(container.AkDecisionTree.DecisionTree,
                    [WwiseHash.Compute("cathay"), WwiseHash.Compute("araby"), DefaultKey]);

                // The child list is rebuilt from what the tree still names, so it stays consistent
                // with the trimmed tree rather than keeping ids nothing points at any more.
                var leaves = new List<uint>();
                CollectLeaves(container.AkDecisionTree.DecisionTree, leaves);
                var childIds = new SortedSet<uint>(leaves.Where(id => id != 0));

                container.MusicTransNodeParams.MusicNodeParams.Children = new Children_V136
                {
                    NumChilds = (uint)childIds.Count,
                    ChildIds = [.. childIds]
                };

                container.AkDecisionTree.Nodes = AkDecisionTree_V136.FlattenDecisionTree(container.AkDecisionTree.DecisionTree);
                container.TreeDataSize = container.AkDecisionTree.GetSize();
                container.UpdateSectionSize();

                Console.WriteLine($"  container {container.Id}: removed {removed} branch(es), " +
                    $"{container.AkDecisionTree.DecisionTree.Nodes.Count} top level keys left, {childIds.Count} children");
            }

            var bkhdChunkBytes = Shared.GameFormats.Wwise.Bkhd.BkhdChunk.WriteData(bnk.BkhdChunk);
            var hircChunkBytes = HircChunk.WriteData(
                Editors.Audio.Shared.Wwise.Generators.Hirc.HircChunkGenerator.GenerateHircChunk(containers),
                bnk.BkhdChunk.AkBankHeader.BankGeneratorVersion);

            using var memStream = new MemoryStream();
            memStream.Write(bkhdChunkBytes);
            memStream.Write(hircChunkBytes);
            var rebuilt = memStream.ToArray();

            var reparsed = BnkFile.CreateFromBytes(rebuilt, testingBank.Key, false);
            Assert.That(reparsed.HircChunk.HircItems, Has.Count.EqualTo(containers.Count));

            testingBank.Value.DataSource = new Shared.Core.PackFiles.Models.FileSources.MemorySource(rebuilt);

            modPack.IsReadOnly = false;
            var outputPath = Path.Combine(Path.GetDirectoryName(ModPackPath)!, "araby_music_cathay_silenced.pack");
            provider.GetRequiredService<IPackFileService>().SavePackContainer(modPack, outputPath, false,
                Shared.Core.Settings.GameInformationDatabase.GetGameById(Shared.Core.Settings.GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nWrote {outputPath}");
        }

        /// <summary>
        /// A pack that replaces vanilla's global_music__core.bnk outright, rather than adding a .bnk
        /// alongside it and relying on Wwise to prefer the newcomer.
        ///
        /// A .bnk holding nothing but vanilla's containers with Cathay's branches removed changed
        /// nothing in game, so a testing .bnk does not override the vanilla .bnk it is named after -
        /// whatever the load order does, the definitions already resident win. Overriding a vanilla
        /// file in Total War is done by shipping a file at the same path, so this does that: vanilla's
        /// whole .bnk, with the two containers swapped for the merged ones and the mod's hierarchy
        /// added, written back over audio\wwise\global_music__core.bnk.
        ///
        /// Everything vanilla had is carried across. Shipping only the containers under that name
        /// would delete the other few thousand hircs the .bnk holds and take the rest of the game's
        /// music with them.
        /// </summary>
        [Test]
        public void BuildAPackThatReplacesTheVanillaMusicBankOutright()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            var testingBank = modPack.GetAllFiles()
                .Single(file => file.Key.EndsWith("global_music_1_music_araby_for_testing.bnk", StringComparison.OrdinalIgnoreCase));

            var modBnk = BnkFile.CreateFromBytes(testingBank.Value.DataSource.ReadData(), testingBank.Key, false);
            var mergedContainers = modBnk.HircChunk.HircItems.OfType<CAkMusicSwitchCntr_V136>().ToDictionary(container => container.Id);
            var generatedHircs = modBnk.HircChunk.HircItems.Where(hirc => hirc is not CAkMusicSwitchCntr_V136).ToList();

            Console.WriteLine($"merged containers: {string.Join(", ", mergedContainers.Keys)}");
            Console.WriteLine($"generated hircs: {string.Join(", ", generatedHircs.Select(hirc => $"{hirc.HircType} {hirc.Id}"))}");

            var vanilla = VanillaBankReader.ReadMusicBanks(Path.Combine(GameDirectory, "data", "audio_base_bnk.pack"))
                .First(bank => bank.Path.EndsWith("global_music__core.bnk", StringComparison.OrdinalIgnoreCase));

            var rebuilt = SpliceHircs(vanilla.Bytes, mergedContainers, generatedHircs);

            var reparsed = BnkFile.CreateFromBytes(rebuilt, vanilla.Path, false);
            var reparsedContainers = reparsed.HircChunk.HircItems.OfType<CAkMusicSwitchCntr_V136>().ToDictionary(x => x.Id);

            Assert.Multiple(() =>
            {
                foreach (var (containerId, merged) in mergedContainers)
                {
                    Assert.That(reparsedContainers.ContainsKey(containerId), Is.True, $"container {containerId} is missing");
                    Assert.That(reparsedContainers[containerId].AkDecisionTree.DecisionTree.Nodes.Count,
                        Is.EqualTo(merged.AkDecisionTree.DecisionTree.Nodes.Count),
                        $"container {containerId} did not survive the splice with its tree intact");
                }

                foreach (var generated in generatedHircs)
                    Assert.That(reparsed.HircChunk.HircItems.Any(hirc => hirc.Id == generated.Id), Is.True,
                        $"generated hirc {generated.Id} is missing");
            });

            Console.WriteLine($"rebuilt {rebuilt.Length} bytes (vanilla was {vanilla.Bytes.Length}), " +
                $"reparsed {reparsed.HircChunk.HircItems.Count} hircs");

            modPack.IsReadOnly = false;
            var packFileService = provider.GetRequiredService<IPackFileService>();
            packFileService.AddFilesToPack(modPack,
            [
                new NewPackFileEntry("audio\\wwise",
                    new Shared.Core.PackFiles.Models.PackFile("global_music__core.bnk",
                        new Shared.Core.PackFiles.Models.FileSources.MemorySource(rebuilt)))
            ]);

            var outputPath = Path.Combine(Path.GetDirectoryName(ModPackPath)!, "araby_music_replacing_vanilla_bank.pack");
            packFileService.SavePackContainer(modPack, outputPath, false,
                Shared.Core.Settings.GameInformationDatabase.GetGameById(Shared.Core.Settings.GameTypeEnum.Warhammer3));

            Console.WriteLine($"\nWrote {outputPath}");
        }

        /// <summary>
        /// Vanilla's .bnk bytes with some hircs swapped and others appended, spliced rather than
        /// rebuilt. Re-serialising the whole file would put every one of its five thousand hircs
        /// through writers that have only ever been asked to write generated ones, and a single type
        /// that does not round trip byte for byte misaligns everything after it. Only the hircs
        /// actually being changed are written, so vanilla's bytes are carried across untouched.
        /// </summary>
        static byte[] SpliceHircs(byte[] vanillaBytes, Dictionary<uint, CAkMusicSwitchCntr_V136> replacements, List<HircItem> additions)
        {
            // The HIRC chunk, found by walking the top level chunk table.
            var offset = 0;
            var hircChunkStart = -1;
            uint hircChunkSize = 0;

            while (offset + 8 <= vanillaBytes.Length)
            {
                var tag = System.Text.Encoding.ASCII.GetString(vanillaBytes, offset, 4);
                var length = BitConverter.ToUInt32(vanillaBytes, offset + 4);

                if (tag == "HIRC")
                {
                    hircChunkStart = offset;
                    hircChunkSize = length;
                    break;
                }

                offset += 8 + (int)length;
            }

            if (hircChunkStart == -1)
                throw new InvalidDataException("no HIRC chunk");

            var itemCount = BitConverter.ToUInt32(vanillaBytes, hircChunkStart + 8);
            var cursor = hircChunkStart + 12;

            using var body = new MemoryStream();
            var replaced = 0;

            for (uint index = 0; index < itemCount; index++)
            {
                var sectionSize = BitConverter.ToUInt32(vanillaBytes, cursor + 1);
                var id = BitConverter.ToUInt32(vanillaBytes, cursor + 5);
                var totalLength = 5 + (int)sectionSize;

                if (replacements.TryGetValue(id, out var replacement))
                {
                    replacement.UpdateSectionSize();
                    body.Write(replacement.WriteData());
                    replaced++;
                }
                else
                    body.Write(vanillaBytes, cursor, totalLength);

                cursor += totalLength;
            }

            if (replaced != replacements.Count)
                throw new InvalidDataException($"only {replaced} of {replacements.Count} hircs to replace were found");

            foreach (var addition in additions)
            {
                addition.UpdateSectionSize();
                body.Write(addition.WriteData());
            }

            var bodyBytes = body.ToArray();

            using var output = new MemoryStream();
            output.Write(vanillaBytes, 0, hircChunkStart);
            output.Write(System.Text.Encoding.ASCII.GetBytes("HIRC"));
            output.Write(BitConverter.GetBytes((uint)(bodyBytes.Length + 4)));
            output.Write(BitConverter.GetBytes(itemCount + (uint)additions.Count));
            output.Write(bodyBytes);

            // Whatever followed the HIRC chunk in the original file.
            var afterHirc = hircChunkStart + 8 + (int)hircChunkSize;
            if (afterHirc < vanillaBytes.Length)
                output.Write(vanillaBytes, afterHirc, vanillaBytes.Length - afterHirc);

            return output.ToArray();
        }

        /// <summary>Drops every branch keyed on one of the given keys, at whatever depth it sits.</summary>
        static int RemoveBranches(AkDecisionTree_V136.Node_V136 node, uint[] keys)
        {
            var removed = node.Nodes.RemoveAll(child => keys.Contains(child.Key));
            foreach (var child in node.Nodes)
                removed += RemoveBranches(child, keys);

            return removed;
        }

        const uint DefaultKey = 0;

        static void CollectLeavesFor(AkDecisionTree_V136.Node_V136 node, uint key, List<AkDecisionTree_V136.Node_V136> found, bool matched = false)
        {
            foreach (var child in node.Nodes)
            {
                var childMatched = matched || child.Key == key;

                if (child.Nodes.Count == 0)
                {
                    if (childMatched)
                        found.Add(child);
                }
                else
                    CollectLeavesFor(child, key, found, childMatched);
            }
        }

        static void CollectTargetsFor(AkDecisionTree_V136.Node_V136 node, uint key, List<uint> found, bool matched = false)
        {
            foreach (var child in node.Nodes)
            {
                var childMatched = matched || child.Key == key;

                if (child.Nodes.Count == 0)
                {
                    if (childMatched)
                        found.Add(child.AudioNodeId);
                }
                else
                    CollectTargetsFor(child, key, found, childMatched);
            }
        }

        /// <summary>
        /// Where a State is declared in vanilla, and whether the mod declares its own the same way.
        ///
        /// Pointing the new branch at a node known to play left it silent, so the branch is never
        /// reached and the State is never becoming current. A State Group's members are declared
        /// outside the decision tree - in the init bank's STMG tables and as State hircs - and the
        /// editor writes neither, so a State the game has never been told about is the candidate.
        /// </summary>
        [Test]
        public void WhereAStateIsDeclaredInVanilla()
        {
            uint[] cultureStates =
            [
                WwiseHash.Compute("Cathay"), WwiseHash.Compute("Empire"), WwiseHash.Compute("Araby"),
            ];

            uint[] stateGroups =
            [
                WwiseHash.Compute("Battle_Music_WH3_Culture"), WwiseHash.Compute("WH3_Campaign_Subcultures"),
            ];

            Console.WriteLine("states:  " + string.Join(", ", new[] { "Cathay", "Empire", "Araby" }
                .Select(name => $"{name}={WwiseHash.Compute(name)}")));
            Console.WriteLine("groups:  " + string.Join(", ", new[] { "Battle_Music_WH3_Culture", "WH3_Campaign_Subcultures" }
                .Select(name => $"{name}={WwiseHash.Compute(name)}")));

            foreach (var packName in new[] { "audio_base_bnk.pack", "audio_base.pack", "audio_base_m.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                foreach (var (path, bytes) in VanillaBankReader.ReadMusicBanks(packPath))
                {
                    var chunkTags = ReadChunkTags(bytes);
                    var hasStmg = chunkTags.Any(tag => tag.StartsWith("STMG"));

                    List<HircItem> hircItems;
                    try
                    {
                        hircItems = BnkFile.CreateFromBytes(bytes, path, false).HircChunk?.HircItems ?? [];
                    }
                    catch
                    {
                        hircItems = [];
                    }

                    var stateHircs = hircItems.Where(hirc => hirc.HircType == Shared.GameFormats.Wwise.Enums.AkBkHircType.State).ToList();
                    var namedStates = stateHircs.Where(hirc => cultureStates.Contains(hirc.Id)).Select(hirc => hirc.Id).ToList();

                    if (!hasStmg && stateHircs.Count == 0)
                        continue;

                    Console.WriteLine($"\n{packName}  {path}");
                    Console.WriteLine($"  chunks: {string.Join(", ", chunkTags)}");
                    Console.WriteLine($"  State hircs: {stateHircs.Count}" +
                        (namedStates.Count == 0 ? "" : $"   including culture states {string.Join(", ", namedStates)}"));

                    if (hasStmg)
                        ReportStmgGroups(bytes, stateGroups, cultureStates);
                }
            }

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            Console.WriteLine("\n################ the mod ################");

            foreach (var file in modPack.GetAllFiles().Where(file => file.Key.EndsWith(".bnk")).OrderBy(file => file.Key))
            {
                var bytes = file.Value.DataSource.ReadData();
                var hircItems = BnkFile.CreateFromBytes(bytes, file.Key, false).HircChunk?.HircItems ?? [];
                Console.WriteLine($"  {file.Key}: chunks {string.Join(", ", ReadChunkTags(bytes))}, " +
                    $"State hircs {hircItems.Count(hirc => hirc.HircType == Shared.GameFormats.Wwise.Enums.AkBkHircType.State)}");
            }
        }

        /// <summary>
        /// The State Group table inside an STMG chunk, walked far enough to list the groups and the
        /// states each one declares. Read by hand because nothing in the codebase parses STMG - which
        /// is the point of the check.
        /// </summary>
        static void ReportStmgGroups(byte[] bytes, uint[] wantedGroups, uint[] wantedStates)
        {
            var offset = 0;
            var stmgStart = -1;
            uint stmgLength = 0;

            while (offset + 8 <= bytes.Length)
            {
                var tag = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                var length = BitConverter.ToUInt32(bytes, offset + 4);

                if (tag == "STMG")
                {
                    stmgStart = offset + 8;
                    stmgLength = length;
                    break;
                }

                if (length == 0 || offset + 8 + length > (uint)bytes.Length)
                    break;

                offset += 8 + (int)length;
            }

            if (stmgStart == -1)
                return;

            // Every id the chunk contains, so a group or state can be looked for without having to
            // model the whole layout correctly.
            var idsInStmg = new HashSet<uint>();
            for (var index = stmgStart; index + 4 <= stmgStart + (int)stmgLength; index += 4)
                idsInStmg.Add(BitConverter.ToUInt32(bytes, index));

            Console.WriteLine($"  STMG is {stmgLength} bytes");
            foreach (var group in wantedGroups)
                Console.WriteLine($"    state group {group}: {(idsInStmg.Contains(group) ? "PRESENT" : "absent")}");
            foreach (var state in wantedStates)
                Console.WriteLine($"    state {state}: {(idsInStmg.Contains(state) ? "PRESENT" : "absent")}");
        }

        /// <summary>
        /// What the mod's event data .dat says about the State Groups, next to what vanilla's own
        /// event data .dats say. If vanilla enumerates the members of a music State Group anywhere,
        /// that is a list the game reads and a new State has to join.
        /// </summary>
        [Test]
        public void WhatTheEventDataDatSaysAboutStates()
        {
            string[] musicStateGroups =
            [
                "Battle_Music_WH3_Culture", "WH3_Campaign_Subcultures",
                "WH3_Campaign_Music_AMS_Fragments_Faction",
            ];

            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var modPack = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);

            var modDat = modPack.GetAllFiles()
                .Single(file => file.Key.EndsWith("event_data__music_araby.dat", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"################ {modDat.Key} ################");
            ReportDat(Shared.GameFormats.Dat.DatFileParser.Parse(modDat.Value, false));

            // Every vanilla event data .dat, so the group memberships the game ships with can be
            // read rather than assumed from the editor's comments.
            foreach (var packName in new[] { "audio_base.pack", "audio_base_bnk.pack" })
            {
                var packPath = Path.Combine(GameDirectory, "data", packName);
                if (!File.Exists(packPath))
                    continue;

                using var vanillaProvider = VanillaBankReader.CreateProvider(GameDirectory);
                var pack = VanillaBankReader.OpenPack(vanillaProvider, packPath);

                foreach (var file in pack.GetAllFiles()
                    .Where(file => file.Key.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                        && file.Key.Contains("event_data", StringComparison.OrdinalIgnoreCase)))
                {
                    Shared.GameFormats.Dat.SoundDatFile parsed;
                    try
                    {
                        parsed = Shared.GameFormats.Dat.DatFileParser.Parse(file.Value, false);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"\n{packName} {file.Key}: could not parse - {exception.Message}");
                        continue;
                    }

                    var relevant = parsed.StateGroupsWithStates0.Concat(parsed.StateGroupsWithStates1)
                        .Where(group => musicStateGroups.Contains(group.StateGroup, StringComparer.OrdinalIgnoreCase))
                        .ToList();

                    if (relevant.Count == 0)
                        continue;

                    Console.WriteLine($"\n################ {packName} {file.Key} ################");
                    foreach (var group in relevant)
                        Console.WriteLine($"  {group.StateGroup}: {group.States.Count} states -> {string.Join(", ", group.States)}");
                }
            }
        }

        static void ReportDat(Shared.GameFormats.Dat.SoundDatFile dat)
        {
            Console.WriteLine($"  EventWithStateGroup: {dat.EventWithStateGroup.Count}");
            foreach (var item in dat.EventWithStateGroup)
                Console.WriteLine($"    {item.Event} = {item.Value}");

            Console.WriteLine($"  StateGroupsWithStates0: {dat.StateGroupsWithStates0.Count}");
            foreach (var item in dat.StateGroupsWithStates0)
                Console.WriteLine($"    {item.StateGroup}: [{string.Join(", ", item.States)}]");

            Console.WriteLine($"  StateGroupsWithStates1: {dat.StateGroupsWithStates1.Count}");
            foreach (var item in dat.StateGroupsWithStates1)
                Console.WriteLine($"    {item.StateGroup}: [{string.Join(", ", item.States)}]");

            Console.WriteLine($"  DialogueEventsWithStateGroups: {dat.DialogueEventsWithStateGroups.Count}");
            Console.WriteLine($"  SettingValues: {dat.SettingValues.Count}");
        }

        /// <summary>What the patched scripts actually say, next to what vanilla says, so the match
        /// key the game compares against is read rather than assumed.</summary>
        [Test]
        public void WhatTheScriptsMatchOn()
        {
            using var provider = VanillaBankReader.CreateProvider(GameDirectory);
            var container = VanillaBankReader.OpenPack(provider, ModPackPath, markAsCa: false);
            var files = container.GetAllFiles();

            foreach (var name in new[] { "battle_music.dat", "campaign_music.dat" })
            {
                var packFile = files.Single(file => file.Key.EndsWith(name, StringComparison.OrdinalIgnoreCase)).Value;
                Console.WriteLine($"\n================ {name} ================");

                var file = MusicDatParser.Parse(packFile);
                foreach (var slot in MusicDatCultureWiring.FindSlots(file))
                {
                    Console.WriteLine($"\n-- slot: {slot.Title}");
                    foreach (var chain in slot.Chains)
                    {
                        var cases = chain.Cases
                            .Select(c => $"{c.MatchKey} => {c.SoundEvent ?? $"<culture:{c.MusicalCulture}>"}")
                            .ToList();
                        Console.WriteLine("   " + string.Join("\n   ", cases));
                        break;
                    }
                }
            }
        }
    }
}
