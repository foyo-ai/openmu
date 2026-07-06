// <copyright file="MiniGameEntryTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.MiniGames;
using MUnique.OpenMU.GameLogic.PlugIns.PeriodicTasks;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tests around entering shared mini games (Blood Castle, Devil Square, Chaos Castle).
/// </summary>
[TestFixture]
public class MiniGameEntryTest
{
    /// <summary>
    /// Regression test: once a shared mini game instance has ended and been disposed, a
    /// read-only lookup must NOT resurrect it. This is the root cause of the bug where a
    /// player could right-click the entrance ticket after the event was over and still get
    /// warped into a fresh, empty instance (no monsters) for the rest of the task duration.
    /// </summary>
    [Test]
    public async Task DisposedSharedMiniGameIsNotResurrectedByLookup()
    {
        var (gameContext, definition) = CreateGameContextWithChaosCastle();
        var requester = await PlayerTestHelper.CreatePlayerAsync(gameContext);

        // The event starts: an open instance exists.
        var miniGame = await gameContext.GetMiniGameAsync(definition, requester);
        Assert.That(miniGame.State, Is.EqualTo(MiniGameState.Open));
        Assert.That(await gameContext.GetExistingMiniGameAsync(definition, null), Is.SameAs(miniGame), "the open instance should be found while it is alive");

        // The event ends and disposes (too few players / finished).
        await gameContext.RemoveMiniGameAsync(miniGame);

        // The non-creating lookup must now report "no instance" instead of creating a new one.
        var afterDispose = await gameContext.GetExistingMiniGameAsync(definition, null);
        Assert.That(afterDispose, Is.Null, "a disposed instance must not be re-created by the read-only lookup");

        // Contrast with the creating accessor, which the entry check used to call: it DOES spin up a
        // fresh open instance. That resurrection (reported as "you can enter now") is exactly what let
        // players re-enter an empty event, so the entry/state checks must never use it.
        var resurrected = await gameContext.GetMiniGameAsync(definition, requester);
        Assert.That(resurrected, Is.Not.SameAs(miniGame), "the creating accessor spins up a brand new instance");
        Assert.That(resurrected.State, Is.EqualTo(MiniGameState.Open), "the resurrected instance would wrongly be open for entry");
        await gameContext.RemoveMiniGameAsync(resurrected);
    }

    /// <summary>
    /// Behavior-level regression test on the actual entry gate: after the event's instance has
    /// disposed while the periodic task is still in its Started window, the "duration until next
    /// start" check must report the event as closed (non-zero), not "enter now" (zero). On the old
    /// code this returned zero because the check re-created the instance.
    /// </summary>
    [Test]
    public async Task EntryGateReportsClosedAfterInstanceDisposedWithinStartedWindow()
    {
        var (gameContext, definition) = CreateGameContextWithChaosCastle();

        var plugin = new ChaosCastleStartPlugIn { Configuration = ChaosCastleStartConfiguration.Default };

        // Drive the periodic task NotStarted -> Prepared -> Started (which opens the instances).
        plugin.ForceStart();
        await plugin.ExecuteTaskAsync((GameContext)gameContext);
        await plugin.ExecuteTaskAsync((GameContext)gameContext);

        // While the instance is open, the gate reports "enter now".
        var openInstance = await gameContext.GetExistingMiniGameAsync(definition, null);
        Assert.That(openInstance, Is.Not.Null, "starting the event should open an instance");
        Assert.That(await plugin.GetDurationUntilNextStartAsync(gameContext, definition), Is.EqualTo(TimeSpan.Zero), "entry should be open during the real window");

        // The event ends and disposes, but the periodic task is still within its TaskDuration.
        await gameContext.RemoveMiniGameAsync(openInstance!);

        // The gate must now report a non-zero wait (closed), instead of re-opening the event.
        var durationAfter = await plugin.GetDurationUntilNextStartAsync(gameContext, definition);
        Assert.That(durationAfter, Is.Not.EqualTo(TimeSpan.Zero), "a finished event must not report 'enter now' for the rest of the task duration");
        Assert.That(await gameContext.GetExistingMiniGameAsync(definition, null), Is.Null, "the gate check must not have re-created the instance");
    }

    private static (IGameContext GameContext, MiniGameDefinition Definition) CreateGameContextWithChaosCastle()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        var context = contextProvider.CreateNewContext();
        var gameConfig = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameConfiguration>();

        var mapDef = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameMapDefinition>();
        mapDef.Number = 0;
        mapDef.TerrainData = new byte[ushort.MaxValue + 3];
        gameConfig.Maps.Add(mapDef);
        gameConfig.MaximumInventoryMoney = int.MaxValue;
        gameConfig.RecoveryInterval = int.MaxValue;

        var entrance = context.CreateNew<ExitGate>();
        entrance.Map = mapDef;
        entrance.X1 = 100;
        entrance.Y1 = 100;
        entrance.X2 = 102;
        entrance.Y2 = 102;
        mapDef.ExitGates.Add(entrance);

        var definition = context.CreateNew<MiniGameDefinition>();
        definition.Name = "Chaos Castle 1 (test)";
        definition.Type = MiniGameType.ChaosCastle;
        definition.GameLevel = 1;
        definition.MapCreationPolicy = MiniGameMapCreationPolicy.Shared;
        definition.Entrance = entrance;

        // Keep the entrance window effectively open for the whole test so the background game
        // loop does not advance the state while we assert.
        definition.EnterDuration = TimeSpan.FromHours(1);
        definition.GameDuration = TimeSpan.FromHours(1);
        definition.ExitDuration = TimeSpan.FromMinutes(1);
        definition.MaximumPlayerCount = 70;
        gameConfig.MiniGameDefinitions.Add(definition);

        var mapInitializer = new MapInitializer(gameConfig, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var plugInManager = new PlugInManager(new List<PlugInConfiguration>(), new NullLoggerFactory(), null, null);
        var gameContext = new GameContext(gameConfig, contextProvider, mapInitializer, new NullLoggerFactory(), plugInManager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;

        return (gameContext, definition);
    }
}
