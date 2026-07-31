using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

using System.Threading;
using System.Windows;

namespace AssetEditorTests;

public class FolderProjectVersionControlDependencyInjectionTests
{
    [NUnit.Framework.Test]
    public void HostServices_AreRegisteredAndScopesValidate()
    {
        var provider =
            new DependencyInjectionConfig(false).Build(true);
        try
        {
            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    provider.GetRequiredService<
                        IFolderProjectOpenService>(),
                    NUnit.Framework.Is.TypeOf<
                        FolderProjectOpenService>());
                NUnit.Framework.Assert.That(
                    provider.GetRequiredService<
                        IFolderProjectVersionControlWindowService>(),
                    NUnit.Framework.Is.TypeOf<
                        FolderProjectVersionControlWindowService>());
                NUnit.Framework.Assert.That(
                    provider.GetRequiredService<
                        FolderProjectVersionControlViewModel>(),
                    NUnit.Framework.Is.Not.Null);
                NUnit.Framework.Assert.That(
                    provider.GetRequiredService<
                        OpenFolderProjectVersionControlCommand>(),
                    NUnit.Framework.Is.Not.Null);
            });
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Apartment(ApartmentState.STA)]
    public void SetOwner_AssignsMainWindowAndRejectsSelfReference()
    {
        var owner = new Window();
        var child = new Window();
        var selfOwnedCandidate = new Window();
        try
        {
            owner.Show();
            FolderProjectVersionControlWindowService.SetOwner(
                child,
                owner);
            FolderProjectVersionControlWindowService.SetOwner(
                selfOwnedCandidate,
                selfOwnedCandidate);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    child.Owner,
                    NUnit.Framework.Is.SameAs(owner));
                NUnit.Framework.Assert.That(
                    selfOwnedCandidate.Owner,
                    NUnit.Framework.Is.Null);
            });
        }
        finally
        {
            selfOwnedCandidate.Close();
            child.Close();
            owner.Close();
        }
    }
}
