using System;
using System.Collections.Generic;
using System.Linq;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators
{
    /// <summary>One culture to add to the vanilla ambient fragments container.</summary>
    public record AmsFragmentBranch(string StateName, uint KeySwitchContainerId);

    public interface IAmsFragmentMergeService
    {
        CAkSwitchCntr_V136 AddCultures(CAkSwitchCntr_V136 vanillaContainer, IReadOnlyList<AmsFragmentBranch> branches);
        CAkSwitchCntr_V136 MergeContainers(CAkSwitchCntr_V136 baseContainer, CAkSwitchCntr_V136 mergingContainer);
        CAkSwitchCntr_V136 CreateKeySwitchContainer(CAkSwitchCntr_V136 vanillaContainer, IAudioRepositoryLookup lookup, uint keySwitchContainerId, uint targetHircId);
    }

    /// <summary>Just enough of the repository for the key switch to be mirrored from a sibling,
    /// rather than taking the whole interface for one lookup.</summary>
    public interface IAudioRepositoryLookup
    {
        CAkSwitchCntr_V136 FindSwitchContainer(uint id);
    }

    /// <summary>
    /// Adds a culture to the vanilla ambient fragments Switch container, and builds the key switch
    /// that culture's audio hangs off.
    ///
    /// Unlike a switch track, a Switch container identifies its branches by switch id rather than by
    /// position, so this merges by key the way a decision tree does and two mods cannot collide by
    /// accident. The children list still has to be kept in step with the switch list - a switch
    /// pointing at a node the container does not claim as a child is not resolved.
    /// </summary>
    public class AmsFragmentMergeService : IAmsFragmentMergeService
    {
        public CAkSwitchCntr_V136 AddCultures(CAkSwitchCntr_V136 vanillaContainer, IReadOnlyList<AmsFragmentBranch> branches)
        {
            ArgumentNullException.ThrowIfNull(vanillaContainer);
            ArgumentNullException.ThrowIfNull(branches);

            var merged = CopyContainer(vanillaContainer);

            foreach (var branch in branches)
            {
                var switchId = WwiseHash.Compute(branch.StateName);

                var existing = merged.SwitchList
                    .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                    .FirstOrDefault(switchPackage => switchPackage.SwitchId == switchId);

                if (existing != null)
                {
                    // Re-pointed rather than added twice; the first package would win and the second
                    // would never be looked at.
                    existing.NodeIdList.Clear();
                    existing.NodeIdList.Add(branch.KeySwitchContainerId);
                }
                else
                {
                    merged.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                    {
                        SwitchId = switchId,
                        NodeIdList = [branch.KeySwitchContainerId]
                    });
                }

                if (!merged.Children.ChildIds.Contains(branch.KeySwitchContainerId))
                    merged.Children.ChildIds.Add(branch.KeySwitchContainerId);
            }

            merged.NumSwitchGroups = (uint)merged.SwitchList.Count;
            merged.Children.NumChilds = (uint)merged.Children.ChildIds.Count;
            merged.UpdateSectionSize();
            return merged;
        }

        /// <summary>
        /// Folds one mod's container into another's. The base takes priority where two claim the same
        /// culture, the same tie break every other merge here uses.
        /// </summary>
        public CAkSwitchCntr_V136 MergeContainers(CAkSwitchCntr_V136 baseContainer, CAkSwitchCntr_V136 mergingContainer)
        {
            ArgumentNullException.ThrowIfNull(baseContainer);
            ArgumentNullException.ThrowIfNull(mergingContainer);

            var merged = CopyContainer(baseContainer);
            var claimedSwitchIds = merged.SwitchList
                .Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>()
                .Select(switchPackage => switchPackage.SwitchId)
                .ToHashSet();

            foreach (var switchPackage in mergingContainer.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
            {
                if (claimedSwitchIds.Contains(switchPackage.SwitchId))
                    continue;

                merged.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                {
                    SwitchId = switchPackage.SwitchId,
                    NodeIdList = [.. switchPackage.NodeIdList]
                });

                foreach (var nodeId in switchPackage.NodeIdList)
                {
                    if (!merged.Children.ChildIds.Contains(nodeId))
                        merged.Children.ChildIds.Add(nodeId);
                }
            }

            merged.NumSwitchGroups = (uint)merged.SwitchList.Count;
            merged.Children.NumChilds = (uint)merged.Children.ChildIds.Count;
            merged.UpdateSectionSize();
            return merged;
        }

        /// <summary>
        /// The Switch container on the musical key Group that a culture's audio hangs off.
        ///
        /// The keys are mirrored from a vanilla sibling rather than written down here. What the key
        /// Group is, and which keys the score actually uses, are the game's business and would go
        /// stale the moment a patch added one; a sibling always has the current answer.
        /// </summary>
        public CAkSwitchCntr_V136 CreateKeySwitchContainer(
            CAkSwitchCntr_V136 vanillaContainer, IAudioRepositoryLookup lookup, uint keySwitchContainerId, uint targetHircId)
        {
            var sibling = vanillaContainer.Children.ChildIds
                .Select(lookup.FindSwitchContainer)
                .FirstOrDefault(child => child != null && child.SwitchList.Count != 0);

            if (sibling == null)
                throw new NotSupportedException(
                    $"Switch container {vanillaContainer.Id} has no child Switch container to take the musical keys from, " +
                    "so a new culture's fragments cannot be keyed the way vanilla's are.");

            var keySwitch = new CAkSwitchCntr_V136
            {
                Id = keySwitchContainerId,
                HircType = AkBkHircType.SwitchContainer,
                EGroupType = sibling.EGroupType,
                GroupId = sibling.GroupId,
                DefaultSwitch = sibling.DefaultSwitch,
                BIsContinuousValidation = sibling.BIsContinuousValidation,
                NodeBaseParams = new NodeBaseParams_V136 { DirectParentId = vanillaContainer.Id }
            };

            // Every key resolves to the same audio. Vanilla varies the fragment by key because the
            // fragments are written against the score; a mod supplying one set has nothing to vary.
            foreach (var siblingSwitch in sibling.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
            {
                keySwitch.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                {
                    SwitchId = siblingSwitch.SwitchId,
                    NodeIdList = [targetHircId]
                });
            }

            keySwitch.Children.ChildIds.Add(targetHircId);
            keySwitch.Children.NumChilds = 1;
            keySwitch.NumSwitchGroups = (uint)keySwitch.SwitchList.Count;
            keySwitch.UpdateSectionSize();
            return keySwitch;
        }

        private static CAkSwitchCntr_V136 CopyContainer(CAkSwitchCntr_V136 source)
        {
            var copy = new CAkSwitchCntr_V136
            {
                Id = source.Id,
                HircType = source.HircType,
                NodeBaseParams = source.NodeBaseParams,
                EGroupType = source.EGroupType,
                GroupId = source.GroupId,
                DefaultSwitch = source.DefaultSwitch,
                BIsContinuousValidation = source.BIsContinuousValidation,
                NumSwitchParams = source.NumSwitchParams
            };

            copy.Children.ChildIds.AddRange(source.Children.ChildIds);
            copy.Children.NumChilds = (uint)copy.Children.ChildIds.Count;
            copy.Parameters.AddRange(source.Parameters);

            // Rebuilt rather than shared so merging cannot edit the repository's copy.
            foreach (var switchPackage in source.SwitchList.Cast<CAkSwitchCntr_V136.CAkSwitchPackage_V136>())
            {
                copy.SwitchList.Add(new CAkSwitchCntr_V136.CAkSwitchPackage_V136
                {
                    SwitchId = switchPackage.SwitchId,
                    NodeIdList = [.. switchPackage.NodeIdList]
                });
            }

            copy.NumSwitchGroups = (uint)copy.SwitchList.Count;
            return copy;
        }
    }
}
