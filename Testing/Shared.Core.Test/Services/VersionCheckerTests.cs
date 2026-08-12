using System.Net;
using System.Text;
using Shared.Core.Services;

namespace Test.Shared.Core.Services
{
    internal class VersionCheckerTests
    {
        [Test]
        public void NormalizeVersion_PreservesZeroBuildForReleaseComparison()
        {
            var currentVersion = VersionChecker.NormalizeVersion(new Version(1, 1, 0, 0));
            var releaseVersion = VersionChecker.ParseReleaseVersion("v1.1.0");

            Assert.Multiple(() =>
            {
                Assert.That(currentVersion.ToString(), Is.EqualTo("1.1.0"));
                Assert.That(currentVersion, Is.EqualTo(releaseVersion));
            });
        }

        [Test]
        public async Task GetReleasesAsync_MapsGiteeReleasesAndSortsByVersion()
        {
            const string json = """
                [
                  {
                    "tag_name": "v2.4.2",
                    "name": "Second",
                    "body": "Notes 2",
                    "created_at": "2026-08-13T10:00:00+08:00"
                  },
                  {
                    "tag_name": "v2.5.0",
                    "name": "First",
                    "body": "Notes 1",
                    "created_at": "2026-08-13T11:00:00+08:00"
                  }
                ]
                """;
            using var client = new HttpClient(new StaticResponseHandler(json));

            var releases = await VersionChecker.GetReleasesAsync(client);

            Assert.That(releases, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(releases![0].TagName, Is.EqualTo("v2.5.0"));
                Assert.That(releases[0].HtmlUrl, Is.EqualTo(
                    "https://gitee.com/szy-cathay/AssetEditor-CN-Downloads/releases/tag/v2.5.0"));
                Assert.That(releases[0].Body, Is.EqualTo("Notes 1"));
                Assert.That(releases[0].PublishedAt, Is.EqualTo(
                    DateTimeOffset.Parse("2026-08-13T11:00:00+08:00")));
            });
        }

        [Test]
        public async Task GetReleasesAsync_RequestFailureReturnsNull()
        {
            using var client = new HttpClient(new StaticResponseHandler(
                "failure",
                HttpStatusCode.ServiceUnavailable));

            var releases = await VersionChecker.GetReleasesAsync(client);

            Assert.That(releases, Is.Null);
        }

        private sealed class StaticResponseHandler(
            string content,
            HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
