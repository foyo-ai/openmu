// <copyright file="BuyRequestAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Action to buy an item from another player shop.
/// </summary>
public class BuyRequestAction
{
    private readonly CloseStoreAction _closeStoreAction = new();

    /// <summary>
    /// Buys the item from another player shop.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="requestedPlayer">The requested player.</param>
    /// <param name="slot">The slot.</param>
    public async ValueTask BuyItemAsync(Player player, Player requestedPlayer, byte slot)
    {
        using var loggerScope = player.Logger.BeginScope(this.GetType());
        if (requestedPlayer.IsTemplatePlayer || player.IsTemplatePlayer)
        {
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.ItemBlock, null)).ConfigureAwait(false);
            return;
        }

        if (!(requestedPlayer.ShopStorage?.StoreOpen ?? false))
        {
            player.Logger.LogDebug("Store not open, Character {0}", requestedPlayer.SelectedCharacter?.Name);
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.ShopNotOpened, null)).ConfigureAwait(false);
            return;
        }

        if (slot < InventoryConstants.FirstStoreItemSlotIndex)
        {
            player.Logger.LogWarning("Store Slot too low: {0}, possible hacker", slot);
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.InvalidShopSlot, null)).ConfigureAwait(false);
            return;
        }

        var item = requestedPlayer.ShopStorage.GetItem(slot);
        if (item?.StorePrice is null)
        {
            player.Logger.LogDebug("Item unavailable, Slot {0}", slot);
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.NameMismatchOrPriceMissing, null)).ConfigureAwait(false);
            return;
        }

        var itemPrice = item.StorePrice.Value;

        // The store is either priced in Zen (default) or in a jewel (a bankable item), in which case the
        // price is the number of that jewel, paid from the buyer's inventory first and then his bank.
        var jewelCurrency = GetJewelCurrency(requestedPlayer.SelectedCharacter);

        var canAfford = jewelCurrency is { } currency
            ? GetBuyerAvailableJewelCount(player, currency.Group, currency.Number) >= itemPrice
            : player.Money >= itemPrice;
        if (!canAfford)
        {
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.LackOfMoney, null)).ConfigureAwait(false);
            return;
        }

        // Check Inv Space
        var freeslot = player.Inventory?.CheckInvSpace(item);
        if (freeslot is null)
        {
            await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.MoneyOverflowOrNotEnoughSpace, null)).ConfigureAwait(false);
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.InventoryNotEnoughSpace)).ConfigureAwait(false);
            return;
        }

        bool itemSold = false;
        using (await requestedPlayer.ShopStorage.StoreLock.LockAsync())
        {
            if (!requestedPlayer.ShopStorage.StoreOpen)
            {
                player.Logger.LogDebug("Store not open anymore, Character {0}", requestedPlayer.SelectedCharacter?.Name);
                await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.ShopNotOpened, null)).ConfigureAwait(false);
                return;
            }

            item = requestedPlayer.ShopStorage.GetItem(slot);
            if (item is null)
            {
                await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.InvalidShopSlot, null)).ConfigureAwait(false);
                return;
            }

            player.Logger.LogDebug("BuyRequest, Item Price: {0}", itemPrice);

            bool paid;
            if (jewelCurrency is { } payCurrency)
            {
                paid = await this.TryChargeBuyerJewelsAsync(player, payCurrency.Group, payCurrency.Number, itemPrice).ConfigureAwait(false);
                if (paid)
                {
                    // The seller always receives the jewels into his bank (no inventory space needed).
                    requestedPlayer.AddToItemBank(payCurrency.Group, payCurrency.Number, itemPrice);
                }
                else
                {
                    await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.LackOfMoney, null)).ConfigureAwait(false);
                }
            }
            else
            {
                paid = false;
                if (player.TryRemoveMoney(itemPrice))
                {
                    if (requestedPlayer.TryAddMoney(itemPrice))
                    {
                        paid = true;
                    }
                    else
                    {
                        await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.MoneyOverflowOrNotEnoughSpace, null)).ConfigureAwait(false);
                        await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.SellerInventoryFull)).ConfigureAwait(false);
                        player.TryAddMoney(itemPrice);
                    }
                }
            }

            if (paid)
            {
                using var itemContext = requestedPlayer.GameContext.PersistenceContextProvider.CreateNewTradeContext();
                itemContext.Attach(item);
                await requestedPlayer.ShopStorage.RemoveItemAsync(item).ConfigureAwait(false);
                await requestedPlayer.InvokeViewPlugInAsync<IItemSoldByPlayerShopPlugIn>(p => p.ItemSoldByPlayerShopAsync(slot, player)).ConfigureAwait(false);
                await requestedPlayer.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(slot)).ConfigureAwait(false);
                item.ItemSlot = (byte)freeslot;
                item.StorePrice = null;
                await player.Inventory!.AddItemAsync(item).ConfigureAwait(false);
                requestedPlayer.PersistenceContext.Detach(item);
                await itemContext.SaveChangesAsync().ConfigureAwait(false);
                player.PersistenceContext.Attach(item);
                await player.InvokeViewPlugInAsync<IPlayerShopBuyRequestResultPlugIn>(p => p.ShowResultAsync(requestedPlayer, ItemBuyResult.Success, item)).ConfigureAwait(false);

                if (jewelCurrency is { } soldCurrency)
                {
                    await this.SendSaleLetterAsync(requestedPlayer, player, item, itemPrice, soldCurrency).ConfigureAwait(false);
                }
                else
                {
                    await requestedPlayer.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
                    await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
                }

                itemSold = true;

                player.GameContext.PlugInManager.GetPlugInPoint<IItemSoldToOtherPlayerPlugIn>()?.ItemSold(requestedPlayer, item, player);
            }
        }

        if (itemSold)
        {
            if (requestedPlayer.ShopStorage.Items.Any())
            {
                // this update may be sent to other players as well which are currently looking at the store
                await player.InvokeViewPlugInAsync<Views.PlayerShop.IShowShopItemListPlugIn>(p => p.ShowShopItemListAsync(requestedPlayer, true)).ConfigureAwait(false);
            }
            else
            {
                await this._closeStoreAction.CloseStoreAsync(requestedPlayer).ConfigureAwait(false);
            }
        }
    }

    private static (byte Group, short Number)? GetJewelCurrency(Character? character)
    {
        return character?.StoreCurrencyItemGroup is { } group && character.StoreCurrencyItemNumber is { } number
            ? (group, number)
            : null;
    }

    private static int GetBuyerAvailableJewelCount(Player buyer, byte group, short number)
    {
        var inInventory = buyer.Inventory?.Items.Count(i => i.Definition?.Group == group && i.Definition.Number == number) ?? 0;
        var inBank = buyer.Account?.GetItemBankCount(group, number) ?? 0;
        return inInventory + inBank;
    }

    /// <summary>
    /// Charges the buyer the given amount of a jewel: from his inventory first, then his bank.
    /// Returns false (charging nothing) if the buyer does not have enough in total.
    /// </summary>
    private async ValueTask<bool> TryChargeBuyerJewelsAsync(Player buyer, byte group, short number, int amount)
    {
        var inventoryJewels = buyer.Inventory!.Items
            .Where(i => i.Definition?.Group == group && i.Definition.Number == number)
            .Take(amount)
            .ToList();
        var bankCount = buyer.Account!.GetItemBankCount(group, number);
        if (inventoryJewels.Count + bankCount < amount)
        {
            return false;
        }

        foreach (var jewel in inventoryJewels)
        {
            var jewelSlot = jewel.ItemSlot;
            await buyer.Inventory.RemoveItemAsync(jewel).ConfigureAwait(false);
            await buyer.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(jewelSlot)).ConfigureAwait(false);
        }

        var remainderFromBank = amount - inventoryJewels.Count;
        if (remainderFromBank > 0)
        {
            buyer.AddToItemBank(group, number, -remainderFromBank);
        }

        return true;
    }

    /// <summary>
    /// Sends a system letter to the seller informing about a jewel sale (item, count, buyer, time).
    /// Persisted so an offline (off-store) seller receives it on next login, and forwarded if online.
    /// </summary>
    private async ValueTask SendSaleLetterAsync(Player seller, Player buyer, Item item, int price, (byte Group, short Number) currency)
    {
        var sellerName = seller.SelectedCharacter?.Name;
        if (string.IsNullOrEmpty(sellerName))
        {
            return;
        }

        var config = seller.GameContext.Configuration;
        var currencyName = config.Items.FirstOrDefault(i => i.Group == currency.Group && i.Number == currency.Number)?.Name ?? "jewels";
        var itemName = item.Definition?.Name ?? "an item";
        var buyerName = buyer.SelectedCharacter?.Name ?? "someone";
        var subject = "Item sold";
        var message = $"{buyerName} bought {itemName} for {price} {currencyName} on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. The {currencyName} were added to your jewel bank.";

        LetterHeader letter;
        try
        {
            using var context = seller.GameContext.PersistenceContextProvider.CreateNewPlayerContext(config);
            var header = context.CreateNew<LetterHeader>();
            header.LetterDate = DateTime.UtcNow;
            header.SenderName = "System";
            header.ReceiverName = sellerName;
            header.Subject = subject;

            var body = context.CreateNew<LetterBody>();
            body.Header = header;
            body.Message = message;
            body.SenderAppearance = context.CreateNew<AppearanceData>();
            body.SenderAppearance.CharacterClass = buyer.AppearanceData.CharacterClass;
            body.Rotation = 0;
            body.Animation = 0;

            if (!await context.CanSaveLetterAsync(header).ConfigureAwait(false))
            {
                return;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            letter = header;
        }
        catch (Exception ex)
        {
            seller.Logger.LogError(ex, "Failed to send sale letter to seller {0}", sellerName);
            return;
        }

        if ((seller.GameContext as IGameServerContext)?.FriendServer is { } friendServer)
        {
            await friendServer.ForwardLetterAsync(letter).ConfigureAwait(false);
        }
    }
}
