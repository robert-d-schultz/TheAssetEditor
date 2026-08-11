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
