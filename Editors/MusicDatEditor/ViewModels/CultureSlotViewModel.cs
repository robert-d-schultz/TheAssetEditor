using CommunityToolkit.Mvvm.ComponentModel;
using Shared.GameFormats.MusicDat;

namespace Editors.MusicDatEditor.ViewModels
{
    /// <summary>One row of the Add Culture wizard: a thing the new culture needs wiring for,
    /// named by the Wwise event it ends up posting.
    ///
    /// The choice on each row is between authoring new audio and borrowing an existing
    /// culture's. Borrowing is not a fallback for the lazy - it is what the shipped scripts do
    /// wherever a culture has no bespoke music of its own, which is why Bretonnia, Dwarfs and
    /// Empire all post music_b_faction_empire.</summary>
    public partial class CultureSlotViewModel : ObservableObject
    {
        readonly Action _changed;

        public MusicDatCultureWiring.CultureSlot Slot { get; }

        [ObservableProperty] bool _include = true;
        [ObservableProperty] bool _useExistingAudio;
        [ObservableProperty] string _referenceCulture = "";
        [ObservableProperty] string _eventInfo = "";
        [ObservableProperty] string _preview = "";

        public string Title => Slot.Title;
        public string Description => Slot.Description;
        public string SkipConsequence => $"If left out: {Slot.SkipConsequence}";
        public IReadOnlyList<string> ReferenceCandidates => Slot.ReferenceCandidates;

        /// <summary>Spelled out per row because the count is the surprise: the ambient fragment
        /// chain is repeated once per randomised variant, so one tick can splice in nine arms.</summary>
        public string Scope => Slot.InsertionCount == 1
            ? "Patches 1 place."
            : $"Patches {Slot.InsertionCount} places (the chain is repeated per randomised variant).";

        public CultureSlotViewModel(MusicDatCultureWiring.CultureSlot slot, Action changed)
        {
            Slot = slot;
            _changed = changed;
        }

        /// <summary>What the arm will post once applied, which is the whole point of the row -
        /// for a new culture it is the event that has to exist in the sound bank, and for a
        /// borrowed one it is the vanilla event being reused.</summary>
        public string EventLine(string musicalCulture)
        {
            if (UseExistingAudio)
                return string.IsNullOrWhiteSpace(ReferenceCulture)
                    ? "Choose an existing culture to borrow from."
                    : $"Reuses '{ReferenceCulture}''s audio - no new Wwise event needed.";

            var name = string.IsNullOrWhiteSpace(musicalCulture) ? "<name>" : musicalCulture;
            var eventName = Slot.EventFor(name);
            return Slot.EventNameIsNegotiable
                ? $"Needs a new Wwise event named {eventName}."
                : $"Needs a new Wwise event named exactly {eventName} - the script builds this name " +
                  "itself, so it has to match precisely.";
        }

        partial void OnIncludeChanged(bool value) => _changed();

        partial void OnUseExistingAudioChanged(bool value)
        {
            // Otherwise the dropdown opens empty and looks broken until something is picked -
            // there is always at least one candidate wherever borrowing is even offered.
            if (value && string.IsNullOrEmpty(ReferenceCulture) && ReferenceCandidates.Count > 0)
                ReferenceCulture = ReferenceCandidates[0];
            _changed();
        }

        partial void OnReferenceCultureChanged(string value) => _changed();
    }
}
