// <copyright file="JewelStoreBuyTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tests buying from a personal store that is priced in a jewel (a bankable item):
/// the buyer is charged from his inventory first and then his bank, and the seller
/// always receives the jewels into his bank.
/// </summary>
[TestFixture]
public class JewelStoreBuyTest
{
    private const byte JewelGroup = 14;
    private const short JewelNumber = 13;
    private const int JewelPrice = 5;

    /// <summary>
    /// A buyer with 3 jewels in the inventory and 10 in the bank buys an item priced at 5 jewels:
    /// the 3 inventory jewels are consumed first, the remaining 2 come from the bank, and the seller's
    /// bank is credited the full 5.
    /// </summary>
    [Test]
    public async Task BuyTakesFromInventoryThenBankAndCreditsSellerBank()
    {
        var gameContext = CreateGameContext();
        var jewelDefinition = CreateJewelDefinition();
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);

        var (seller, soldItem, storeSlot) = await this.CreateJewelStoreSellerAsync(gameContext, map!).ConfigureAwait(false);

        var buyer = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        await map!.AddAsync(buyer).ConfigureAwait(false);
        for (var i = 0; i < 3; i++)
        {
            await buyer.Inventory!.AddItemAsync(CreateItem(jewelDefinition)).ConfigureAwait(false);
        }

        buyer.AddToItemBank(JewelGroup, JewelNumber, 10);

        await new BuyRequestAction().BuyItemAsync(buyer, seller, storeSlot);

        Assert.That(CountInventoryJewels(buyer), Is.EqualTo(0), "the 3 inventory jewels should be consumed first");
        Assert.That(buyer.Account!.GetItemBankCount(JewelGroup, JewelNumber), Is.EqualTo(8), "the remaining 2 should be taken from the buyer's bank");
        Assert.That(seller.Account!.GetItemBankCount(JewelGroup, JewelNumber), Is.EqualTo(JewelPrice), "the seller should receive the jewels into his bank");
        Assert.That(buyer.Inventory!.Items, Does.Contain(soldItem), "the item should be delivered to the buyer");
    }

    /// <summary>
    /// When the buyer's inventory + bank jewels are fewer than the price, the purchase is cancelled and
    /// nothing changes on either side.
    /// </summary>
    [Test]
    public async Task BuyIsCancelledWhenNotEnoughJewels()
    {
        var gameContext = CreateGameContext();
        var jewelDefinition = CreateJewelDefinition();
        var map = await gameContext.GetMapAsync(0).ConfigureAwait(false);

        var (seller, soldItem, storeSlot) = await this.CreateJewelStoreSellerAsync(gameContext, map!).ConfigureAwait(false);

        var buyer = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        await map!.AddAsync(buyer).ConfigureAwait(false);
        await buyer.Inventory!.AddItemAsync(CreateItem(jewelDefinition)).ConfigureAwait(false); // 1 in inventory
        buyer.AddToItemBank(JewelGroup, JewelNumber, 1); // + 1 in bank = 2 (< price 5)

        await new BuyRequestAction().BuyItemAsync(buyer, seller, storeSlot);

        Assert.That(CountInventoryJewels(buyer), Is.EqualTo(1), "inventory jewels should be untouched");
        Assert.That(buyer.Account!.GetItemBankCount(JewelGroup, JewelNumber), Is.EqualTo(1), "bank jewels should be untouched");
        Assert.That(seller.Account!.GetItemBankCount(JewelGroup, JewelNumber), Is.EqualTo(0), "the seller should receive nothing");
        Assert.That(buyer.Inventory!.Items, Does.Not.Contain(soldItem), "the item should not be delivered");
        Assert.That(seller.ShopStorage!.GetItem(storeSlot), Is.EqualTo(soldItem), "the item should remain in the store");
    }

    private static int CountInventoryJewels(Player player)
    {
        return player.Inventory!.Items.Count(i => i.Definition?.Group == JewelGroup && i.Definition.Number == JewelNumber);
    }

    private async Task<(Player Seller, Item SoldItem, byte StoreSlot)> CreateJewelStoreSellerAsync(IGameContext gameContext, MUnique.OpenMU.GameLogic.GameMap map)
    {
        var seller = await PlayerTestHelper.CreatePlayerAsync(gameContext);
        var sellerCharacter = seller.SelectedCharacter!;
        sellerCharacter.StoreCurrencyItemGroup = JewelGroup;
        sellerCharacter.StoreCurrencyItemNumber = JewelNumber;
        await map.AddAsync(seller).ConfigureAwait(false);

        var soldItem = CreateItem(new ItemDefinition { Width = 1, Height = 1 });
        var storeSlot = InventoryConstants.FirstStoreItemSlotIndex;
        await seller.ShopStorage!.AddItemAsync(storeSlot, soldItem).ConfigureAwait(false);
        soldItem.StorePrice = JewelPrice;
        await new OpenStoreAction().OpenStoreAsync(seller, "JewelStore");

        return (seller, soldItem, storeSlot);
    }

    private static ItemDefinition CreateJewelDefinition()
    {
        return new ItemDefinition
        {
            Group = JewelGroup,
            Number = JewelNumber,
            Name = "Jewel of Bless",
            Width = 1,
            Height = 1,
        };
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

    private static Item CreateItem(ItemDefinition definition)
    {
        var item = new Mock<Item>();
        item.SetupAllProperties();
        item.Object.Definition = definition;
        item.Setup(i => i.ItemOptions).Returns(new List<ItemOptionLink>());
        return item.Object;
    }
}
