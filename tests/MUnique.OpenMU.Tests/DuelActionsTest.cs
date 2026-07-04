// <copyright file="DuelActionsTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Duel;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tests the duel flow (request, accept, spectate) headlessly, without a game client.
/// </summary>
[TestFixture]
public class DuelActionsTest
{
    private const int EntranceFee = 30_000;
    private const int StartMoney = 10_000_000;

    /// <summary>
    /// Tests that two players can start a duel and a third can spectate it,
    /// exercising the same server actions the client triggers.
    /// </summary>
    [Test]
    public async Task TwoPlayersDuelAndAThirdSpectates()
    {
        var (gameContext, _) = CreateGameContextWithDuelConfig();
        var requester = await this.CreateReadyDuelistAsync(gameContext);
        var opponent = await this.CreateReadyDuelistAsync(gameContext);
        var spectator = await this.CreateReadyDuelistAsync(gameContext);

        var duelActions = new DuelActions();

        // 1) Requester challenges the opponent.
        await duelActions.HandleDuelRequestAsync(requester, opponent);

        Assert.That(requester.DuelRoom, Is.Not.Null, "requester should be in a duel room after requesting");
        Assert.That(opponent.DuelRoom, Is.SameAs(requester.DuelRoom), "opponent should share the same duel room");
        var duelRoom = requester.DuelRoom!;
        Assert.That(duelRoom.State, Is.EqualTo(DuelState.DuelRequested));
        Assert.That(duelRoom.Requester, Is.SameAs(requester));
        Assert.That(duelRoom.Opponent, Is.SameAs(opponent));

        // 2) Opponent accepts the duel.
        await duelActions.HandleDuelResponseAsync(opponent, requester, accepted: true);

        Assert.That(duelRoom.State, Is.EqualTo(DuelState.DuelAccepted), "duel should be accepted");
        Assert.That(requester.Money, Is.EqualTo(StartMoney - EntranceFee), "entrance fee should be deducted from requester");
        Assert.That(opponent.Money, Is.EqualTo(StartMoney - EntranceFee), "entrance fee should be deducted from opponent");

        // 3) A third player joins the duel channel as a spectator.
        await duelActions.HandleDuelChannelJoinRequestAsync(spectator, (byte)duelRoom.Index);

        Assert.That(duelRoom.Spectators, Does.Contain(spectator), "the spectator should be added to the duel room");
    }

    private static (IGameContext GameContext, GameMapDefinition Map) CreateGameContextWithDuelConfig()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        var context = contextProvider.CreateNewContext();
        var gameConfig = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameConfiguration>();

        var mapDef = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameMapDefinition>();
        mapDef.Number = 0;
        mapDef.TerrainData = new byte[ushort.MaxValue + 3];
        gameConfig.Maps.Add(mapDef);
        gameConfig.MaximumPartySize = 5;
        gameConfig.RecoveryInterval = int.MaxValue;
        gameConfig.MaximumInventoryMoney = int.MaxValue;

        ExitGate MakeGate(byte x, byte y)
        {
            var gate = context.CreateNew<ExitGate>();
            gate.Map = mapDef;
            gate.X1 = x;
            gate.Y1 = y;
            gate.X2 = (byte)(x + 2);
            gate.Y2 = (byte)(y + 2);
            return gate;
        }

        // Keep every duel gate on map 0 so warping does not require a second map to be initialized.
        var duelConfig = context.CreateNew<DuelConfiguration>();
        duelConfig.MaximumScore = 10;
        duelConfig.MinimumCharacterLevel = 30;
        duelConfig.EntranceFee = EntranceFee;
        duelConfig.MaximumSpectatorsPerDuelRoom = 10;
        duelConfig.Exit = MakeGate(125, 125);

        var area = context.CreateNew<DuelArea>();
        area.Index = 0;
        area.FirstPlayerGate = MakeGate(100, 60);
        area.SecondPlayerGate = MakeGate(150, 60);
        area.SpectatorsGate = MakeGate(100, 70);
        duelConfig.DuelAreas.Add(area);
        gameConfig.DuelConfiguration = duelConfig;

        var mapInitializer = new MapInitializer(gameConfig, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var plugInManager = new PlugInManager(new List<PlugInConfiguration>(), new NullLoggerFactory(), null, null);
        var gameContext = new GameContext(gameConfig, contextProvider, mapInitializer, new NullLoggerFactory(), plugInManager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;

        return (gameContext, mapDef);
    }

    private async Task<Player> CreateReadyDuelistAsync(IGameContext gameContext)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        player.Attributes![Stats.Level] = 400;
        player.Money = StartMoney;

        // Put the player onto the (same) game map so the duel's same-map check passes.
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);
        await map!.AddAsync(player).ConfigureAwait(false);

        return player;
    }
}
