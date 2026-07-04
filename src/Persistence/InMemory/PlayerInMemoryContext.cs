// <copyright file="PlayerInMemoryContext.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.InMemory;

using System.Threading;
using MUnique.OpenMU.Persistence.BasicModel;

/// <summary>
/// In-memory context implementation for <see cref="IPlayerContext"/>.
/// </summary>
public class PlayerInMemoryContext : InMemoryContext, IPlayerContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerInMemoryContext"/> class.
    /// </summary>
    /// <param name="provider">The manager which holds the memory repositories.</param>
    public PlayerInMemoryContext(InMemoryRepositoryProvider provider)
        : base(provider)
    {
    }

    /// <inheritdoc/>
    public async ValueTask<MUnique.OpenMU.DataModel.Entities.LetterBody?> GetLetterBodyByHeaderIdAsync(Guid headerId, CancellationToken cancellationToken = default)
    {
        var allLetters = await this.Provider.GetRepository<LetterBody>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allLetters.FirstOrDefault(body => body.Header.Id == headerId);
    }

    /// <inheritdoc/>
    public async ValueTask<MUnique.OpenMU.DataModel.Entities.AccountState?> AuthenticateAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        var account = allAccounts.FirstOrDefault(a => a.LoginName == loginName && BCrypt.Net.BCrypt.Verify(password, a.PasswordHash));
        return account?.State;
    }

    /// <inheritdoc/>
    public async ValueTask<MUnique.OpenMU.DataModel.Entities.Account?> GetAccountByLoginNameAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allAccounts.FirstOrDefault(account => account.LoginName == loginName && BCrypt.Net.BCrypt.Verify(password, account.PasswordHash));
    }

    /// <inheritdoc/>
    public async ValueTask<MUnique.OpenMU.DataModel.Entities.Account?> GetAccountByLoginNameAsync(string loginName, CancellationToken cancellationToken = default)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allAccounts.FirstOrDefault(account => account.LoginName == loginName);
    }

    /// <inheritdoc/>
    public async ValueTask<IEnumerable<MUnique.OpenMU.DataModel.Entities.Account>> GetAccountsOrderedByLoginNameAsync(int skip, int count, CancellationToken cancellationToken = default)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allAccounts.OrderBy(a => a.LoginName).Skip(skip).Take(count);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CanSaveLetterAsync(Interfaces.LetterHeader letterHeader, CancellationToken cancellationToken = default)
    {
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<DataModel.Entities.Account?> GetAccountByCharacterNameAsync(string characterName, CancellationToken cancellationToken = default)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allAccounts.FirstOrDefault(account => account.Characters.Any(c => c.Name == characterName));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CharacterRankingEntry>> GetCharacterRankingByStatsAsync(Guid resetsAttributeId, Guid levelAttributeId, int count, CancellationToken cancellationToken = default)
    {
        var characters = await this.GetRankableCharactersAsync(cancellationToken).ConfigureAwait(false);
        return characters
            .Select(c => new CharacterRankingEntry(c.Name, c.CharacterClass?.GetId(), c.PlayerKillCount, GetStatValue(c, resetsAttributeId), GetStatValue(c, levelAttributeId)))
            .OrderByDescending(e => e.Resets)
            .ThenByDescending(e => e.Level)
            .ThenBy(e => e.Name)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CharacterRankingEntry>> GetCharacterRankingByKillsAsync(int count, CancellationToken cancellationToken = default)
    {
        var characters = await this.GetRankableCharactersAsync(cancellationToken).ConfigureAwait(false);
        return characters
            .Where(c => c.PlayerKillCount > 0)
            .OrderByDescending(c => c.PlayerKillCount)
            .ThenBy(c => c.Name)
            .Take(count)
            .Select(c => new CharacterRankingEntry(c.Name, c.CharacterClass?.GetId(), c.PlayerKillCount, 0f, 0f))
            .ToList();
    }

    private static float GetStatValue(DataModel.Entities.Character character, Guid definitionId)
    {
        return character.Attributes?.FirstOrDefault(a => a.Definition?.Id == definitionId)?.Value ?? 0f;
    }

    private async ValueTask<IEnumerable<DataModel.Entities.Character>> GetRankableCharactersAsync(CancellationToken cancellationToken)
    {
        var allAccounts = await this.Provider.GetRepository<Account>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        return allAccounts
            .SelectMany(account => account.Characters)
            .Where(c => c.CharacterStatus != DataModel.Entities.CharacterStatus.Banned);
    }
}