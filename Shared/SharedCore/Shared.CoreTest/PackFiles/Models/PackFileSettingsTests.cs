using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace Shared.CoreTest.PackFiles.Models
{
    [TestFixture]
    internal class PackFileSettingsTests
    {
        [Test]
        public void SaveLocationPath_WhenChanged_PublishesSettingsChangedEvent()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var settings = new PackFileSettings();
            settings.SetEventHub(eventHub.Object);

            settings.SaveLocationPath = @"c:\output\test.pack";

            eventHub.Verify(x => x.PublishGlobalEvent(It.Is<PackFileSettingsChangedEvent>(e => ReferenceEquals(e.Settings, settings))), Times.Once);
        }

        [Test]
        public void GameVersion_WhenChanged_PublishesSettingsChangedEvent()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var settings = new PackFileSettings();
            settings.SetEventHub(eventHub.Object);

            settings.GameVersion = GameTypeEnum.Warhammer3;

            eventHub.Verify(x => x.PublishGlobalEvent(It.Is<PackFileSettingsChangedEvent>(e => ReferenceEquals(e.Settings, settings))), Times.Once);
        }

        [Test]
        public void IgnoredFilesWhenSerializing_WhenCollectionChanges_PublishesSettingsChangedEvent()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var settings = new PackFileSettings();
            settings.SetEventHub(eventHub.Object);

            settings.IgnoredFilesWhenSerializing.Add(@"db\ignored.tsv");

            eventHub.Verify(x => x.PublishGlobalEvent(It.Is<PackFileSettingsChangedEvent>(e => ReferenceEquals(e.Settings, settings))), Times.Once);
        }

        [Test]
        public void EnablePackFileCorruptionDetection_WhenChanged_PublishesSettingsChangedEvent()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var settings = new PackFileSettings();
            settings.SetEventHub(eventHub.Object);

            settings.EnablePackFileCorruptionDetection = true;

            eventHub.Verify(x => x.PublishGlobalEvent(It.Is<PackFileSettingsChangedEvent>(e => ReferenceEquals(e.Settings, settings))), Times.Once);
        }

        [Test]
        public void UnchangedValue_DoesNotPublishSettingsChangedEvent()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var settings = new PackFileSettings { SaveLocationPath = @"c:\output\test.pack" };
            settings.SetEventHub(eventHub.Object);

            settings.SaveLocationPath = @"c:\output\test.pack";

            eventHub.Verify(x => x.PublishGlobalEvent(It.IsAny<PackFileSettingsChangedEvent>()), Times.Never);
        }
    }
}
