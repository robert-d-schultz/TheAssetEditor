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
