// <copyright file="ItemBankChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Composition;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the <c>/bank</c> command: an account-wide item bank
/// that keeps counts of configured items as plain numbers, so players are not limited by
/// inventory or vault slots. Which items are bankable is configured in the admin panel by
/// picking the item (searchable, with image) and giving it a short command alias.
/// <list type="bullet">
///   <item><c>/bank</c> shows the current balances.</item>
///   <item><c>/bank deposit &lt;alias&gt; &lt;amount&gt;</c> deposits items from the inventory.</item>
///   <item><c>/bank withdraw &lt;alias&gt; &lt;amount&gt;</c> withdraws items to the inventory.</item>
/// </list>
/// </summary>
[Guid("B2E7F4A1-6C3D-4E82-9A5B-1F0C7D8E2A45")]
[PlugIn]
[Display(Name = "Item Bank Command", Description = "Handles the /bank command for the account-wide, configurable item bank (deposit/withdraw/show).")]
[ChatCommandHelp(Command, typeof(ItemBankChatCommandPlugIn.Arguments), CharacterStatus.Normal)]
public class ItemBankChatCommandPlugIn : ChatCommandPlugInBase<ItemBankChatCommandPlugIn.Arguments>,
    ISupportCustomConfiguration<ItemBankChatCommandPlugIn.ItemBankConfiguration>,
    ISupportDefaultCustomConfiguration
{
    private const string Command = "/bank";

    /// <inheritdoc />
    public ItemBankConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public object CreateDefaultConfig() => new ItemBankConfiguration();

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, Arguments arguments)
    {
        if (player.SelectedCharacter is null || player.Account is null || player.Inventory is null)
        {
            return;
        }

        var config = this.Configuration ??= new ItemBankConfiguration();

        // No action: just show the balances.
        if (string.IsNullOrWhiteSpace(arguments.Action))
        {
            await this.ShowBalancesAsync(player, config).ConfigureAwait(false);
            return;
        }

        var bankable = config.Items.FirstOrDefault(i => string.Equals(i.Alias, arguments.Item, StringComparison.OrdinalIgnoreCase));
        if (bankable is null)
        {
            await ShowUsageAsync(player, config).ConfigureAwait(false);
            return;
        }

        var definition = ResolveDefinition(player, bankable);
        if (definition is null)
        {
            await player.ShowBlueMessageAsync($"The item '{bankable.Alias}' is not configured correctly.").ConfigureAwait(false);
            return;
        }

        if (arguments.Amount <= 0)
        {
            await player.ShowBlueMessageAsync("Invalid amount.").ConfigureAwait(false);
            return;
        }

        switch (arguments.Action.ToLowerInvariant())
        {
            case "deposit":
            case "in":
            case "add":
                await this.DepositAsync(player, definition, arguments.Amount).ConfigureAwait(false);
                break;
            case "withdraw":
            case "out":
            case "take":
                await this.WithdrawAsync(player, definition, arguments.Amount).ConfigureAwait(false);
                break;
            default:
                await ShowUsageAsync(player, config).ConfigureAwait(false);
                break;
        }
    }

    private static async ValueTask ShowUsageAsync(Player player, ItemBankConfiguration config)
    {
        var aliases = string.Join(", ", config.Items.Where(i => !string.IsNullOrWhiteSpace(i.Alias)).Select(i => i.Alias));
        await player.ShowBlueMessageAsync("Usage: /bank | /bank deposit <item> <amount> | /bank withdraw <item> <amount>.").ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"Items: {aliases}").ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the game item definition for a configured bankable item, using the server's item
    /// list so it matches the definition instances used by inventory items.
    /// </summary>
    private static ItemDefinition? ResolveDefinition(Player player, BankableItem bankable)
    {
        if (bankable.Item is null)
        {
            return null;
        }

        return player.GameContext.Configuration.Items.FirstOrDefault(i => i.Group == bankable.Item.Group && i.Number == bankable.Item.Number);
    }

    private static int GetBalance(Account account, ItemDefinition definition)
    {
        return account.GetItemBankCount(definition.Group, definition.Number);
    }

    private static void AddBalance(Player player, ItemDefinition definition, int delta)
    {
        player.AddToItemBank(definition.Group, definition.Number, delta);
    }

    private async ValueTask ShowBalancesAsync(Player player, ItemBankConfiguration config)
    {
        var account = player.Account!;
        var configured = config.Items.Where(i => i.Item is not null && !string.IsNullOrWhiteSpace(i.Alias)).ToList();
        if (configured.Count == 0)
        {
            await player.ShowBlueMessageAsync("The item bank has no configured items.").ConfigureAwait(false);
            return;
        }

        var parts = configured.Select(i => $"{i.Item!.Name}: {GetBalance(account, i.Item!)}");
        await player.ShowBlueMessageAsync("[Item Bank] " + string.Join("  ", parts)).ConfigureAwait(false);
    }

    private async ValueTask DepositAsync(Player player, ItemDefinition definition, int amount)
    {
        var items = player.Inventory!.Items.Where(i => i.Definition == definition).Take(amount).ToList();
        if (items.Count == 0)
        {
            await player.ShowBlueMessageAsync($"You have no {definition.Name} in your inventory.").ConfigureAwait(false);
            return;
        }

        foreach (var toDeposit in items)
        {
            await player.Inventory.RemoveItemAsync(toDeposit).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IItemRemovedPlugIn>(p => p.RemoveItemAsync(toDeposit.ItemSlot)).ConfigureAwait(false);
        }

        AddBalance(player, definition, items.Count);
        await player.ShowBlueMessageAsync($"Deposited {items.Count} {definition.Name}. Balance: {GetBalance(player.Account!, definition)}.").ConfigureAwait(false);
    }

    private async ValueTask WithdrawAsync(Player player, ItemDefinition definition, int amount)
    {
        var balance = GetBalance(player.Account!, definition);
        if (balance <= 0)
        {
            await player.ShowBlueMessageAsync($"Your {definition.Name} balance is 0.").ConfigureAwait(false);
            return;
        }

        var requested = Math.Min(amount, balance);
        var given = 0;
        for (var i = 0; i < requested; i++)
        {
            var newItem = player.PersistenceContext.CreateNew<Item>();
            newItem.Definition = definition;
            newItem.Durability = 1;
            newItem.Level = 0;
            if (!await player.Inventory!.AddItemAsync(newItem).ConfigureAwait(false))
            {
                // Inventory is full: discard the just-created item and stop.
                await player.PersistenceContext.DeleteAsync(newItem).ConfigureAwait(false);
                break;
            }

            await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(newItem)).ConfigureAwait(false);
            given++;
        }

        if (given == 0)
        {
            await player.ShowBlueMessageAsync("No free inventory space to withdraw.").ConfigureAwait(false);
            return;
        }

        AddBalance(player, definition, -given);

        if (given < requested)
        {
            await player.ShowBlueMessageAsync($"Not enough inventory space, only withdrew {given} {definition.Name}. Balance: {GetBalance(player.Account!, definition)}.").ConfigureAwait(false);
        }
        else
        {
            await player.ShowBlueMessageAsync($"Withdrew {given} {definition.Name}. Balance: {GetBalance(player.Account!, definition)}.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Arguments for the <c>/bank</c> command. All are optional and parsed positionally.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the action: empty to show balances, or deposit/withdraw.
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the item alias (as configured), e.g. bless, soul, chaos, life.
        /// </summary>
        public string? Item { get; set; }

        /// <summary>
        /// Gets or sets the amount of items to deposit or withdraw.
        /// </summary>
        public int Amount { get; set; }
    }

    /// <summary>
    /// The admin-configurable configuration of the item bank.
    /// </summary>
    public class ItemBankConfiguration
    {
        /// <summary>
        /// Gets or sets the list of item types that can be stored in the bank.
        /// </summary>
        /// <remarks>
        /// Marked <see cref="MemberOfAggregateAttribute"/> + <see cref="ScaffoldColumnAttribute"/> so the admin
        /// panel renders it as an inline-editable table (edit each row's fields, add/remove rows).
        /// </remarks>
        [MemberOfAggregate]
        [ScaffoldColumn(true)]
        [Display(Name = "Bankable items", Description = "The item types that players can deposit/withdraw with the /bank command.")]
        public ICollection<BankableItem> Items { get; set; } = new List<BankableItem>();
    }

    /// <summary>
    /// One bankable item type, configured in the admin panel.
    /// </summary>
    public class BankableItem
    {
        /// <summary>
        /// Gets or sets the alias used as the keyword in the /bank command (e.g. "bless").
        /// </summary>
        [Display(Name = "Alias", Description = "Keyword used in the /bank command, e.g. 'bless'.")]
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the bankable item. Selected from a searchable list (shows the item image) in the admin panel.
        /// </summary>
        [Display(Name = "Item", Description = "The item that can be banked. Search by name; the item image is shown.")]
        public ItemDefinition? Item { get; set; }

        /// <inheritdoc />
        public override string ToString() => $"{this.Alias}: {this.Item?.Name ?? "(no item selected)"}";
    }
}
