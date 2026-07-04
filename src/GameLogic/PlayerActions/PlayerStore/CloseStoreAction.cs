// <copyright file="CloseStoreAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;

using MUnique.OpenMU.GameLogic.Views.PlayerShop;

/// <summary>
/// Action to close the own player store.
/// </summary>
public class CloseStoreAction
{
    /// <summary>
    /// Closes the store of the player.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask CloseStoreAsync(Player player)
    {
        if (player.ShopStorage is null)
        {
            return;
        }

        using (await player.ShopStorage.StoreLock.LockAsync())
        {
            player.ShopStorage.StoreOpen = false;
        }

        await player.ForEachWorldObserverAsync<IPlayerShopClosedPlugIn>(plugin => plugin.PlayerShopClosedAsync(player), true).ConfigureAwait(false);

        // A store ghost without an open store has no purpose anymore (e.g. it sold out),
        // so its offline session is stopped, which also saves the earned money.
        if (player is Offline.OfflinePlayer { Mode: Offline.OfflinePlayerMode.Store, AccountLoginName: { } loginName })
        {
            await player.GameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
        }
    }
}