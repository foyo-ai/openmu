// <copyright file="PlugInControllerTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.Tests;

using System.Threading;
using Blazored.Modal.Services;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;
using MUnique.OpenMU.Web.Shared.Models;
using MUnique.OpenMU.Web.Shared.Services;

/// <summary>
/// Tests for <see cref="PlugInController"/>.
/// </summary>
[TestFixture]
public class PlugInControllerTests
{
    /// <summary>
    /// Activating a plugin must persist the single <see cref="PlugInConfiguration"/> through a
    /// narrow, type-scoped context — NOT by calling SaveChanges on the shared full game
    /// configuration context, which forces EF Core to run change-detection over the entire
    /// (tens of thousands of entities) configuration graph and made each toggle take seconds.
    /// This is the regression test for the slow plugin activate/deactivate.
    /// </summary>
    [Test]
    public async Task ActivateAsync_SavesViaTypedContextNotTheWholeConfigurationContext()
    {
        // Arrange
        var pluginId = new Guid("11111111-1111-1111-1111-111111111111");
        var config = new MUnique.OpenMU.Persistence.BasicModel.PlugInConfiguration { Id = pluginId, IsActive = false };
        var viewItem = new PlugInConfigurationViewItem(config) { Id = pluginId };

        var sharedContext = new Mock<IContext>();
        sharedContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));

        var typedContext = new Mock<IContext>();
        typedContext.Setup(c => c.GetByIdAsync(pluginId, typeof(PlugInConfiguration), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        typedContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));

        var dataSource = new Mock<IDataSource<GameConfiguration>>();
        dataSource.Setup(s => s.GetOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<GameConfiguration>(new GameConfiguration()));
        dataSource.Setup(s => s.GetContextAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IContext>(sharedContext.Object));

        var contextProvider = new Mock<IPersistenceContextProvider>();
        contextProvider.Setup(p => p.CreateNewTypedContext(typeof(PlugInConfiguration), It.IsAny<bool>(), It.IsAny<GameConfiguration?>()))
            .Returns(typedContext.Object);

        var controller = new PlugInController(dataSource.Object, contextProvider.Object, Mock.Of<IModalService>());

        // Act
        await controller.ActivateAsync(viewItem);

        // Assert
        typedContext.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sharedContext.Verify(
            c => c.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "Toggling a plugin must not SaveChanges over the whole game configuration graph.");

        // The toggled entity must be re-baselined in the shared (warm) context so the toggle does
        // not leave it dirty (which would trigger a spurious 'unsaved changes' prompt elsewhere).
        sharedContext.Verify(c => c.Detach(config), Times.Once);
        sharedContext.Verify(c => c.Attach(config), Times.Once);

        Assert.That(config.IsActive, Is.True);
    }
}
