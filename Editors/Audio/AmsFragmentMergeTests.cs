using Editors.Audio.Shared.Wwise.Generators;
using Shared.ByteParsing;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Test.Audio
{
    // Adding a culture to the ambient fragments Switch container. The shape asserted here was read
    // out of campaign_music__core.bnk with MusicEventShapeResearch.DumpTheFragmentsSwitchContainer:
    // a faction container whose children are themselves Switch containers keyed on the musical key
    // the score is in, and two cultures pointing at the same child where vanilla shares a fragment
    // set between them.
    internal class AmsFragmentMergeTests
    {
        const uint FactionContainerId = 418295225;
        const uint KeyGroupId = 2939663928;
        const uint NewKeySwitchId = 5000;

        [Test]
        public void ANewCultureBecomesASwitchAndAChild()
        {
            var merged = new AmsFragmentMergeService()
                .AddCultures(CreateVanillaContainer(), [new AmsFragmentBranch("Araby", NewKeySwitchId)]);

            Assert.Multiple(() =>
            {
                var switchPackage = merged.SwitchList
                    .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                    .Single(package => package.SwitchId == WwiseHash.Compute("Araby"));

                Assert.That(switchPackage.NodeIdList, Is.EqualTo(new[] { NewKeySwitchId }));

                // A switch pointing at a node the container does not claim as a child is not
                // resolved, so the two lists have to move together.
                Assert.That(merged.Children.ChildIds, Does.Contain(NewKeySwitchId));
                Assert.That(merged.Children.NumChilds, Is.EqualTo((uint)merged.Children.ChildIds.Count));
                Assert.That(merged.NumSwitchGroups, Is.EqualTo((uint)merged.SwitchList.Count));
            });
        }

        [Test]
        public void TheVanillaCulturesAreKept()
        {
            var vanillaContainer = CreateVanillaContainer();
            var merged = new AmsFragmentMergeService()
                .AddCultures(vanillaContainer, [new AmsFragmentBranch("Araby", NewKeySwitchId)]);

            Assert.Multiple(() =>
            {
                foreach (var vanillaSwitch in vanillaContainer.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
                {
                    var mergedSwitch = merged.SwitchList
                        .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                        .SingleOrDefault(package => package.SwitchId == vanillaSwitch.SwitchId);

                    Assert.That(mergedSwitch, Is.Not.Null, $"vanilla switch {vanillaSwitch.SwitchId} was dropped");
                    Assert.That(mergedSwitch.NodeIdList, Is.EqualTo(vanillaSwitch.NodeIdList));
                }

                Assert.That(merged.DefaultSwitch, Is.EqualTo(vanillaContainer.DefaultSwitch));
                Assert.That(merged.GroupId, Is.EqualTo(vanillaContainer.GroupId));
            });
        }

        [Test]
        public void MergingDoesNotEditTheVanillaContainer()
        {
            var vanillaContainer = CreateVanillaContainer();
            new AmsFragmentMergeService().AddCultures(vanillaContainer, [new AmsFragmentBranch("Araby", NewKeySwitchId)]);

            Assert.Multiple(() =>
            {
                Assert.That(vanillaContainer.SwitchList, Has.Count.EqualTo(3));
                Assert.That(vanillaContainer.Children.ChildIds, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void TheMergedContainerSurvivesAWriteReadRoundTrip()
        {
            var merged = new AmsFragmentMergeService()
                .AddCultures(CreateVanillaContainer(), [new AmsFragmentBranch("Araby", NewKeySwitchId)]);

            var reloaded = new CAkSwitchCntr_V136();
            reloaded.ReadHirc(new ByteChunk(merged.WriteData()));

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(FactionContainerId));
                Assert.That(reloaded.GroupId, Is.EqualTo(merged.GroupId));
                Assert.That(reloaded.DefaultSwitch, Is.EqualTo(merged.DefaultSwitch));
                Assert.That(reloaded.SwitchList, Has.Count.EqualTo(4));
                Assert.That(reloaded.Children.ChildIds, Is.EqualTo(merged.Children.ChildIds));
                Assert.That(reloaded.Parameters, Has.Count.EqualTo(merged.Parameters.Count));

                var reloadedSwitch = reloaded.SwitchList
                    .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                    .Single(package => package.SwitchId == WwiseHash.Compute("Araby"));
                Assert.That(reloadedSwitch.NodeIdList, Is.EqualTo(new[] { NewKeySwitchId }));
            });
        }

        [Test]
        public void TheKeySwitchMirrorsTheKeysAnExistingCultureUses()
        {
            // Which keys the score uses is the game's business, so they are copied from a sibling
            // rather than written down - a table would go stale the moment a patch added one.
            var vanillaContainer = CreateVanillaContainer();
            var keySwitch = new AmsFragmentMergeService().CreateKeySwitchContainer(
                vanillaContainer, new SiblingLookup(), NewKeySwitchId, targetHircId: 6000);

            Assert.Multiple(() =>
            {
                Assert.That(keySwitch.Id, Is.EqualTo(NewKeySwitchId));
                Assert.That(keySwitch.GroupId, Is.EqualTo(KeyGroupId));
                Assert.That(keySwitch.NodeBaseParams.DirectParentId, Is.EqualTo(FactionContainerId));
                Assert.That(keySwitch.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>().Select(x => x.SwitchId),
                    Is.EqualTo(new[] { WwiseHash.Compute("None"), WwiseHash.Compute("C_01"), WwiseHash.Compute("C_02") }));

                // Every key resolves to the same audio: vanilla varies the fragment by key because
                // the fragments are written against the score, and a mod supplying one set cannot.
                foreach (var switchPackage in keySwitch.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
                    Assert.That(switchPackage.NodeIdList, Is.EqualTo(new[] { 6000u }));

                Assert.That(keySwitch.Children.ChildIds, Is.EqualTo(new[] { 6000u }));
            });
        }

        [Test]
        public void TheKeySwitchSurvivesAWriteReadRoundTrip()
        {
            var keySwitch = new AmsFragmentMergeService().CreateKeySwitchContainer(
                CreateVanillaContainer(), new SiblingLookup(), NewKeySwitchId, targetHircId: 6000);

            var reloaded = new CAkSwitchCntr_V136();
            reloaded.ReadHirc(new ByteChunk(keySwitch.WriteData()));

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(NewKeySwitchId));
                Assert.That(reloaded.GroupId, Is.EqualTo(KeyGroupId));
                Assert.That(reloaded.SwitchList, Has.Count.EqualTo(3));
                Assert.That(reloaded.Children.ChildIds, Is.EqualTo(new[] { 6000u }));
            });
        }

        [Test]
        public void AContainerWithNoSiblingToCopyKeysFromIsRefused()
        {
            var vanillaContainer = CreateVanillaContainer();

            Assert.That(
                () => new AmsFragmentMergeService().CreateKeySwitchContainer(
                    vanillaContainer, new EmptyLookup(), NewKeySwitchId, targetHircId: 6000),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void CombiningTwoModsKeepsBothCultures()
        {
            var vanillaContainer = CreateVanillaContainer();
            var service = new AmsFragmentMergeService();

            var firstMod = service.AddCultures(vanillaContainer, [new AmsFragmentBranch("Araby", 5000)]);
            var secondMod = service.AddCultures(vanillaContainer, [new AmsFragmentBranch("Nippon", 5001)]);

            var merged = service.MergeContainers(vanillaContainer, firstMod);
            merged = service.MergeContainers(merged, secondMod);

            var switchIds = merged.SwitchList
                .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                .Select(package => package.SwitchId)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(switchIds, Does.Contain(WwiseHash.Compute("Araby")));
                Assert.That(switchIds, Does.Contain(WwiseHash.Compute("Nippon")));
                Assert.That(merged.Children.ChildIds, Does.Contain(5000u));
                Assert.That(merged.Children.ChildIds, Does.Contain(5001u));
            });
        }

        [Test]
        public void TheBaseWinsACultureBothModsClaim()
        {
            var vanillaContainer = CreateVanillaContainer();
            var service = new AmsFragmentMergeService();

            var firstMod = service.AddCultures(vanillaContainer, [new AmsFragmentBranch("Araby", 5000)]);
            var secondMod = service.AddCultures(vanillaContainer, [new AmsFragmentBranch("Araby", 5001)]);

            var merged = service.MergeContainers(vanillaContainer, firstMod);
            merged = service.MergeContainers(merged, secondMod);

            var arabySwitch = merged.SwitchList
                .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                .Single(package => package.SwitchId == WwiseHash.Compute("Araby"));

            Assert.That(arabySwitch.NodeIdList, Is.EqualTo(new[] { 5000u }));
        }

        /// <summary>Cut down from the real thing: two cultures sharing one fragment set, which is
        /// what vanilla does for Empire and WH1_2, plus the 'None' default.</summary>
        static CAkSwitchCntr_V136 CreateVanillaContainer()
        {
            var container = new CAkSwitchCntr_V136
            {
                Id = FactionContainerId,
                HircType = AkBkHircType.SwitchContainer,
                EGroupType = AkGroupType.State,
                GroupId = WwiseHash.Compute("WH3_Campaign_Music_AMS_Fragments_Faction"),
                DefaultSwitch = WwiseHash.Compute("None"),
                NodeBaseParams = new NodeBaseParams_V136()
            };

            container.Children.ChildIds.AddRange([1001u, 1002u]);
            container.Children.NumChilds = 2;

            foreach (var (switchName, nodeId) in new[] { ("None", 1001u), ("Empire", 1002u), ("WH1_2", 1002u) })
            {
                container.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                {
                    SwitchId = WwiseHash.Compute(switchName),
                    NodeIdList = [nodeId]
                });
            }

            container.NumSwitchGroups = 3;
            return container;
        }

        /// <summary>A vanilla culture's key switch, which is what a new culture's is modelled on.</summary>
        sealed class SiblingLookup : IAudioRepositoryLookup
        {
            public CAkSwitchCntr_V136 FindSwitchContainer(uint id)
            {
                if (id != 1001)
                    return null;

                var keySwitch = new CAkSwitchCntr_V136
                {
                    Id = 1001,
                    EGroupType = AkGroupType.State,
                    GroupId = KeyGroupId,
                    DefaultSwitch = WwiseHash.Compute("None"),
                    NodeBaseParams = new NodeBaseParams_V136()
                };

                foreach (var keyName in new[] { "None", "C_01", "C_02" })
                {
                    keySwitch.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                    {
                        SwitchId = WwiseHash.Compute(keyName),
                        NodeIdList = [2001u]
                    });
                }

                return keySwitch;
            }
        }

        sealed class EmptyLookup : IAudioRepositoryLookup
        {
            public CAkSwitchCntr_V136 FindSwitchContainer(uint id) => null;
        }
    }
}
