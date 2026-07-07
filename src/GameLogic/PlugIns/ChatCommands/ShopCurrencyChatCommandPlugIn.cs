// <copyright file="ShopCurrencyChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using System.ComponentModel.DataAnnotations;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the <c>/shopcurrency</c> chat command, which selects the currency the player's personal
/// store is priced in: either Zen (default) or one of the configured bankable items (a jewel). When a
/// jewel is chosen, item prices in the store are counts of that jewel, paid from the buyer's inventory
/// then bank and credited to the seller's bank.
/// </summary>
[Guid("C4A9F0E2-3B7D-4E61-9A28-6D1F0C8E5B33")]
[PlugIn]
[Display(Name = "Shop Currency Command", Description = "Handles the /shopcurrency command to price a personal store in a jewel (or Zen).")]
[ChatCommandHelp(Command, typeof(ShopCurrencyChatCommandPlugIn.Arguments), CharacterStatus.Normal)]
public class ShopCurrencyChatCommandPlugIn : ChatCommandPlugInBase<ShopCurrencyChatCommandPlugIn.Arguments>
{
    private const string Command = "/shopcurrency";

    private const string BankCommandKey = "/bank";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, Arguments arguments)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        if (player.ShopStorage?.StoreOpen ?? false)
        {
            await player.ShowBlueMessageAsync("Close your store before changing its currency.").ConfigureAwait(false);
            return;
        }

        var bankItems = this.GetBankableItems(player);

        if (string.IsNullOrWhiteSpace(arguments.Currency)
            || string.Equals(arguments.Currency, "zen", StringComparison.OrdinalIgnoreCase))
        {
            character.StoreCurrencyItemGroup = null;
            character.StoreCurrencyItemNumber = null;
            await player.ShowBlueMessageAsync("Your store is now priced in Zen.").ConfigureAwait(false);
            return;
        }

        var item = bankItems.FirstOrDefault(i => string.Equals(i.Alias, arguments.Currency, StringComparison.OrdinalIgnoreCase));
        if (item?.Item is null)
        {
            var aliases = string.Join(", ", bankItems.Where(i => i.Item is not null && !string.IsNullOrWhiteSpace(i.Alias)).Select(i => i.Alias));
            await player.ShowBlueMessageAsync("Usage: /shopcurrency <jewel|zen>.").ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"Jewels: {aliases}").ConfigureAwait(false);
            return;
        }

        character.StoreCurrencyItemGroup = item.Item.Group;
        character.StoreCurrencyItemNumber = item.Item.Number;
        await player.ShowBlueMessageAsync($"Your store is now priced in {item.Item.Name}. Prices are the number of {item.Item.Name}.").ConfigureAwait(false);
    }

    private IEnumerable<ItemBankChatCommandPlugIn.BankableItem> GetBankableItems(Player player)
    {
        var bankPlugin = player.GameContext.PlugInManager?.GetStrategy<IChatCommandPlugIn>(BankCommandKey) as ItemBankChatCommandPlugIn;
        return bankPlugin?.Configuration?.Items ?? Enumerable.Empty<ItemBankChatCommandPlugIn.BankableItem>();
    }

    /// <summary>
    /// Arguments for the <c>/shopcurrency</c> command.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the currency: a bankable-item alias (e.g. bless) or "zen".
        /// </summary>
        public string? Currency { get; set; }
    }
}
