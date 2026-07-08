// <copyright file="ShopCurrencyTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Moq;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Views.PlayerShop;
using NUnit.Framework;

/// <summary>
/// Tests for the <see cref="ShopCurrencyChatCommandPlugIn"/> and <see cref="ShopCurrencyDataChatCommandPlugIn"/>.
/// </summary>
[TestFixture]
public class ShopCurrencyTest
{
    /// <summary>
    /// Setting the currency to Zen clears the character fields and pushes the ShopCurrency packet
    /// with the Zen sentinel (group 0xFF, number -1).
    /// </summary>
    [Test]
    public async Task ZenClearsFieldsAndPushes()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        player.SelectedCharacter!.StoreCurrencyItemGroup = 14;
        player.SelectedCharacter!.StoreCurrencyItemNumber = 13;

        var plugin = new ShopCurrencyChatCommandPlugIn();
        await plugin.HandleCommandAsync(player, "/shopcurrency zen").ConfigureAwait(false);

        Assert.That(player.SelectedCharacter!.StoreCurrencyItemGroup, Is.Null);
        Assert.That(player.SelectedCharacter!.StoreCurrencyItemNumber, Is.Null);

        var view = Mock.Get(player.ViewPlugIns.GetPlugIn<IShowShopCurrencyPlugIn>()!);
        view.Verify(v => v.ShowShopCurrencyAsync((byte)0xFF, (short)-1), Times.Once);
    }

    /// <summary>
    /// The quiet /shopcurdata command pushes the character's current currency (a jewel) to the client.
    /// </summary>
    [Test]
    public async Task DataCommandPushesCurrentJewelCurrency()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        player.SelectedCharacter!.StoreCurrencyItemGroup = 14;
        player.SelectedCharacter!.StoreCurrencyItemNumber = 13;

        var plugin = new ShopCurrencyDataChatCommandPlugIn();
        await plugin.HandleCommandAsync(player, "/shopcurdata").ConfigureAwait(false);

        var view = Mock.Get(player.ViewPlugIns.GetPlugIn<IShowShopCurrencyPlugIn>()!);
        view.Verify(v => v.ShowShopCurrencyAsync((byte)14, (short)13), Times.Once);
    }
}
