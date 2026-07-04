// <copyright file="OffStoreTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tests the offline store flow (/offstore) headlessly, without a game client:
/// a seller opens a store, transitions to an offline ghost, a buyer purchases
/// the item from the ghost, and the ghost session stops after selling out.
/// </summary>
[TestFixture]
public class OffStoreTest
{
    private const int ItemPrice = 5_000;
    private const int BuyerStartMoney = 1_000_000;
    private const string SellerLogin = "offseller";

    /// <summary>
    /// Tests the whole offline store round trip: start, restore of the store on the ghost,
    /// a successful purchase from the ghost, and the automatic session stop after sell-out.
    /// </summary>
    [Test]
    public async Task OfflineStoreSellsItemAndStopsWhenSoldOut()
    {
        var gameContext = CreateGameContext();
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);

        // 1) The seller puts one item into the store, sets a price and opens the store.
        var seller = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        seller.Account!.LoginName = SellerLogin;
        var sellerCharacter = seller.SelectedCharacter!;
        await map!.AddAsync(seller).ConfigureAwait(false);

        var item = CreateItem();
        var storeSlot = InventoryConstants.FirstStoreItemSlotIndex;
        Assert.That(await seller.ShopStorage!.AddItemAsync(storeSlot, item), Is.True, "item should be placed into the store");
        item.StorePrice = ItemPrice;
        await new OpenStoreAction().OpenStoreAsync(seller, "TestStore");
        Assert.That(seller.ShopStorage.StoreOpen, Is.True, "store should be open before going offline");

        // 2) The seller goes offline; a store ghost replaces them (this is what /offstore triggers).
        var manager = gameContext.OfflinePlayerManager;
        var started = await manager.StartAsync(seller, SellerLogin, OfflinePlayerMode.Store);

        Assert.That(started, Is.True, "offline store session should start");
        Assert.That(manager.IsActive(SellerLogin), Is.True);
        Assert.That(manager.TryGetPlayer(SellerLogin, out var ghost), Is.True);
        Assert.That(ghost!.Mode, Is.EqualTo(OfflinePlayerMode.Store));
        Assert.That(ghost.ShopStorage!.StoreOpen, Is.True, "the ghost should have restored the open store");

        // 3) A buyer purchases the item from the ghost's store.
        var buyer = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        buyer.Money = BuyerStartMoney;
        await map.AddAsync(buyer).ConfigureAwait(false);

        await new BuyRequestAction().BuyItemAsync(buyer, ghost, storeSlot);

        Assert.That(buyer.Money, Is.EqualTo(BuyerStartMoney - ItemPrice), "the price should be deducted from the buyer");
        Assert.That(buyer.Inventory!.Items, Does.Contain(item), "the item should be in the buyer's inventory");

        // The ghost logs itself out after selling out, so the money is read from the character.
        Assert.That(sellerCharacter.Inventory!.Money, Is.EqualTo(ItemPrice), "the seller should have received the money");

        // 4) The store sold out, so the ghost session must have stopped itself.
        Assert.That(manager.IsActive(SellerLogin), Is.False, "the ghost should stop after selling out");
    }

    /// <summary>
    /// Tests that a store ghost is not started when the store is not open,
    /// which is the guard the /offstore command relies on.
    /// </summary>
    [Test]
    public async Task StoreGhostFailsWithoutOpenStore()
    {
        var gameContext = CreateGameContext();
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);

        var seller = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        seller.Account!.LoginName = SellerLogin;
        await map!.AddAsync(seller).ConfigureAwait(false);
        Assert.That(seller.ShopStorage!.StoreOpen, Is.False);

        var started = await gameContext.OfflinePlayerManager.StartAsync(seller, SellerLogin, OfflinePlayerMode.Store);

        Assert.That(started, Is.False, "a store ghost without an open store should not start");
        Assert.That(gameContext.OfflinePlayerManager.IsActive(SellerLogin), Is.False);
    }

    private static IGameContext CreateGameContext()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        var context = contextProvider.CreateNewContext();
        var gameConfig = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameConfiguration>();

        var mapDef = context.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameMapDefinition>();
        mapDef.Number = 0;
        mapDef.TerrainData = new byte[ushort.MaxValue + 3];
        gameConfig.Maps.Add(mapDef);
        gameConfig.RecoveryInterval = int.MaxValue;
        gameConfig.MaximumInventoryMoney = int.MaxValue;

        var mapInitializer = new MapInitializer(gameConfig, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var plugInManager = new PlugInManager(new List<PlugInConfiguration>(), new NullLoggerFactory(), null, null);
        var gameContext = new GameContext(gameConfig, contextProvider, mapInitializer, new NullLoggerFactory(), plugInManager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;

        return gameContext;
    }

    private static Item CreateItem()
    {
        var item = new Mock<Item>();
        item.SetupAllProperties();
        item.Object.Definition = new ItemDefinition { Width = 1, Height = 1 };
        item.Setup(i => i.ItemOptions).Returns(new List<ItemOptionLink>());
        return item.Object;
    }
}
