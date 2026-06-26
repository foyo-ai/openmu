// <copyright file="PersistentObjectsLookupControllerTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Web.Shared.Services;

/// <summary>
/// Tests for <see cref="PersistentObjectsLookupController"/>.
/// </summary>
[TestFixture]
public class PersistentObjectsLookupControllerTests
{
    /// <summary>
    /// When editing within a context which also supports the requested configuration type
    /// (e.g. selecting an <see cref="ItemDefinition"/> while editing a merchant), the lookup
    /// must serve suggestions from the already-loaded, in-memory game configuration and must
    /// NOT issue a (slow) database query through the edit context.
    /// This is the regression test for the multi-second merchant item lookup.
    /// </summary>
    [Test]
    public async Task GetSuggestionsAsync_WhenEditContextSupportsConfigType_UsesInMemorySourceNotDatabase()
    {
        // Arrange
        var itemDefinitions = new List<ItemDefinition>
        {
            new() { Name = "Jewel of Bless" },
            new() { Name = "Bless of Guardian" },
            new() { Name = "Apple" },
        };

        var dataSource = new Mock<IDataSource<GameConfiguration>>();
        dataSource.Setup(s => s.GetOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<GameConfiguration>(new GameConfiguration()));
        dataSource.Setup(s => s.IsSupporting(typeof(ItemDefinition))).Returns(true);
        dataSource.Setup(s => s.GetAll<ItemDefinition>()).Returns(itemDefinitions);

        var editContext = new Mock<IContext>();
        editContext.Setup(c => c.IsSupporting(typeof(ItemDefinition))).Returns(true);
        editContext.Setup(c => c.GetAsync<ItemDefinition>(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IEnumerable<ItemDefinition>>(Enumerable.Empty<ItemDefinition>()));

        var controller = new PersistentObjectsLookupController(
            Mock.Of<IPersistenceContextProvider>(),
            dataSource.Object,
            Mock.Of<ILogger<PersistentObjectsLookupController>>());

        // Act
        var result = (await controller.GetSuggestionsAsync<ItemDefinition>("bless", editContext.Object)).ToList();

        // Assert
        editContext.Verify(
            c => c.GetAsync<ItemDefinition>(It.IsAny<CancellationToken>()),
            Times.Never,
            "The lookup must not hit the database when the configuration is already in memory.");
        Assert.That(
            result.Select(i => i.Name.ToString()),
            Is.EquivalentTo(new[] { "Jewel of Bless", "Bless of Guardian" }));
    }
}
