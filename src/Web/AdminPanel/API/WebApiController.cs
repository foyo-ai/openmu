// <copyright file="WebApiController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.API
{
    using Microsoft.AspNetCore.Mvc;
    using MUnique.OpenMU.DataModel.Entities;
    using MUnique.OpenMU.GameLogic;
    using MUnique.OpenMU.GameLogic.Attributes;
    using MUnique.OpenMU.GameServer;
    using MUnique.OpenMU.Interfaces;
    using MUnique.OpenMU.Persistence;

    /// <summary>
    /// Authenticated API used by the external player website, so the website never accesses the
    /// game database directly. Protected by the <see cref="ApiKeyAttribute"/> shared-secret filter.
    /// Reads return live data from memory when the character is online, otherwise from persistence.
    /// </summary>
    [ApiKey]
    [Route("api/v1")]
    public class WebApiController : Controller
    {
        private readonly IDictionary<int, IGameServer> _gameServers;
        private readonly IDataSource<Account> _accountSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebApiController"/> class.
        /// </summary>
        /// <param name="gameServers">The running game servers, used for live data and online checks.</param>
        /// <param name="accountSource">The account data source, used for offline persistence access.</param>
        public WebApiController(IDictionary<int, IGameServer> gameServers, IDataSource<Account> accountSource)
        {
            this._gameServers = gameServers;
            this._accountSource = accountSource;
        }

        /// <summary>
        /// Authenticated smoke-test endpoint to verify routing and the API key.
        /// </summary>
        /// <returns>An object indicating success.</returns>
        [HttpGet]
        [Route("ping")]
        public IActionResult Ping() => this.Ok(new { ok = true });

        /// <summary>
        /// Gets basic account information.
        /// </summary>
        /// <param name="login">The account login name.</param>
        /// <returns>The account info, or 404.</returns>
        [HttpGet]
        [Route("accounts/{login}")]
        public async Task<IActionResult> GetAccountAsync(string login)
        {
            var context = await this.GetPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false);
            if (account is null)
            {
                return this.NotFound(new { error = "not_found", message = "Account not found." });
            }

            return this.Ok(ToAccountDto(account));
        }

        /// <summary>
        /// Gets the characters of an account (live when online, otherwise from persistence).
        /// </summary>
        /// <param name="login">The account login name.</param>
        /// <returns>The character list, or 404.</returns>
        [HttpGet]
        [Route("accounts/{login}/characters")]
        public async Task<IActionResult> GetCharactersAsync(string login)
        {
            var context = await this.GetPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false);
            if (account is null)
            {
                return this.NotFound(new { error = "not_found", message = "Account not found." });
            }

            var result = new List<object>();
            foreach (var character in account.Characters)
            {
                var player = await this.FindOnlinePlayerByCharacterAsync(character.Name).ConfigureAwait(false);
                result.Add(BuildCharacterDto(character, player));
            }

            return this.Ok(result);
        }

        /// <summary>
        /// Gets a single character by name (live when online, otherwise from persistence).
        /// </summary>
        /// <param name="name">The character name.</param>
        /// <returns>The character info, or 404.</returns>
        [HttpGet]
        [Route("characters/{name}")]
        public async Task<IActionResult> GetCharacterAsync(string name)
        {
            var player = await this.FindOnlinePlayerByCharacterAsync(name).ConfigureAwait(false);
            if (player?.SelectedCharacter is not null)
            {
                return this.Ok(BuildCharacterDto(player.SelectedCharacter, player));
            }

            var context = await this.GetPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByCharacterNameAsync(name).ConfigureAwait(false);
            var character = account?.Characters.FirstOrDefault(c => c.Name == name);
            if (character is null)
            {
                return this.NotFound(new { error = "not_found", message = "Character not found." });
            }

            return this.Ok(BuildCharacterDto(character, null));
        }

        private static object ToAccountDto(Account account) => new
        {
            login = account.LoginName,
            email = account.EMail,
            state = (int)account.State,
            registrationDate = account.RegistrationDate,
        };

        private static object BuildCharacterDto(Character character, Player? player)
        {
            var entity = player?.SelectedCharacter ?? character;
            var online = player is not null;
            var level = online ? player!.Level : AttributeValue(character, Stats.Level.Id);
            var resets = online ? (int)(player!.Attributes?[Stats.Resets] ?? 0f) : AttributeValue(character, Stats.Resets.Id);
            var masterLevel = online ? (int)(player!.Attributes?[Stats.MasterLevel] ?? 0f) : AttributeValue(character, Stats.MasterLevel.Id);

            return new
            {
                id = entity.GetId(),
                name = entity.Name,
                className = entity.CharacterClass is { } cc ? (string)cc.Name : null,
                level,
                resets,
                masterLevel,
                points = entity.LevelUpPoints,
                masterPoints = entity.MasterLevelUpPoints,
                kills = entity.PlayerKillCount,
                status = (int)entity.CharacterStatus,
                online,
            };
        }

        private static int AttributeValue(Character character, Guid definitionId)
        {
            var attribute = character.Attributes?.FirstOrDefault(a => a.Definition?.Id == definitionId);
            return (int)(attribute?.Value ?? 0f);
        }

        private async ValueTask<IPlayerContext> GetPlayerContextAsync()
            => (IPlayerContext)await this._accountSource.GetContextAsync().ConfigureAwait(false);

        private async ValueTask<Player?> FindOnlinePlayerByCharacterAsync(string characterName)
        {
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var players = await server.Context.GetPlayersAsync().ConfigureAwait(false);
                var player = players.FirstOrDefault(p => p.SelectedCharacter?.Name == characterName);
                if (player is not null)
                {
                    return player;
                }
            }

            return null;
        }
    }
}
