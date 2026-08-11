using System.Text;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Test.Audio
{
    // Round-trip harness for the Wwise hirc parsers, run against the shipped WH3 banks.
    //
    // The point is that parsing "succeeding" proves very little here: HircItem.ReadHirc
    // force-seeks to the declared end of each item after ReadData returns, so a parser that
    // reads too few bytes is silently resynced instead of failing. The only way to know a
    // parser actually understands an item is to write it back and compare against the bytes
    // it came from - which is also exactly the bar that has to be met before any of these
    // types can be generated rather than only inspected.
    //
    // Reference for field layouts when a mismatch needs diagnosing: https://github.com/bnnm/wwiser
    [Explicit("Research probe, needs a WH3 install, run manually")]
    internal class MusicHircRoundTripResearch
    {
        const string BnkPack = @"D:\SteamLibrary\steamapps\common\Total War WARHAMMER III\data\audio_base_bnk.pack";

        static readonly AkBkHircType[] MusicTypes =
        [
            AkBkHircType.Music_Segment,
            AkBkHircType.Music_Track,
            AkBkHircType.Music_Switch,
            AkBkHircType.Music_Random_Sequence,
        ];

        [Test]
        public void ProbeHircRoundTrip()
        {
            var banks = VanillaBankReader.ReadMusicBanks(BnkPack);
            Assert.That(banks, Is.Not.Empty, "no banks were extracted");

            var stats = new Dictionary<AkBkHircType, Tally>();
            var firstFailureByType = new Dictionary<AkBkHircType, string>();
            // Actions are the one type where the failures are spread across a discriminated union,
            // so knowing which AkActionType values still fail is more useful than a single example.
            var failingActionTypes = new Dictionary<AkActionType, int>();

            foreach (var (bankPath, bytes) in banks)
            {
                BnkFile bnk;
                try
                {
                    bnk = BnkFile.CreateFromBytes(bytes, bankPath, true);
                }
                catch (Exception e)
                {
                    TestContext.Out.WriteLine($"!! {bankPath}: bank parse failed: {e.Message}");
                    continue;
                }

                if (bnk.HircChunk == null)
                    continue;

                foreach (var hirc in bnk.HircChunk.HircItems)
                {
                    var type = hirc.HircType;
                    var entry = stats.TryGetValue(type, out var e0) ? e0 : new Tally();
                    entry.Total++;

                    var start = (int)hirc.ByteIndexInFile;
                    var length = (int)(HircHeader.PrefixSize + hirc.SectionSize);
                    if (start + length > bytes.Length)
                    {
                        entry.Mismatch++;
                        stats[type] = entry;
                        continue;
                    }

                    var original = bytes.AsSpan(start, length).ToArray();

                    try
                    {
                        var written = hirc.WriteData();
                        if (written.AsSpan().SequenceEqual(original))
                        {
                            // WriteData re-emits the SectionSize it parsed, so matching bytes say
                            // nothing about whether the size can be *computed*. Generation has no
                            // parsed value to fall back on, so check the arithmetic separately.
                            var parsedSectionSize = hirc.SectionSize;
                            try
                            {
                                hirc.UpdateSectionSize();
                                if (hirc.SectionSize == parsedSectionSize)
                                {
                                    entry.Ok++;
                                }
                                else
                                {
                                    entry.BadSize++;
                                    if (!firstFailureByType.ContainsKey(type))
                                        firstFailureByType[type] =
                                            $"{bankPath} id={hirc.Id}: UpdateSectionSize gave {hirc.SectionSize}, parsed {parsedSectionSize}";
                                }
                            }
                            catch (Exception e)
                            {
                                entry.BadSize++;
                                if (!firstFailureByType.ContainsKey(type))
                                    firstFailureByType[type] = $"{bankPath} id={hirc.Id}: UpdateSectionSize threw {e.GetType().Name}: {e.Message}";
                            }
                            finally
                            {
                                hirc.SectionSize = parsedSectionSize;
                            }
                        }
                        else
                        {
                            entry.Mismatch++;
                            RecordActionType(failingActionTypes, hirc);
                            if (!firstFailureByType.ContainsKey(type))
                                firstFailureByType[type] = Describe(bankPath, hirc, original, written);
                        }
                    }
                    catch (Exception e)
                    {
                        entry.Threw++;
                        RecordActionType(failingActionTypes, hirc);
                        if (!firstFailureByType.ContainsKey(type))
                            firstFailureByType[type] = $"{bankPath} id={hirc.Id}: {e.GetType().Name}: {e.Message}";
                    }

                    stats[type] = entry;
                }
            }

            TestContext.Out.WriteLine($"=== round-trip over {banks.Count} banks ===");
            foreach (var kvp in stats.OrderByDescending(x => x.Value.Total))
            {
                var v = kvp.Value;
                var marker = MusicTypes.Contains(kvp.Key) ? " <-- music" : "";
                TestContext.Out.WriteLine($"  {kvp.Key,-24} total={v.Total,-6} ok={v.Ok,-6} threw={v.Threw,-6} mismatch={v.Mismatch,-6} badSize={v.BadSize,-6}{marker}");
            }

            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("=== first failure per type ===");
            foreach (var kvp in firstFailureByType.OrderBy(x => x.Key.ToString()))
                TestContext.Out.WriteLine($"  {kvp.Key}: {kvp.Value}");

            if (failingActionTypes.Count != 0)
            {
                TestContext.Out.WriteLine();
                TestContext.Out.WriteLine("=== failing action types ===");
                foreach (var kvp in failingActionTypes.OrderByDescending(x => x.Value))
                    TestContext.Out.WriteLine($"  {kvp.Key,-24} {kvp.Value}");
            }
        }

        static void RecordActionType(Dictionary<AkActionType, int> failingActionTypes, HircItem hirc)
        {
            if (hirc is not ICAkAction action)
                return;

            var actionType = action.GetActionType();
            failingActionTypes[actionType] = failingActionTypes.TryGetValue(actionType, out var count) ? count + 1 : 1;
        }

        // Answers the question the whole music feature turns on: when an Action Event sets a State,
        // what actually decides which music plays? Dumps the Music Switch containers and the State
        // Groups their decision trees branch on, plus how many branches each has.
        [Test]
        public void ProbeMusicSwitchContainerArguments()
        {
            var banks = VanillaBankReader.ReadMusicBanks(BnkPack)
                .Where(bank => bank.Path.Contains("music", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var (bankPath, bytes) in banks)
            {
                var bnk = BnkFile.CreateFromBytes(bytes, bankPath, true);
                if (bnk.HircChunk == null)
                    continue;

                var switchContainers = bnk.HircChunk.HircItems
                    .OfType<Shared.GameFormats.Wwise.Hirc.V136.CAkMusicSwitchCntr_V136>()
                    .ToList();

                if (switchContainers.Count == 0)
                    continue;

                TestContext.Out.WriteLine($"=== {bankPath} ===");
                foreach (var container in switchContainers)
                {
                    var arguments = string.Join(", ", container.Arguments.Select(argument => $"{argument.GroupType}:{argument.GroupId}"));
                    TestContext.Out.WriteLine(
                        $"  MusicSwitch id={container.Id} depth={container.TreeDepth} args=[{arguments}] " +
                        $"treeNodes={container.AkDecisionTree.Nodes.Count} rootChildren={container.AkDecisionTree.DecisionTree.Nodes.Count}");
                }
            }
        }

        sealed class Tally
        {
            public int Total;
            public int Ok;
            public int Threw;
            public int Mismatch;
            public int BadSize;
        }

        static string Describe(string bankPath, HircItem hirc, byte[] original, byte[] written)
        {
            var divergeAt = -1;
            var shared = Math.Min(original.Length, written.Length);
            for (var i = 0; i < shared; i++)
            {
                if (original[i] != written[i])
                {
                    divergeAt = i;
                    break;
                }
            }
            if (divergeAt < 0 && original.Length != written.Length)
                divergeAt = shared;

            var sb = new StringBuilder();
            sb.Append($"{bankPath} id={hirc.Id} origLen={original.Length} writtenLen={written.Length} divergeAt={divergeAt}");
            if (divergeAt >= 0)
            {
                sb.Append(" orig=").Append(Hex(original, divergeAt));
                sb.Append(" written=").Append(Hex(written, divergeAt));
            }
            return sb.ToString();
        }

        static string Hex(byte[] data, int from) =>
            Convert.ToHexString(data.AsSpan(from, Math.Min(12, Math.Max(0, data.Length - from))));
    }
}
