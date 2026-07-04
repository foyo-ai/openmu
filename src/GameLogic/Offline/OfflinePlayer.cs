// <copyright file="OfflinePlayer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Offline;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.MuHelper;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// An offline player that continues leveling after the real client disconnects.
/// </summary>
public sealed class OfflinePlayer : Player
{
    private OfflinePlayerMuHelper? _intelligence;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfflinePlayer"/> class.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="mode">The behavior of the offline player.</param>
    public OfflinePlayer(IGameContext gameContext, OfflinePlayerMode mode = OfflinePlayerMode.Leveling)
        : base(gameContext)
    {
        this.Mode = mode;
    }

    /// <summary>
    /// Gets the behavior of this offline player.
    /// </summary>
    public OfflinePlayerMode Mode { get; }

    /// <summary>
    /// Gets the login name this offline player belongs to.
    /// </summary>
    public string? AccountLoginName => this.Account?.LoginName;

    /// <summary>
    /// Gets the start timestamp of the offline session.
    /// </summary>
    public DateTime StartTimestamp { get; internal set; }

    /// <summary>
    /// Initializes the offline player from captured references.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="character">The character.</param>
    /// <returns><c>true</c> if successfully started.</returns>
    public async ValueTask<bool> InitializeAsync(Account account, Character character)
    {
        try
        {
            this.StartTimestamp = DateTime.UtcNow;
            this.Account = account;
            this.PersistenceContext.Attach(account);

            await this.AdvanceToCharacterSelectionStateAsync().ConfigureAwait(false);

            await this.SetupCharacterAsync(character).ConfigureAwait(false);

            await this.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);

            if (this.Mode == OfflinePlayerMode.Store && !(this.ShopStorage?.StoreOpen ?? false))
            {
                // The store restore in OnPlayerEnteredWorldAsync didn't succeed - without an open
                // store this ghost has no purpose, so we treat the start as failed.
                this.Logger.LogWarning("Offline store ghost for {CharacterName} could not restore the store.", character.Name);
                return false;
            }

            if (this.Mode == OfflinePlayerMode.Leveling)
            {
                this.StartIntelligence();
            }

            this.Logger.LogDebug(
                "Offline player started for character {CharacterName} on map {Map} at {Position}.",
                character.Name,
                character.CurrentMap?.Name,
                this.Position);

            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to initialize offline player for {player}.", this);
            return false;
        }
    }

    /// <summary>
    /// Stops the offline player and removes it from the world.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (this._intelligence is { } intelligence)
        {
            await intelligence.DisposeAsync().ConfigureAwait(false);
            this._intelligence = null;
        }

        try
        {
            await this.SaveProgressAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to save progress of offline player {AccountLoginName}.", this.AccountLoginName);
        }

        await this.DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer()
        => new OfflineViewPlugInContainer(this);

    private async ValueTask AdvanceToCharacterSelectionStateAsync()
    {
        // Advance state to allow the intelligence to perform actions.
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);
    }

    private async ValueTask SetupCharacterAsync(Character character)
    {
        // Add to context and set character.
        await this.GameContext.AddPlayerAsync(this).ConfigureAwait(false);
        await this.SetSelectedCharacterAsync(character).ConfigureAwait(false);

        if (this.SelectedCharacter is { } selectedCharacter)
        {
            this.PersistenceContext.Attach(selectedCharacter);
        }
    }

    private void StartIntelligence()
    {
        this._intelligence = new OfflinePlayerMuHelper(this);
        this._intelligence.Start();
    }
}