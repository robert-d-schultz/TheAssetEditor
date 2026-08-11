using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise.Generators;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    // A music Action Event only sets a State, so a mod's music is unreachable until a branch for
    // that State exists in the vanilla Music Switch container. These cover the merge that adds it.
    internal class MusicSwitchContainerMergeTests
    {
        const uint SwitchContainerId = 698158058;
        const uint NewRanSeqId = 5000;

        [Test]
        public void ANewStateIsAddedAsABranchPointingAtItsRandomSequence()
        {
            var merged = new MusicSwitchContainerMergeService()
                .MergeBranches(CreateVanillaContainer(), [new MusicBranch("Araby", NewRanSeqId)]);

            var branch = merged.AkDecisionTree.DecisionTree.Nodes
                .Single(node => node.Key == WwiseHash.Compute("Araby"));

            Assert.Multiple(() =>
            {
                Assert.That(branch.AudioNodeId, Is.EqualTo(NewRanSeqId));
                Assert.That(branch.Weight, Is.EqualTo(Wh3MusicHierarchyInformation.BranchWeight));
                Assert.That(branch.Probability, Is.EqualTo(Wh3MusicHierarchyInformation.BranchProbability));
            });
        }

        [Test]
        public void TheVanillaBranchesAreKept()
        {
            var merged = new MusicSwitchContainerMergeService()
                .MergeBranches(CreateVanillaContainer(), [new MusicBranch("Araby", NewRanSeqId)]);

            var keys = merged.AkDecisionTree.DecisionTree.Nodes.Select(node => node.Key).ToList();

            Assert.Multiple(() =>
            {
                // The default branch key 0 matters most: it is what plays when no branch matches, so
                // dropping it would silence every culture the mod does not cover.
                Assert.That(keys, Contains.Item(0u));
                Assert.That(keys, Contains.Item(WwiseHash.Compute("Dwarfs")));
                Assert.That(keys, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void AStateVanillaAlreadyCoversIsReplacedRatherThanDuplicated()
        {
            var merged = new MusicSwitchContainerMergeService()
                .MergeBranches(CreateVanillaContainer(), [new MusicBranch("Dwarfs", NewRanSeqId)]);

            var dwarfBranches = merged.AkDecisionTree.DecisionTree.Nodes
                .Where(node => node.Key == WwiseHash.Compute("Dwarfs"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(dwarfBranches, Has.Count.EqualTo(1));
                Assert.That(dwarfBranches[0].AudioNodeId, Is.EqualTo(NewRanSeqId), "the mod's branch takes priority");
            });
        }

        [Test]
        public void TheMergedContainerSurvivesAWriteReadRoundTrip()
        {
            var merged = new MusicSwitchContainerMergeService()
                .MergeBranches(CreateVanillaContainer(), [new MusicBranch("Araby", NewRanSeqId)]);

            var reloaded = new CAkMusicSwitchCntr_V136();
            reloaded.ReadHirc(new ByteChunk(merged.WriteData()));

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(SwitchContainerId));
                Assert.That(reloaded.TreeDepth, Is.EqualTo(1));
                Assert.That(reloaded.Arguments.Single().GroupId, Is.EqualTo(WwiseHash.Compute("WH3_Campaign_Subcultures")));

                // The root plus three branches. The size on disk is recomputed from the tree, so a
                // wrong count here would desynchronise everything after the container in the bank.
                Assert.That(reloaded.AkDecisionTree.Nodes, Has.Count.EqualTo(4));
                Assert.That(reloaded.TreeDataSize, Is.EqualTo(merged.AkDecisionTree.GetSize()));

                var branch = reloaded.AkDecisionTree.DecisionTree.Nodes
                    .Single(node => node.Key == WwiseHash.Compute("Araby"));
                Assert.That(branch.AudioNodeId, Is.EqualTo(NewRanSeqId));
            });
        }

        [Test]
        public void AMultiArgumentContainerIsRefusedRatherThanMergedWrongly()
        {
            // Battle music branches on the result as well as the culture, so a single keyed node is
            // not a branch there. Writing one anyway produces a tree the game cannot walk.
            var battleContainer = CreateVanillaContainer();
            battleContainer.TreeDepth = 2;

            Assert.Throws<NotSupportedException>(() => new MusicSwitchContainerMergeService()
                .MergeBranches(battleContainer, [new MusicBranch("Araby", NewRanSeqId)]));
        }

        // Stands in for the vanilla WH3_Campaign_Subcultures container: one argument, a default
        // branch and one culture, matching what MusicEventShapeResearch reports.
        static CAkMusicSwitchCntr_V136 CreateVanillaContainer()
        {
            var container = new CAkMusicSwitchCntr_V136
            {
                Id = SwitchContainerId,
                HircType = AkBkHircType.Music_Switch,
                TreeDepth = 1
            };

            container.Arguments.Add(new AkGameSync_V136
            {
                GroupId = WwiseHash.Compute("WH3_Campaign_Subcultures"),
                GroupType = AkGroupType.State
            });

            var root = new AkDecisionTree_V136.Node_V136
            {
                Nodes =
                [
                    CreateBranch(0, 338645341),
                    CreateBranch(WwiseHash.Compute("Dwarfs"), 1056018414)
                ]
            };

            container.AkDecisionTree = new AkDecisionTree_V136
            {
                DecisionTree = root,
                Nodes = AkDecisionTree_V136.FlattenDecisionTree(root)
            };
            container.TreeDataSize = container.AkDecisionTree.GetSize();
            container.UpdateSectionSize();

            return container;
        }

        static AkDecisionTree_V136.Node_V136 CreateBranch(uint key, uint audioNodeId)
        {
            return new AkDecisionTree_V136.Node_V136
            {
                Key = key,
                AudioNodeId = audioNodeId,
                Weight = Wh3MusicHierarchyInformation.BranchWeight,
                Probability = Wh3MusicHierarchyInformation.BranchProbability
            };
        }
    }
}
