// <copyright file="IShowItemBankBalancesPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.ItemBank;

/// <summary>
/// Interface of a view whose implementation sends the account-wide item bank balances to the client,
/// so the jewel bank window can show the configured bankable items and their banked counts.
/// </summary>
public interface IShowItemBankBalancesPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the item bank balances of the player's account.
    /// </summary>
    /// <param name="balances">The configured bankable items with their current banked counts.</param>
    ValueTask ShowItemBankBalancesAsync(IReadOnlyList<ItemBankBalance> balances);
}

/// <summary>
/// One configured bankable item and its account-wide banked count.
/// </summary>
/// <param name="ItemGroup">The item group.</param>
/// <param name="ItemNumber">The item number.</param>
/// <param name="Count">The banked count.</param>
/// <param name="Alias">The short command alias (e.g. "bless"), used by the client to send /bank commands.</param>
public readonly record struct ItemBankBalance(byte ItemGroup, short ItemNumber, int Count, string Alias);
