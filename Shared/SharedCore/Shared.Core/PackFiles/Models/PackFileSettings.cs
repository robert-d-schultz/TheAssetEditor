using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shared.Core.Events;
using Shared.Core.PackFiles.Events;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Models
{
    public class PackFileSettings
    {
        private string? _saveLocationPath;
        private GameTypeEnum? _gameVersion;
        private bool _enablePackFileCorruptionDetection;
        private ObservableCollection<string> _ignoredFilesWhenSerializing = new();
        private IGlobalEventHub? _eventHub;

        public PackFileSettings()
        {
            _ignoredFilesWhenSerializing.CollectionChanged += OnIgnoredFilesChanged;
        }

        public void SetEventHub(IGlobalEventHub? eventHub)
        {
            _eventHub = eventHub;
        }

        public string? SaveLocationPath
        {
            get => _saveLocationPath;
            set
            {
                if (_saveLocationPath == value)
                    return;

                _saveLocationPath = value;
                PublishSettingsChanged();
            }
        }

        public GameTypeEnum? GameVersion
        {
            get => _gameVersion;
            set
            {
                if (_gameVersion == value)
                    return;

                _gameVersion = value;
                PublishSettingsChanged();
            }
        }

        public bool EnablePackFileCorruptionDetection
        {
            get => _enablePackFileCorruptionDetection;
            set
            {
                if (_enablePackFileCorruptionDetection == value)
                    return;

                _enablePackFileCorruptionDetection = value;
                PublishSettingsChanged();
            }
        }

        public ObservableCollection<string> IgnoredFilesWhenSerializing
        {
            get => _ignoredFilesWhenSerializing;
            set
            {
                if (ReferenceEquals(_ignoredFilesWhenSerializing, value))
                    return;

                _ignoredFilesWhenSerializing.CollectionChanged -= OnIgnoredFilesChanged;
                _ignoredFilesWhenSerializing = value ?? new ObservableCollection<string>();
                _ignoredFilesWhenSerializing.CollectionChanged += OnIgnoredFilesChanged;
                PublishSettingsChanged();
            }
        }

        private void OnIgnoredFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PublishSettingsChanged();
        }

        private void PublishSettingsChanged()
        {
            _eventHub?.PublishGlobalEvent(new PackFileSettingsChangedEvent(this));
        }
    }
}