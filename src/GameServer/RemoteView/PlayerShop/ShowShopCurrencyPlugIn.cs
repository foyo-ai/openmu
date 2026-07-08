// <copyright file="ShowShopCurrencyPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.PlayerShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.PlayerShop;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The default implementation of <see cref="IShowShopCurrencyPlugIn"/> which sends the personal
/// store's current currency to the owner (consumed by the shop currency selector).
/// </summary>
[PlugIn]
[Guid("B5E2D74A-1C86-4F03-9A5D-3E9C7A02F14B")]
public class ShowShopCurrencyPlugIn : IShowShopCurrencyPlugIn
{
    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowShopCurrencyPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public ShowShopCurrencyPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowShopCurrencyAsync(byte itemGroup, short itemNumber)
    {
        await this._player.Connection.SendShopCurrencyAsync(itemGroup, (ushort)itemNumber).ConfigureAwait(false);
    }
}
