// <copyright file="IShowShopCurrencyPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.PlayerShop;

/// <summary>
/// Interface of a view whose implementation sends the personal store's current currency
/// (a bankable item, or Zen) to the store owner's client.
/// </summary>
public interface IShowShopCurrencyPlugIn : IViewPlugIn
{
    /// <summary>
    /// Sends the current store currency to the owner. <paramref name="itemGroup"/> = 0xFF and
    /// <paramref name="itemNumber"/> = -1 mean Zen.
    /// </summary>
    /// <param name="itemGroup">The currency item's group, or 0xFF for Zen.</param>
    /// <param name="itemNumber">The currency item's number, or -1 for Zen.</param>
    ValueTask ShowShopCurrencyAsync(byte itemGroup, short itemNumber);
}
