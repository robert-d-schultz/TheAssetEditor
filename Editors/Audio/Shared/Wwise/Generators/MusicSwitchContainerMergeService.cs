using System;
using System.Collections.Generic;
using System.Linq;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators
{
    /// <summary>One branch to add to a vanilla Music Switch container's decision tree.</summary>
    public record MusicBranch(string StateName, uint RandomSequenceId);

    public interface IMusicSwitchContainerMergeService
    {
        CAkMusicSwitchCntr_V136 MergeBranches(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches);
    }

    /// <summary>
    /// Adds a mod's branches to a vanilla Music Switch container's decision tree and re-emits the
    /// whole container, the same way <see cref="SoundBankGeneratorService"/> re-emits a whole
    /// Dialogue Event rather than trying to patch one in place.
    ///
    /// A music Action Event only sets a State. Nothing plays until that State selects a branch here,
    /// so this is what turns a generated music hierarchy into audio the game will actually reach.
    /// </summary>
    public class MusicSwitchContainerMergeService : IMusicSwitchContainerMergeService
    {
        public CAkMusicSwitchCntr_V136 MergeBranches(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches)
        {
            ArgumentNullException.ThrowIfNull(vanillaContainer);
            ArgumentNullException.ThrowIfNull(branches);

            // Only single argument containers are handled. The campaign subculture container is one,
            // so a branch is a single node keyed on the State. Battle music branches on the result
            // as well as the culture and is six deep, which needs a key per level - refused here
            // rather than silently written as a tree the game cannot walk.
            if (vanillaContainer.TreeDepth != 1)
                throw new NotSupportedException(
                    $"Music Switch container {vanillaContainer.Id} branches on {vanillaContainer.TreeDepth} arguments. " +
                    "Only containers with a single argument can be merged into.");

            var vanillaDecisionTree = vanillaContainer.AkDecisionTree.DecisionTree;

            var moddedRoot = new AkDecisionTree_V136.Node_V136
            {
                Nodes = [.. branches.Select(CreateBranchNode)]
            };

            // The modded tree is the base so that a branch for a State vanilla already covers
            // replaces it, which is what a modder replacing a culture's music is asking for.
            var mergedTree = AkDecisionTree_V136.MergeDecisionTrees(moddedRoot, vanillaDecisionTree);

            // A new container rather than the one passed in. The vanilla hirc belongs to the audio
            // repository and is shared with everything else reading vanilla data, so merging must
            // not edit it in place.
            var mergedContainer = new CAkMusicSwitchCntr_V136
            {
                Id = vanillaContainer.Id,
                HircType = vanillaContainer.HircType,
                MusicTransNodeParams = vanillaContainer.MusicTransNodeParams,
                IsContinuePlayback = vanillaContainer.IsContinuePlayback,
                TreeDepth = vanillaContainer.TreeDepth,
                Arguments = [.. vanillaContainer.Arguments],
                Mode = vanillaContainer.Mode
            };

            mergedContainer.AkDecisionTree = new AkDecisionTree_V136
            {
                DecisionTree = mergedTree,
                Nodes = AkDecisionTree_V136.FlattenDecisionTree(mergedTree)
            };
            mergedContainer.TreeDataSize = mergedContainer.AkDecisionTree.GetSize();
            mergedContainer.UpdateSectionSize();

            return mergedContainer;
        }

        private static AkDecisionTree_V136.Node_V136 CreateBranchNode(MusicBranch branch)
        {
            return new AkDecisionTree_V136.Node_V136
            {
                Key = WwiseHash.Compute(branch.StateName),
                AudioNodeId = branch.RandomSequenceId,
                Weight = Wh3MusicHierarchyInformation.BranchWeight,
                Probability = Wh3MusicHierarchyInformation.BranchProbability
            };
        }
    }
}
