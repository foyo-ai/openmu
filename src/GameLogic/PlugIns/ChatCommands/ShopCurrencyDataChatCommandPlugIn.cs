// <copyright file="ShopCurrencyDataChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.PlayerShop;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the quiet <c>/shopcurdata</c> command: the shop currency selector sends it to fetch the
/// character's current store currency. Produces no chat output; it only sends the ShopCurrency packet.
/// </summary>
[Guid("D9C4B0A1-72E5-4F38-8B6C-1A0E5D3F9C22")]
[PlugIn]
[Display(Name = "Shop Currency Data Command", Description = "Handles the quiet /shopcurdata command which sends the current store currency to the selector.")]
[ChatCommandHelp(Command, typeof(ShopCurrencyDataChatCommandPlugIn.EmptyArguments), CharacterStatus.Normal)]
public class ShopCurrencyDataChatCommandPlugIn : ChatCommandPlugInBase<ShopCurrencyDataChatCommandPlugIn.EmptyArguments>
{
    private const string Command = "/shopcurdata";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Pushes the character's current store currency to the client (group 0xFF / number -1 = Zen).
    /// Shared by this command and <see cref="ShopCurrencyChatCommandPlugIn"/>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="character">The selected character.</param>
    internal static async ValueTask SendCurrentAsync(Player player, Character character)
    {
        var group = character.StoreCurrencyItemGroup;
        var number = character.StoreCurrencyItemNumber;
        var isZen = group is null || number is null;
        await player.InvokeViewPlugInAsync<IShowShopCurrencyPlugIn>(
            p => p.ShowShopCurrencyAsync(
                isZen ? (byte)0xFF : group!.Value,
                isZen ? (short)-1 : number!.Value)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyArguments arguments)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        await SendCurrentAsync(player, character).ConfigureAwait(false);
    }

    /// <summary>
    /// The (empty) arguments of the <c>/shopcurdata</c> command.
    /// </summary>
    public class EmptyArguments : ArgumentsBase
    {
    }
}
