using Shared.Core.ErrorHandling.Exceptions;

namespace Test.Shared.Core.ErrorHandling;

[TestFixture]
public class ExceptionServiceTests
{
    [Test]
    public void Create_WithoutUserMessage_RemainsSupported()
    {
        var service = new ExceptionService([]);

        var information = service.Create(
            new InvalidOperationException("diagnostic detail"));

        Assert.Multiple(() =>
        {
            Assert.That(information.UserMessage, Is.Empty);
            Assert.That(
                information.ExceptionInfo.Single().Message,
                Is.EqualTo("diagnostic detail"));
        });
    }

    [Test]
    public void Create_PreservesLocalizedUserMessageAndDiagnosticDetails()
    {
        var service = new ExceptionService([new DiagnosticProvider()]);
        var exception = new InvalidOperationException(
            "English outer detail",
            new InvalidDataException("English inner detail"));
        var information = service.Create(
            exception,
            "无法导入照片工作室设置。");

        Assert.Multiple(() =>
        {
            Assert.That(
                information.UserMessage,
                Is.EqualTo("无法导入照片工作室设置。"));
            Assert.That(
                information.ExceptionInfo.Select(item => item.Message),
                Is.EqualTo(
                    new[]
                    {
                        "English outer detail",
                        "English inner detail"
                    }));
            Assert.That(
                information.LogHistory,
                Does.Contain("diagnostic detail"));
        });
    }

    private sealed class DiagnosticProvider :
        IExceptionInformationProvider
    {
        public void HydrateExcetion(
            ExceptionInformation exceptionInformation)
        {
            exceptionInformation.LogHistory.Add(
                "diagnostic detail");
        }
    }
}
