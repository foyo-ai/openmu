// <copyright file="ItemBankDataChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.ItemBank;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the <c>/bankdata</c> chat command: a quiet, machine-readable request that the jewel bank
/// window sends to fetch the account's item bank balances. It produces no chat output; it only sends
/// the <see cref="IShowItemBankBalancesPlugIn"/> packet. Only the updated client emits this command,
/// so older clients never receive the (new) balances packet.
/// </summary>
[Guid("E1B6A93C-4D2F-4A78-9C05-7B3E1F2D8A64")]
[PlugIn]
[Display(Name = "Item Bank Data Command", Description = "Handles the quiet /bankdata command which sends the item bank balances to the jewel bank window.")]
[ChatCommandHelp(Command, typeof(ItemBankDataChatCommandPlugIn.EmptyArguments), CharacterStatus.Normal)]
public class ItemBankDataChatCommandPlugIn : ChatCommandPlugInBase<ItemBankDataChatCommandPlugIn.EmptyArguments>
{
    private const string Command = "/bankdata";

    private const string BankCommandKey = "/bank";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyArguments arguments)
    {
        if (player.Account is null)
        {
            return;
        }

        var bankPlugin = player.GameContext.PlugInManager?.GetStrategy<IChatCommandPlugIn>(BankCommandKey) as ItemBankChatCommandPlugIn;
        var items = bankPlugin?.Configuration?.Items;
        if (items is null)
        {
            return;
        }

        var balances = new List<ItemBankBalance>();
        foreach (var item in items)
        {
            if (item.Item is null || string.IsNullOrWhiteSpace(item.Alias))
            {
                continue;
            }

            var count = player.Account.GetItemBankCount(item.Item.Group, item.Item.Number);
            balances.Add(new ItemBankBalance(item.Item.Group, item.Item.Number, count, item.Alias));
        }

        await player.InvokeViewPlugInAsync<IShowItemBankBalancesPlugIn>(p => p.ShowItemBankBalancesAsync(balances)).ConfigureAwait(false);
    }

    /// <summary>
    /// The (empty) arguments of the <c>/bankdata</c> command.
    /// </summary>
    public class EmptyArguments : ArgumentsBase
    {
    }
}
