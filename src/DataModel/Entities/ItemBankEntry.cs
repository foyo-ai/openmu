// <copyright file="ItemBankEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// A per-account stored count of one item type, identified by its item group and number.
/// </summary>
/// <remarks>
/// Used by the account item bank (see the <c>/bank</c> chat command) so that fungible items
/// like jewels are kept as plain numbers, instead of occupying inventory or vault slots.
/// Which item types are bankable is configured on the bank chat command plugin.
/// </remarks>
public class ItemBankEntry
{
    /// <summary>
    /// Gets or sets the item group of the stored item type.
    /// </summary>
    public byte ItemGroup { get; set; }

    /// <summary>
    /// Gets or sets the item number of the stored item type (within its group).
    /// </summary>
    public short ItemNumber { get; set; }

    /// <summary>
    /// Gets or sets the stored count of this item type.
    /// </summary>
    public int Count { get; set; }
}
