// <copyright file="ShowItemBankBalancesPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.ItemBank;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.ItemBank;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The default implementation of the <see cref="IShowItemBankBalancesPlugIn"/> which sends the
/// account-wide item bank balances to the client (consumed by the jewel bank window).
/// </summary>
[PlugIn]
[Guid("A7F3C1D8-9B24-4E56-8C1A-2D7E0F5B6A93")]
public class ShowItemBankBalancesPlugIn : IShowItemBankBalancesPlugIn
{
    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowItemBankBalancesPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public ShowItemBankBalancesPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowItemBankBalancesAsync(IReadOnlyList<ItemBankBalance> balances)
    {
        var connection = this._player.Connection;
        if (connection is null)
        {
            return;
        }

        int Write()
        {
            var size = ItemBankBalancesRef.GetRequiredSize(balances.Count);
            var span = connection.Output.GetSpan(size)[..size];
            var packet = new ItemBankBalancesRef(span)
            {
                ItemCount = (byte)balances.Count,
            };

            for (int i = 0; i < balances.Count; i++)
            {
                var balance = balances[i];
                var entry = packet[i];
                entry.ItemGroup = balance.ItemGroup;
                entry.ItemNumber = (ushort)balance.ItemNumber;
                entry.Count = (uint)Math.Max(0, balance.Count);
                entry.Alias = balance.Alias ?? string.Empty;
            }

            return size;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
