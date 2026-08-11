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
        CAkMusicSwitchCntr_V136 CreateModdedContainer(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches);
        CAkMusicSwitchCntr_V136 MergeBranches(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches);
        CAkMusicSwitchCntr_V136 MergeContainers(CAkMusicSwitchCntr_V136 baseContainer, CAkMusicSwitchCntr_V136 mergingContainer);
    }

    /// <summary>
    /// Re-emits a vanilla Music Switch container carrying a mod's branches, the same way
    /// <see cref="SoundBankGeneratorService"/> re-emits a whole Dialogue Event rather than trying to
    /// patch one in place.
    ///
    /// A music Action Event only sets a State. Nothing plays until that State selects a branch here,
    /// so this is what turns a generated music hierarchy into audio the game will actually reach.
    ///
    /// Two forms, because the Dialogue Event path needs both and music needs them for the same
    /// reasons: <see cref="CreateModdedContainer"/> carries the mod's branches alone and goes in the
    /// merging .bnk, so several mods can be combined before vanilla is folded in;
    /// <see cref="MergeBranches"/> folds vanilla in immediately and goes in the testing .bnk, so one
    /// mod can be played on its own.
    /// </summary>
    public class MusicSwitchContainerMergeService : IMusicSwitchContainerMergeService
    {
        public CAkMusicSwitchCntr_V136 CreateModdedContainer(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches)
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

            var moddedRoot = new AkDecisionTree_V136.Node_V136
            {
                Nodes = [.. branches.Select(CreateBranchNode)]
            };

            return CopyWithDecisionTree(vanillaContainer, moddedRoot);
        }

        public CAkMusicSwitchCntr_V136 MergeBranches(CAkMusicSwitchCntr_V136 vanillaContainer, IReadOnlyList<MusicBranch> branches)
        {
            var moddedContainer = CreateModdedContainer(vanillaContainer, branches);

            // The modded tree is the base so that a branch for a State vanilla already covers
            // replaces it, which is what a modder replacing a culture's music is asking for.
            var mergedTree = AkDecisionTree_V136.MergeDecisionTrees(
                moddedContainer.AkDecisionTree.DecisionTree,
                vanillaContainer.AkDecisionTree.DecisionTree);

            return CopyWithDecisionTree(vanillaContainer, mergedTree);
        }

        /// <summary>
        /// Folds one container's branches into another's, for the merger combining several mods.
        /// The base takes priority where both claim the same State, and everything outside the tree
        /// comes from the base. The merger starts from vanilla, so vanilla is what wins - the same
        /// tie break the Dialogue Event merge uses. Branches normally add a subculture vanilla does
        /// not have, so nothing collides and the order does not come up.
        /// </summary>
        public CAkMusicSwitchCntr_V136 MergeContainers(CAkMusicSwitchCntr_V136 baseContainer, CAkMusicSwitchCntr_V136 mergingContainer)
        {
            ArgumentNullException.ThrowIfNull(baseContainer);
            ArgumentNullException.ThrowIfNull(mergingContainer);

            var mergedTree = AkDecisionTree_V136.MergeDecisionTrees(
                baseContainer.AkDecisionTree.DecisionTree,
                mergingContainer.AkDecisionTree.DecisionTree);

            return CopyWithDecisionTree(baseContainer, mergedTree);
        }

        /// <summary>
        /// A new container rather than the one passed in. The vanilla hirc belongs to the audio
        /// repository and is shared with everything else reading vanilla data, so this must not edit
        /// it in place. Everything outside the decision tree is carried over unchanged - the
        /// arguments, the mode and the transition parameters are what make the container the one the
        /// game is already asking for.
        /// </summary>
        private static CAkMusicSwitchCntr_V136 CopyWithDecisionTree(CAkMusicSwitchCntr_V136 vanillaContainer, AkDecisionTree_V136.Node_V136 decisionTree)
        {
            var container = new CAkMusicSwitchCntr_V136
            {
                Id = vanillaContainer.Id,
                HircType = vanillaContainer.HircType,
                MusicTransNodeParams = vanillaContainer.MusicTransNodeParams,
                IsContinuePlayback = vanillaContainer.IsContinuePlayback,
                TreeDepth = vanillaContainer.TreeDepth,
                Arguments = [.. vanillaContainer.Arguments],
                Mode = vanillaContainer.Mode,
                AkDecisionTree = new AkDecisionTree_V136
                {
                    DecisionTree = decisionTree,
                    Nodes = AkDecisionTree_V136.FlattenDecisionTree(decisionTree)
                }
            };

            container.TreeDataSize = container.AkDecisionTree.GetSize();
            container.UpdateSectionSize();
            return container;
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
