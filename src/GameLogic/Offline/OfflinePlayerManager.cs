// <copyright file="OfflinePlayerManager.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Offline;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.MuHelper;
using MUnique.OpenMU.GameLogic.Views.Login;

/// <summary>
/// Manages active <see cref="OfflinePlayer"/> sessions.
/// </summary>
public sealed class OfflinePlayerManager
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OfflinePlayer> _activePlayers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a snapshot of all currently active offline players.
    /// </summary>
    public IReadOnlyCollection<OfflinePlayer> OfflinePlayers
        => this._activePlayers.Values.ToList();

    /// <summary>
    /// Starts an offline player session by replacing the real player with a copy.
    /// </summary>
    /// <param name="realPlayer">The real player who typed the command.</param>
    /// <param name="loginName">The pre-validated account login name.</param>
    /// <param name="mode">The behavior of the offline player.</param>
    /// <returns><c>true</c> if the offline session was started successfully.</returns>
    public async ValueTask<bool> StartAsync(Player realPlayer, string loginName, OfflinePlayerMode mode = OfflinePlayerMode.Leveling)
    {
        var account = realPlayer.Account;
        var character = realPlayer.SelectedCharacter;

        if (account is null || character is null)
        {
            return false;
        }

        // Capture the identity and the CURRENT spawn spot from the live online player BEFORE tearing it
        // down. The ghost re-loads a FRESH copy of the account into its OWN context and spawns here — it
        // does NOT reuse the online player's tracked entity objects. Sharing those objects across the
        // (torn-down) online context and the ghost's context is exactly what let two persistence contexts
        // fight over the same items and lose them (the recurring 0-row periodic-save data loss).
        var gameContext = realPlayer.GameContext;
        var characterName = character.Name;
        var spawnMapNumber = realPlayer.CurrentMap?.Definition.Number ?? character.CurrentMap?.Number;
        var spawnX = realPlayer.Position.X;
        var spawnY = realPlayer.Position.Y;

        var sentinel = new OfflinePlayer(gameContext, mode);

        // Atomically claim the slot to prevent racing during initialization.
        if (!this._activePlayers.TryAdd(loginName, sentinel))
        {
            await sentinel.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        // Only leveling sessions are charged - a store ghost doesn't farm anything. Charge the live player
        // before the teardown so the deduction is part of its final save and the fresh reload reflects it.
        if (mode == OfflinePlayerMode.Leveling && !this.TryChargeInitialZenCost(realPlayer))
        {
            await this.RemoveAndDisposeAsync(loginName, sentinel).ConfigureAwait(false);
            return false;
        }

        try
        {
            // Fully tear down the online session (saves its final state, removes it from the world,
            // releases its context) and send the client back to the server-select screen.
            await this.TransitionToOfflineAsync(realPlayer, loginName).ConfigureAwait(false);

            // Load a FRESH copy of the account for the ghost. The loading context must stay alive through
            // InitializeAsync (the account graph is read while entering the world), then is disposed; the
            // ghost owns the account for its lifetime via its own PersistenceContext.
            using var loadingContext = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var freshAccount = await loadingContext.GetAccountByLoginNameAsync(loginName).ConfigureAwait(false);
            var freshCharacter = freshAccount?.Characters.FirstOrDefault(c => string.Equals(c.Name, characterName, StringComparison.Ordinal));
            if (freshAccount is null || freshCharacter is null)
            {
                // The fresh reload should always succeed in production (the account is persisted). If it
                // cannot be found - a genuine DB anomaly, or a unit-test harness with no real persistence -
                // fall back to the live objects so the ghost still starts. This fallback reuses the online
                // player's objects (the two-context situation the reload exists to avoid, i.e. the item-loss
                // protection is bypassed for this one session), so the warning is the monitoring signal.
                realPlayer.Logger.LogWarning(
                    "Offline ghost for '{LoginName}' could not reload a fresh account copy; falling back to the live objects (item-loss protection bypassed for this session).",
                    loginName);
                freshAccount = account;
                freshCharacter = character;
            }

            // Spawn the ghost where the player was, not at the safezone the teardown moved the (old) copy to.
            if (spawnMapNumber is { } mapNumber
                && gameContext.Configuration.Maps.FirstOrDefault(m => m.Number == mapNumber) is { } spawnMap)
            {
                freshCharacter.CurrentMap = spawnMap;
                freshCharacter.PositionX = spawnX;
                freshCharacter.PositionY = spawnY;
            }

            if (!await sentinel.InitializeAsync(freshAccount, freshCharacter).ConfigureAwait(false))
            {
                this._activePlayers.TryRemove(loginName, out _);
                return false;
            }

            return true;
        }
        catch
        {
            this._activePlayers.TryRemove(loginName, out _);
            throw;
        }
    }

    /// <summary>
    /// Stops and removes the offline session for the given account, if one exists.
    /// </summary>
    /// <param name="loginName">The account login name.</param>
    public async ValueTask StopAsync(string loginName)
    {
        if (this._activePlayers.TryRemove(loginName, out var offlinePlayer))
        {
            await offlinePlayer.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns whether an offline player session is currently active for <paramref name="loginName"/>.
    /// </summary>
    /// <param name="loginName">The account login name.</param>
    public bool IsActive(string loginName) => this._activePlayers.ContainsKey(loginName);

    /// <summary>
    /// Tries to get the active offline player for the given account login name.
    /// </summary>
    /// <param name="loginName">The account login name.</param>
    /// <param name="player">The offline player, if found.</param>
    /// <returns><c>true</c> if an active session exists; otherwise <c>false</c>.</returns>
    public bool TryGetPlayer(string loginName, out OfflinePlayer? player)
        => this._activePlayers.TryGetValue(loginName, out player);

    private async ValueTask TransitionToOfflineAsync(Player realPlayer, string loginName)
    {
        await this.LogOffFromLoginServerAsync(realPlayer, loginName).ConfigureAwait(false);

        // Tell the real client to go back to the server-select screen. Without this the client is only
        // told the socket closed and stays stuck in-game (it never receives a logout response), so the
        // player can't get out. LogOffFromLoginServer above already freed the account slot for reconnect.
        try
        {
            await realPlayer.InvokeViewPlugInAsync<ILogoutPlugIn>(p => p.LogoutAsync(LogoutType.BackToServerSelection)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            realPlayer.Logger.LogWarning(ex, "Could not send the back-to-server-select logout to the client during offline transition.");
        }

        realPlayer.SuppressDisconnectedEvent();
        await realPlayer.DisconnectAsync().ConfigureAwait(false);
        realPlayer.PersistenceContext.Dispose();
    }

    /// <summary>
    /// Calculates and deducts the initial Zen cost for starting an offline player session.
    /// The cost is based on the first MuHelper cost stage multiplied by the player's total level.
    /// </summary>
    /// <param name="player">The player to charge.</param>
    /// <returns><c>true</c> if the cost was successfully charged or no cost applies; otherwise <c>false</c>.</returns>
    private bool TryChargeInitialZenCost(Player player)
    {
        var initialCost = this.CalculateInitialZenCost(player);

        return initialCost <= 0 || player.TryRemoveMoney(initialCost);
    }

    /// <summary>
    /// Calculates the initial Zen cost for the given player based on the MuHelper configuration
    /// and the player's combined normal and master level.
    /// </summary>
    /// <param name="player">The player for whom to calculate the cost.</param>
    /// <returns>The Zen amount to charge; 0 if no cost applies.</returns>
    private int CalculateInitialZenCost(Player player)
    {
        var config = player.GameContext.FeaturePlugIns.GetPlugIn<MuHelperFeaturePlugIn>()?.Configuration
                     ?? new MuHelperConfiguration();

        var costPerStage = config.CostPerStage.FirstOrDefault();
        if (costPerStage <= 0)
        {
            return 0;
        }

        var totalLevel = player.Level + (int)(player.Attributes?[Stats.MasterLevel] ?? 0);

        return costPerStage * totalLevel;
    }

    /// <summary>
    /// Attempts to log the player off from the login server so the account slot is freed
    /// for reconnection while the ghost session is running. Failures are logged and swallowed
    /// because the offline session can still proceed without this step.
    /// </summary>
    /// <param name="player">The player being transitioned to offline.</param>
    /// <param name="loginName">The account login name.</param>
    private async ValueTask LogOffFromLoginServerAsync(Player player, string loginName)
    {
        if (player.GameContext is not IGameServerContext gsCtx)
        {
            return;
        }

        try
        {
            await gsCtx.LoginServer.LogOffAsync(loginName, gsCtx.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogWarning(ex, "Could not log off from login server during offline player start.");
        }
    }

    /// <summary>
    /// Removes the sentinel entry from the active players dictionary and disposes the ghost.
    /// Used when startup fails before the ghost is fully initialized.
    /// </summary>
    /// <param name="loginName">The account login name.</param>
    /// <param name="sentinel">The ghost player to dispose.</param>
    private async ValueTask RemoveAndDisposeAsync(string loginName, OfflinePlayer sentinel)
    {
        this._activePlayers.TryRemove(loginName, out _);
        await sentinel.DisposeAsync().ConfigureAwait(false);
    }
}