// <copyright file="ItemBankExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Helper methods for the account-wide item bank (see <see cref="Account.ItemBank"/>), which keeps
/// per-item-type counts as numbers. Shared by the <c>/bank</c> command, the jewel-priced player store
/// and the bank UI packet handlers.
/// </summary>
public static class ItemBankExtensions
{
    /// <summary>
    /// Gets the banked count of the item type identified by <paramref name="itemGroup"/> and <paramref name="itemNumber"/>.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="itemGroup">The item group.</param>
    /// <param name="itemNumber">The item number.</param>
    /// <returns>The banked count (0 if none).</returns>
    public static int GetItemBankCount(this Account account, byte itemGroup, short itemNumber)
    {
        return account.ItemBank.FirstOrDefault(e => e.ItemGroup == itemGroup && e.ItemNumber == itemNumber)?.Count ?? 0;
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the banked count of an item type for the player's account,
    /// creating the entry if needed. A negative delta withdraws.
    /// </summary>
    /// <param name="player">The player whose account and persistence context are used.</param>
    /// <param name="itemGroup">The item group.</param>
    /// <param name="itemNumber">The item number.</param>
    /// <param name="delta">The amount to add (negative to subtract).</param>
    /// <returns>The resulting banked count.</returns>
    public static int AddToItemBank(this Player player, byte itemGroup, short itemNumber, int delta)
    {
        var account = player.Account!;
        var entry = account.ItemBank.FirstOrDefault(e => e.ItemGroup == itemGroup && e.ItemNumber == itemNumber);
        if (entry is null)
        {
            entry = player.PersistenceContext.CreateNew<ItemBankEntry>();
            entry.ItemGroup = itemGroup;
            entry.ItemNumber = itemNumber;
            entry.Count = 0;
            account.ItemBank.Add(entry);
        }

        entry.Count += delta;
        return entry.Count;
    }
}
