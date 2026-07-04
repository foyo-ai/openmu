// <copyright file="GameConfigurationReuseInEditContextTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.Persistence.EntityFramework.Json;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Integration tests verifying that a typed edit context (as used by the admin panel's config
/// edit pages) reuses the configuration which was provided to it, instead of reloading the whole
/// configuration graph on every page visit. The reload took multiple seconds per visit and made
/// the server config page very slow. See <see cref="GameConfigurationRepository"/>.
/// Requires a running PostgreSQL database with initialized data (e.g. the openmu-postgres docker
/// container), hence <see cref="ExplicitAttribute"/>.
/// </summary>
[TestFixture]
internal class GameConfigurationReuseInEditContextTests
{
    /// <summary>
    /// Loading a <see cref="GameServerDefinition"/> in a typed edit context must resolve its
    /// configuration navigation to the provided (cached) configuration instance and must be
    /// fast, because no full configuration reload happens.
    /// </summary>
    [Test]
    [Explicit("Integration test - requires a running PostgreSQL database with initialized data (e.g. the openmu-postgres docker container).")]
    public async Task EditContextReusesProvidedConfigurationAsync()
    {
        JsonConverterRegistry.RegisterConverter(new LocalizedStringJsonConverter());
        JsonConverterRegistry.RegisterConverter(new BinaryAsHexJsonConverter());
        ConnectionConfigurator.Initialize(new ConfigFileDatabaseConnectionStringProvider());
        var provider = new PersistenceContextProvider(new NullLoggerFactory(), null);

        // The configuration which the admin panel's cached data source provides.
        using var configContext = provider.CreateNewContext();
        var configuration = (await configContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
        Assert.That(configuration, Is.Not.Null, "Expected an initialized GameConfiguration in the database.");

        // Warm up the typed context model once, so the timing below measures the load, not the model build.
        using (var warmupContext = provider.CreateNewTypedContext(typeof(GameServerDefinition), true, configuration!))
        {
            _ = (await warmupContext.GetAsync<GameServerDefinition>().ConfigureAwait(false)).FirstOrDefault();
        }

        // The admin edit page: a typed context created with that configuration.
        var stopwatch = Stopwatch.StartNew();
        using var editContext = provider.CreateNewTypedContext(typeof(GameServerDefinition), true, configuration!);
        var serverDefinition = (await editContext.GetAsync<GameServerDefinition>().ConfigureAwait(false)).FirstOrDefault();
        stopwatch.Stop();

        Assert.That(serverDefinition, Is.Not.Null, "Expected at least one GameServerDefinition in the database.");
        Assert.Multiple(() =>
        {
            Assert.That(serverDefinition!.GameConfiguration, Is.SameAs(configuration), "The edit context must reuse the provided configuration instead of reloading it.");
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000), "Loading a server definition for editing must not reload the whole configuration.");
        });
    }
}
