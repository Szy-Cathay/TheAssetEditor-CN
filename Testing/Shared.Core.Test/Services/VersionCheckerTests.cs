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
        public void NumericHotfixTag_IsNewerThanPublishedPatchVersion()
        {
            var publishedVersion = VersionChecker.ParseReleaseVersion("v2.4.9");
            var hotfixVersion = VersionChecker.ParseReleaseVersion("v2.4.9.1");

            Assert.Multiple(() =>
            {
                Assert.That(hotfixVersion, Is.GreaterThan(publishedVersion));
                Assert.That(VersionChecker.GetReleaseDisplayVersion(
                    new UpdateRelease("v2.4.9.1", "2.4.9-hotfix", "", "", null)),
                    Is.EqualTo("2.4.9-hotfix"));
                Assert.That(VersionChecker.GetReleaseDisplayVersion(
                    new UpdateRelease("v2.4.10", "Asset Editor 国区版 2.4.10", "", "", null)),
                    Is.EqualTo("2.4.10"));
            });
        }

        [Test]
        public void InformationalVersion_DisplaysHotfixLabelWithoutCommitMetadata()
        {
            var displayVersion = VersionChecker.FormatDisplayVersion(
                "2.4.9-hotfix+8b4f26f8",
                new Version(2, 4, 9, 1));

            Assert.That(displayVersion, Is.EqualTo("2.4.9-hotfix"));
        }

        [Test]
        public async Task GetReleasesAsync_UsesPublicFeedWhenApiIsRateLimited()
        {
            const string json = """
                {
                  "tag_name": "v2.4.9.1",
                  "name": "2.4.9-hotfix",
                  "body": "Hotfix notes",
                  "created_at": "2026-09-29T00:00:00+08:00"
                }
                """;
            var requestedUris = new List<Uri>();
            using var client = new HttpClient(new RouteResponseHandler(uri =>
            {
                requestedUris.Add(uri);
                return uri.AbsolutePath.Contains("/raw/master/update-feed.json")
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    }
                    : new HttpResponseMessage(HttpStatusCode.Forbidden);
            }));

            var releases = await VersionChecker.GetReleasesAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(releases, Has.Count.EqualTo(1));
                Assert.That(releases![0].TagName, Is.EqualTo("v2.4.9.1"));
                Assert.That(requestedUris, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task GetReleasesAsync_MissingFeedFallsBackToApiAndSortsByVersion()
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
            var requestedUris = new List<Uri>();
            using var client = new HttpClient(new RouteResponseHandler(uri =>
            {
                requestedUris.Add(uri);
                return uri.AbsolutePath.Contains("/raw/master/update-feed.json")
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
            }));

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
                Assert.That(requestedUris, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void GetReleasesAsync_BothSourcesUnavailableThrows()
        {
            using var client = new HttpClient(new StaticResponseHandler(
                "failure",
                HttpStatusCode.ServiceUnavailable));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await VersionChecker.GetReleasesAsync(client));

            Assert.That(exception!.InnerException, Is.TypeOf<HttpRequestException>());
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

        private sealed class RouteResponseHandler(Func<Uri, HttpResponseMessage> responseFactory)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(responseFactory(request.RequestUri!));
            }
        }
    }
}
