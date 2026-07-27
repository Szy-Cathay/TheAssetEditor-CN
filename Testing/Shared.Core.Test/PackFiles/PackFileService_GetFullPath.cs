using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Test.Shared.Core.PackFiles
{
    internal class PackFileService_GetFullPath
    {
        [Test]
        public void GetFullPath_WhenEarlierSiblingHasSameBasename_ReturnsRequestedInstancePath()
        {
            var earlier = PackFile.CreateFromBytes("shared.bin", [1]);
            var requested = PackFile.CreateFromBytes("shared.bin", [2]);
            var container = new PackFileContainer("test")
            {
                FileList =
                {
                    ["a\\shared.bin"] = earlier,
                    ["z\\shared.bin"] = requested
                }
            };
            var service = CreateService(container);

            Assert.That(service.GetFullPath(requested), Is.EqualTo("z\\shared.bin"));
        }

        [Test]
        public void GetFullPath_WithExplicitContainer_WhenEarlierSiblingHasSameBasename_ReturnsRequestedInstancePath()
        {
            var earlier = PackFile.CreateFromBytes("shared.bin", [1]);
            var requested = PackFile.CreateFromBytes("shared.bin", [2]);
            var container = new PackFileContainer("test")
            {
                FileList =
                {
                    ["a\\shared.bin"] = earlier,
                    ["z\\shared.bin"] = requested
                }
            };
            var service = CreateService(container);

            Assert.That(service.GetFullPath(requested, container), Is.EqualTo("z\\shared.bin"));
        }

        [Test]
        public void GetFullPath_WhenEarlierContainerHasSameBasename_ReturnsRequestedInstancePath()
        {
            var earlier = PackFile.CreateFromBytes("shared.bin", [1]);
            var requested = PackFile.CreateFromBytes("shared.bin", [2]);
            var earlierContainer = new PackFileContainer("earlier")
            {
                SystemFilePath = "earlier.pack",
                FileList =
                {
                    ["a\\shared.bin"] = earlier
                }
            };
            var requestedContainer = new PackFileContainer("requested")
            {
                SystemFilePath = "requested.pack",
                FileList =
                {
                    ["z\\shared.bin"] = requested
                }
            };
            var service = CreateService(earlierContainer, requestedContainer);

            Assert.That(service.GetFullPath(requested), Is.EqualTo("z\\shared.bin"));
        }

        [Test]
        public void GetFullPath_WhenDetachedObjectSharesBasenameWithStoredFile_Throws()
        {
            var stored = PackFile.CreateFromBytes("shared.bin", [1]);
            var detached = PackFile.CreateFromBytes("shared.bin", [2]);
            var container = new PackFileContainer("test")
            {
                FileList =
                {
                    ["a\\shared.bin"] = stored
                }
            };
            var service = CreateService(container);

            Assert.Throws<Exception>(() => service.GetFullPath(detached));
        }

        private static PackFileService CreateService(params PackFileContainer[] containers)
        {
            var service = new PackFileService(null)
            {
                EnforceGameFilesMustBeLoaded = false
            };

            foreach (var container in containers)
                service.AddContainer(container);

            return service;
        }
    }
}
