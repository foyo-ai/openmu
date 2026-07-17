// <copyright file="ItemStackAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Items;

using MUnique.OpenMU.GameLogic.Views.Inventory;

/// <summary>
/// Action to stack items.
/// </summary>
public class ItemStackAction
{
    private const int CombineFeePerTen = 500_000;
    private const int DismantleFee = 1_000_000;

    /// <summary>
    /// Stacks several items to one stacked item.
    /// </summary>
    /// <param name="player">The player that is stacking.</param>
    /// <param name="stackId">The id of the stacking.</param>
    /// <param name="stackSize">The size of the requested stack.</param>
    /// <param name="requireOpenedNpc">If set to <c>true</c>, the Lahap NPC must be opened (the normal in-game path).
    /// The <c>/pack</c> chat command passes <c>false</c> to allow packing without visiting the NPC.</param>
    /// <param name="chargeFee">If set to <c>true</c>, the combine fee is charged (the normal in-game path).</param>
    /// <returns><c>true</c> if the jewels were stacked; otherwise <c>false</c>.</returns>
    public async ValueTask<bool> StackItemsAsync(Player player, byte stackId, byte stackSize, bool requireOpenedNpc = true, bool chargeFee = true)
    {
        using var loggerScope = player.Logger.BeginScope(this.GetType());
        if (requireOpenedNpc && !this.IsCorrectNpcOpened(player))
        {
            return false;
        }

        var mix = this.GetJewelMix(stackId, player);
        if (mix is null)
        {
            return false;
        }

        if (player.Inventory is null)
        {
            return false;
        }

        var jewels = player.Inventory.Items.Where(item => item.Definition == mix.SingleJewel).Take(stackSize).ToList();
        if (jewels.Count != stackSize)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.YouLackOfJewels)).ConfigureAwait(false);
            return false;
        }

        if (chargeFee)
        {
            var fee = GetCombineFee(stackSize);
            if (!player.TryRemoveMoney(fee))
            {
                await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.NotEnoughMoney)).ConfigureAwait(false);
                return false;
            }
        }

        foreach (var jewel in jewels)
        {
            await player.Inventory.RemoveItemAsync(jewel).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(jewel.ItemSlot)).ConfigureAwait(false);
        }

        var stacked = player.PersistenceContext.CreateNew<Item>();
        stacked.Definition = mix.MixedJewel;
        stacked.Level = (byte)((stackSize / 10) - 1);
        stacked.Durability = 1;
        await player.Inventory.AddItemAsync(stacked).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(stacked)).ConfigureAwait(false);
        if (chargeFee)
        {
            await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Unstack the stacked item from the specified slot.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="stackId">The stack identifier.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="requireOpenedNpc">If set to <c>true</c>, the Lahap NPC must be opened (the normal in-game path);
    /// when it is not, the player is disconnected (anti-dupe). The <c>/unpack</c> chat command passes <c>false</c>.</param>
    /// <param name="chargeFee">If set to <c>true</c>, the dismantle fee is charged (the normal in-game path).</param>
    /// <returns><c>true</c> if the stacked jewel was unstacked; otherwise <c>false</c>.</returns>
    public async ValueTask<bool> UnstackItemsAsync(Player player, byte stackId, byte slot, bool requireOpenedNpc = true, bool chargeFee = true)
    {
        if (requireOpenedNpc && !this.IsCorrectNpcOpened(player))
        {
            await player.DisconnectAsync().ConfigureAwait(false);
            return false;
        }

        var mix = this.GetJewelMix(stackId, player);
        if (mix is null)
        {
            return false;
        }

        var stacked = player.Inventory?.GetItem(slot);
        if (stacked is null)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.StackedJewelNotFound)).ConfigureAwait(false);
            return false;
        }

        if (stacked.Definition != mix.MixedJewel)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.SelectedItemIsNotStackedJewel)).ConfigureAwait(false);
            return false;
        }

        if (chargeFee && !player.TryRemoveMoney(DismantleFee))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.NotEnoughMoney)).ConfigureAwait(false);
            return false;
        }

        byte pieces = (byte)((stacked.Level + 1) * 10);

        var freeSlots = player.Inventory!.FreeSlots.Take(pieces).ToList();
        if (freeSlots.Count < pieces)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.InventoryNotEnoughSpace)).ConfigureAwait(false);
            return false;
        }

        await player.Inventory.RemoveItemAsync(stacked).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(slot)).ConfigureAwait(false);
        foreach (var freeSlot in freeSlots)
        {
            var jewel = player.PersistenceContext.CreateNew<Item>();
            jewel.Definition = mix.SingleJewel;
            jewel.Durability = 1;
            jewel.ItemSlot = freeSlot;
            await player.Inventory.AddItemAsync(freeSlot, jewel).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(jewel)).ConfigureAwait(false);
        }

        if (chargeFee)
        {
            await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
        }

        return true;
    }

    private static int GetCombineFee(byte stackSize) => (stackSize / 10) * CombineFeePerTen;

    private bool IsCorrectNpcOpened(Player player)
    {
        if (player.OpenedNpc is null || player.OpenedNpc.Definition.NpcWindow != NpcWindow.Lahap)
        {
            player.Logger.LogWarning("Probably Hacker tried to Combine/Dismantle Jewels without talking to Lahap. Dupe Method. Acc: [{accountName}] Character: [{characterName}]", player.Account?.LoginName, player.SelectedCharacter?.Name);
            return false;
        }

        return true;
    }

    private JewelMix? GetJewelMix(byte mixId, Player player)
    {
        var mix = player.GameContext.Configuration.JewelMixes.FirstOrDefault(m => m.Number == mixId);
        if (mix is null)
        {
            player.Logger.LogWarning("Unknown mix type [{mixType}], Player Name: [{characterName}], Account Name: [{accountName}]", mixId, player.SelectedCharacter?.Name, player.Account?.LoginName);
        }

        return mix;
    }
}