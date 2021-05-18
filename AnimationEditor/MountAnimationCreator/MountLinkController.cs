﻿using AnimationEditor.Common.ReferenceModel;
using Common;
using CommonControls.Services;
using Filetypes.AnimationPack;
using FileTypes.PackFiles.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace AnimationEditor.MountAnimationCreator
{
    public class MountLinkController : NotifyPropertyChangedImpl
    {
        ObservableCollection<FragmentDisplayItem> _animationSets0;
        public ObservableCollection<FragmentDisplayItem> AnimationSets0
        {
            get { return _animationSets0; }
            set { SetAndNotify(ref _animationSets0, value); }
        }

        ObservableCollection<FragmentDisplayItem> _animationSets1;
        public ObservableCollection<FragmentDisplayItem> AnimationSets1
        {
            get { return _animationSets1; }
            set { SetAndNotify(ref _animationSets1, value); }
        }

        FragmentDisplayItem _selectedMount;
        public FragmentDisplayItem SelectedMount
        {
            get { return _selectedMount; }
            set { SetAndNotify(ref _selectedMount, value); MuntSelected(value); }
        }

        FragmentDisplayItem _selectedRider;
        public FragmentDisplayItem SeletedRider
        {
            get { return _selectedRider; }
            set { SetAndNotify(ref _selectedRider, value); RiderSelected(value); }
        }


        ObservableCollection<SlotDisplayItem> _possibleMountTags;
        public ObservableCollection<SlotDisplayItem> PossibleMountTags
        {
            get { return _possibleMountTags; }
            set { SetAndNotify(ref _possibleMountTags, value); }
        }


        SlotDisplayItem _selectedMountTag;
        public SlotDisplayItem SelectedMountTag
        {
            get { return _selectedMountTag; }
            set { SetAndNotify(ref _selectedMountTag, value); MountTagSeleted(value); }
        }

        ObservableCollection<SlotDisplayItem> _possibleRiderTags;
        public ObservableCollection<SlotDisplayItem> PossibleRiderTags
        {
            get { return _possibleRiderTags; }
            set { SetAndNotify(ref _possibleRiderTags, value); }
        }


        SlotDisplayItem _selectedRiderTag;
        public SlotDisplayItem SelectedRiderTag
        {
            get { return _selectedRiderTag; }
            set { SetAndNotify(ref _selectedRiderTag, value); RiderTagSelected(value); }
        }


        bool _canBatchProcess;
        public bool CanBatchProcess
        {
            get { return _canBatchProcess; }
            set { SetAndNotify(ref _canBatchProcess, value);  }
        }



        AssetViewModel _rider;
        AssetViewModel _mount;
        PackFileService _pfs;


        public MountLinkController(PackFileService pfs, AssetViewModel rider, AssetViewModel mount)
        {
            var file = pfs.FindFile(@"animations\animation_tables\animation_tables.animpack");

            _pfs = pfs;
            _rider = rider;
            _mount = mount;

            var fragments = AnimationPackLoader.GetFragmentCollections(file as PackFile);
            AnimationSets0 = new ObservableCollection<FragmentDisplayItem>(fragments.Select(x => new FragmentDisplayItem(x)));
            AnimationSets1 = new ObservableCollection<FragmentDisplayItem>(fragments.Select(x => new FragmentDisplayItem(x)));
        }


        void MuntSelected(FragmentDisplayItem value)
        {
            if (value == null)
            {
                PossibleMountTags = new ObservableCollection<SlotDisplayItem>();
                return;
            }
            PossibleMountTags = new ObservableCollection<SlotDisplayItem>(value.Entry.AnimationFragments.Select(x =>new SlotDisplayItem(x)));
            CanBatchProcess = SelectedMount != null && SeletedRider != null;
        }

        void RiderSelected(FragmentDisplayItem value)
        {
            if (value == null)
            {
                PossibleRiderTags = new ObservableCollection<SlotDisplayItem>();
                return;
            }
            PossibleRiderTags = new ObservableCollection<SlotDisplayItem>(value.Entry.AnimationFragments.Select(x => new SlotDisplayItem(x)));
            CanBatchProcess = SelectedMount != null && SeletedRider != null;
        }


        private void MountTagSeleted(SlotDisplayItem value)
        {
            if (value == null)
                return;

            var lookUp = "RIDER_" + value.Entry.Slot.Value;
            SelectedRiderTag = PossibleRiderTags.FirstOrDefault(x => x.Entry.Slot.Value == lookUp);
            
            var file = _pfs.FindFile(value.Entry.AnimationFile);
            _mount.SetAnimation(file as PackFile);
        }

        private void RiderTagSelected(SlotDisplayItem value)
        {
            if (value == null)
                return;

            var file = _pfs.FindFile(value.Entry.AnimationFile);
            _rider.SetAnimation(file as PackFile);
        }


        public List<AnimationFragmentItem> GetAllMountFragments()
        {
            return PossibleMountTags.Select(x => x.Entry).ToList();
        }


        public AnimationFragmentItem GetRiderFragmentFromMount(AnimationFragmentItem mountItem)
        {
            var lookUp = "RIDER_" + mountItem.Slot.Value;
            return PossibleRiderTags.FirstOrDefault(x => x.Entry.Slot.Value == lookUp)?.Entry;
        }

        public class FragmentDisplayItem
        {
            public AnimationFragmentCollection Entry { get; set; }
            public FragmentDisplayItem(AnimationFragmentCollection entry)
            {
                Entry = entry;
            }

            public string DisplayName { get => Entry.FileName; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        public class SlotDisplayItem
        {
            public AnimationFragmentItem Entry { get; set; }
            public SlotDisplayItem(AnimationFragmentItem entry)
            {
                Entry = entry;
            }

            public string DisplayName { get => Entry.Slot.Value; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }

    
}