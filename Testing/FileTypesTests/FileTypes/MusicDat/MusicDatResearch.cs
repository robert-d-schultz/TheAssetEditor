using Shared.GameFormats.MusicDat;
using Test.TestingUtility.TestUtility;

namespace FileTypesTests.FileTypes.MusicDat
{
    // Scratch/research probes - not assertions about behaviour, just structured dumps used
    // while working out what still needs relocating when instructions are inserted.
    [Explicit("Research probes, run manually")]
    public class MusicDatResearch
    {
        static MusicDatFile Load(string name) => MusicDatParser.Parse(PathHelper.GetFileAsBytes($@"Music\{name}"));

        [TestCase("battle_music.dat")]
        [TestCase("campaign_music.dat")]
        public void ProbeAddCulturePreview(string name)
        {
            var file = Load(name);
            var key = name.StartsWith("battle", StringComparison.Ordinal) ? "mod_testculture" : "mod_dlc99_sc_tst_testculture";

            TestContext.Out.WriteLine($"=== {name} ===");
            foreach (var slot in MusicDatCultureWiring.FindSlots(file))
            {
                TestContext.Out.WriteLine($"--- {slot.Title}: {slot.EventFor("testculture")} " +
                    $"({slot.InsertionCount} insertion(s), name {(slot.EventNameIsNegotiable ? "negotiable" : "fixed")}) ---");
                TestContext.Out.WriteLine($"    {slot.Description}");
                TestContext.Out.WriteLine($"    skipping: {slot.SkipConsequence}");
                TestContext.Out.WriteLine(MusicDatCultureWiring.PreviewSlot(file, slot, key, "testculture", null));
            }
        }

        // Which triggers / string variables are never written by the script? Those are the
        // ones the game engine supplies (the "Feedback_" naming convention), i.e. the real
        // boundary between the script and the game database.
        [TestCase("battle_music.dat")]
        [TestCase("campaign_music.dat")]
        public void ProbeEngineSuppliedSymbols(string name)
        {
            var f = Load(name);
            var all = MusicDatDisassembler.Disassemble(f).SelectMany(fn => fn.Instructions).ToList();

            var storedTriggers = all.Where(i => i.Opcode == 12).Select(i => (int)i.Operands[0]).ToHashSet();
            var storedStrVars = all.Where(i => i.Opcode == 42).Select(i => (int)i.Operands[0]).ToHashSet();
            var storedVars = all.Where(i => i.Opcode == 26).Select(i => (int)i.Operands[0]).ToHashSet();

            TestContext.Out.WriteLine($"=== {name} ===");
            TestContext.Out.WriteLine("read-only TRIGGERS (engine supplied): " + string.Join(", ",
                f.Triggers.Select((t, i) => (t, i)).Where(x => !storedTriggers.Contains(x.i)).Select(x => x.t.Name)));
            TestContext.Out.WriteLine("read-only STRING VARS (engine supplied): " + string.Join(", ",
                f.StringVariables.Select((t, i) => (t, i)).Where(x => !storedStrVars.Contains(x.i)).Select(x => x.t.Name)));
            TestContext.Out.WriteLine("read-only FLOAT VARS (engine supplied): " + string.Join(", ",
                f.Variables.Select((t, i) => (t, i)).Where(x => !storedVars.Contains(x.i)).Select(x => x.t.Name)));
        }

        // What Wwise-facing intrinsics exist besides PostSoundEvent, and how many arguments
        // each call site binds - looking for anything that sets a Switch/State/RTPC (which
        // would let one event vary its content based on something decided at runtime, like
        // the enemy's culture) as opposed to only ever posting a bare, culture-named event.
        [TestCase("battle_music.dat")]
        [TestCase("campaign_music.dat")]
        public void ProbeWwiseIntrinsics(string name)
        {
            var file = Load(name);
            var all = MusicDatDisassembler.Disassemble(file).SelectMany(fn => fn.Instructions).ToList();

            TestContext.Out.WriteLine($"=== {name}: intrinsics ===");
            foreach (var (intrinsic, i) in file.Intrinsics.Select((x, i) => (x, i)))
                TestContext.Out.WriteLine($"  [{i}] {intrinsic.Name} ({intrinsic.ParameterCount} params)");

            TestContext.Out.WriteLine($"=== {name}: call sites per intrinsic ===");
            var calls = all.Where(i => i.Opcode == 2).ToList();
            foreach (var group in calls.GroupBy(i => i.Operands[0]))
            {
                var idx = (int)group.Key;
                var iname = idx < file.Intrinsics.Count ? file.Intrinsics[idx].Name : $"intrinsic{idx}";
                TestContext.Out.WriteLine($"  {iname}: {group.Count()} site(s)");
            }
        }

        // Which opcodes each file actually uses, and how often. Both shipped files are fully
        // named now, so this is for pointing at a different build (a DLC update, another
        // Total War title on the same format): an opcode listed here that has no entry in
        // MusicDatDisassembler.Mnemonics is new, and one whose sites cluster oddly - always
        // right after the same instruction, or leaving slots that nothing else reads - is
        // worth checking for a wrong operand count before trusting its decode. That was
        // exactly how FLOAT_TO_STRING and STRING_TO_INT turned out to be mis-decoded.
        [TestCase("battle_music.dat")]
        [TestCase("campaign_music.dat")]
        public void ProbeOpcodeFrequency(string name)
        {
            var file = Load(name);
            var all = MusicDatDisassembler.Disassemble(file).SelectMany(fn => fn.Instructions).ToList();

            TestContext.Out.WriteLine($"=== {name}: {all.Count} instructions ===");
            foreach (var group in all.GroupBy(i => i.Opcode).OrderBy(g => g.Key))
                TestContext.Out.WriteLine($"  op{group.Key,-3} {group.First().Mnemonic,-20} x{group.Count()}");
        }
    }
}
