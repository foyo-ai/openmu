// <copyright file="WebApiController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.API
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using MUnique.OpenMU.AttributeSystem;
    using MUnique.OpenMU.DataModel.Configuration;
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
    /// Writes are executed server-side on offline characters only (409 if the account is online).
    /// </summary>
    [ApiKey]
    [Route("api/v1")]
    public class WebApiController : Controller
    {
        private readonly IDictionary<int, IGameServer> _gameServers;
        private readonly IPersistenceContextProvider _persistenceProvider;
        private readonly IDataSource<GameConfiguration> _configSource;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebApiController"/> class.
        /// </summary>
        /// <param name="gameServers">The running game servers, used for live data and online checks.</param>
        /// <param name="persistenceProvider">The persistence provider — a FRESH context is created per request to avoid stale reads.</param>
        /// <param name="configSource">The game configuration data source.</param>
        /// <param name="configuration">The app configuration (for write-action settings).</param>
        public WebApiController(IDictionary<int, IGameServer> gameServers, IPersistenceContextProvider persistenceProvider, IDataSource<GameConfiguration> configSource, IConfiguration configuration)
        {
            this._gameServers = gameServers;
            this._persistenceProvider = persistenceProvider;
            this._configSource = configSource;
            this._configuration = configuration;
        }

        /// <summary>
        /// Authenticated smoke-test endpoint to verify routing and the API key.
        /// </summary>
        /// <returns>An object indicating success.</returns>
        [HttpGet]
        [Route("ping")]
        public IActionResult Ping() => this.Ok(new { ok = true });

        /// <summary>Gets basic account information.</summary>
        /// <param name="login">The account login name.</param>
        /// <returns>The account info, or 404.</returns>
        [HttpGet]
        [Route("accounts/{login}")]
        public async Task<IActionResult> GetAccountAsync(string login)
        {
            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false);
            return account is null ? NotFound("not_found", "Account not found.") : this.Ok(ToAccountDto(account));
        }

        /// <summary>Gets the characters of an account (live when online, otherwise from persistence).</summary>
        /// <param name="login">The account login name.</param>
        /// <returns>The character list, or 404.</returns>
        [HttpGet]
        [Route("accounts/{login}/characters")]
        public async Task<IActionResult> GetCharactersAsync(string login)
        {
            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false);
            if (account is null)
            {
                return NotFound("not_found", "Account not found.");
            }

            var result = new List<object>();
            foreach (var character in account.Characters)
            {
                var player = await this.FindOnlinePlayerByCharacterAsync(character.Name).ConfigureAwait(false);
                result.Add(BuildCharacterDto(character, player));
            }

            return this.Ok(result);
        }

        /// <summary>Gets a single character by name (live when online, otherwise from persistence).</summary>
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

            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByCharacterNameAsync(name).ConfigureAwait(false);
            var character = account?.Characters.FirstOrDefault(c => c.Name == name);
            return character is null ? NotFound("not_found", "Character not found.") : this.Ok(BuildCharacterDto(character, null));
        }

        /// <summary>Resets an (offline) character using the configured rules.</summary>
        /// <param name="name">The character name.</param>
        /// <param name="request">The owning account login.</param>
        /// <returns>The updated character, or an error.</returns>
        [HttpPost]
        [Route("characters/{name}/reset")]
        public Task<IActionResult> ResetAsync(string name, [FromBody] LoginRequest request)
            => this.WriteAsync(request?.Login, name, async (ctx, character) =>
            {
                var minLevel = this.ConfigInt("WebApi:ResetMinLevel", 400);
                if (AttributeValue(character, Stats.Level.Id) < minLevel)
                {
                    return this.Error(422, "reset_level", $"Level {minLevel} is required to reset.");
                }

                var config = await this.GetConfigAsync().ConfigureAwait(false);
                this.SetAttribute(ctx, config, character, Stats.Resets, AttributeValue(character, Stats.Resets.Id) + 1);
                this.SetAttribute(ctx, config, character, Stats.Level, this.ConfigInt("WebApi:ResetLevelAfter", 1));
                character.Experience = 0;
                if (this.ConfigBool("WebApi:ResetClearStats", false) && character.CharacterClass is { } cc)
                {
                    foreach (var stat in cc.StatAttributes.Where(s => s.IncreasableByPlayer && s.Attribute is not null))
                    {
                        this.SetAttribute(ctx, config, character, stat.Attribute!, stat.BaseValue);
                    }
                }

                character.LevelUpPoints += this.ConfigInt("WebApi:ResetRewardPoints", 0);
                return null;
            }).AsTask();

        /// <summary>Adds level-up points to an (offline) character's base stats.</summary>
        /// <param name="name">The character name.</param>
        /// <param name="request">The points to add.</param>
        /// <returns>The updated character, or an error.</returns>
        [HttpPost]
        [Route("characters/{name}/add-points")]
        public Task<IActionResult> AddPointsAsync(string name, [FromBody] AddPointsRequest request)
            => this.WriteAsync(request?.Login, name, (ctx, character) =>
            {
                var total = request!.Strength + request.Agility + request.Vitality + request.Energy + request.Leadership;
                if (total <= 0)
                {
                    return new ValueTask<IActionResult?>(this.Error(422, "no_points", "Nothing to add."));
                }

                if (total > character.LevelUpPoints)
                {
                    return new ValueTask<IActionResult?>(this.Error(422, "not_enough_points", "Not enough level-up points."));
                }

                this.AddToAttribute(character, Stats.BaseStrength, request.Strength);
                this.AddToAttribute(character, Stats.BaseAgility, request.Agility);
                this.AddToAttribute(character, Stats.BaseVitality, request.Vitality);
                this.AddToAttribute(character, Stats.BaseEnergy, request.Energy);
                this.AddToAttribute(character, Stats.BaseLeadership, request.Leadership);
                character.LevelUpPoints -= total;
                return new ValueTask<IActionResult?>((IActionResult?)null);
            }).AsTask();

        /// <summary>Renames an (offline) character.</summary>
        /// <param name="name">The current character name.</param>
        /// <param name="request">The new name.</param>
        /// <returns>The updated character, or an error.</returns>
        [HttpPatch]
        [Route("characters/{name}/rename")]
        public async Task<IActionResult> RenameAsync(string name, [FromBody] RenameRequest request)
        {
            var newName = request?.NewName?.Trim() ?? string.Empty;
            if (newName.Length is < 1 or > 10 || !System.Text.RegularExpressions.Regex.IsMatch(newName, "^[A-Za-z0-9]+$"))
            {
                return this.Error(422, "invalid_name", "Name must be 1-10 letters or numbers.");
            }

            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var existing = await context.GetAccountByCharacterNameAsync(newName).ConfigureAwait(false);
            if (existing is not null)
            {
                return this.Error(422, "name_taken", "That name is already taken.");
            }

            return await this.WriteAsync(request?.Login, name, (ctx, character) =>
            {
                character.Name = newName;
                return new ValueTask<IActionResult?>((IActionResult?)null);
            }).ConfigureAwait(false);
        }

        /// <summary>Clears the PK status of an (offline) character.</summary>
        /// <param name="name">The character name.</param>
        /// <param name="request">The owning account login.</param>
        /// <returns>The updated character, or an error.</returns>
        [HttpPost]
        [Route("characters/{name}/clear-pk")]
        public Task<IActionResult> ClearPkAsync(string name, [FromBody] LoginRequest request)
            => this.WriteAsync(request?.Login, name, (ctx, character) =>
            {
                character.PlayerKillCount = 0;
                character.State = HeroState.Normal;
                character.StateRemainingSeconds = 0;
                return new ValueTask<IActionResult?>((IActionResult?)null);
            }).AsTask();

        /// <summary>Moves a stuck (offline) character to the safezone (Lorencia by default).</summary>
        /// <param name="name">The character name.</param>
        /// <param name="request">The owning account login.</param>
        /// <returns>The updated character, or an error.</returns>
        [HttpPost]
        [Route("characters/{name}/unstick")]
        public Task<IActionResult> UnstickAsync(string name, [FromBody] LoginRequest request)
            => this.WriteAsync(request?.Login, name, async (ctx, character) =>
            {
                var config = await this.GetConfigAsync().ConfigureAwait(false);
                var mapNumber = (short)this.ConfigInt("WebApi:SafezoneMap", 0);
                var map = config.Maps.FirstOrDefault(m => m.Number == mapNumber);
                if (map is null)
                {
                    return this.Error(422, "safezone", "Safezone map not found.");
                }

                character.CurrentMap = map;
                character.PositionX = (byte)this.ConfigInt("WebApi:SafezoneX", 125);
                character.PositionY = (byte)this.ConfigInt("WebApi:SafezoneY", 125);
                return null;
            }).AsTask();

        /// <summary>Deletes an (offline) character (security code + guild checks), with a real cascade.</summary>
        /// <param name="name">The character name.</param>
        /// <param name="request">Login and security code.</param>
        /// <returns>A result, or an error.</returns>
        [HttpDelete]
        [Route("characters/{name}")]
        public async Task<IActionResult> DeleteAsync(string name, [FromBody] DeleteRequest request)
        {
            if (string.IsNullOrEmpty(request?.Login))
            {
                return this.Error(422, "login_required", "Account login is required.");
            }

            if (await this.IsAccountOnlineAsync(request.Login).ConfigureAwait(false))
            {
                return this.Error(409, "character_online", "The account is online. Log out of the game first.");
            }

            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(request.Login).ConfigureAwait(false);
            var character = account?.Characters.FirstOrDefault(c => c.Name == name);
            if (account is null || character is null)
            {
                return NotFound("not_found", "Character not found.");
            }

            var securityCode = request.SecurityCode ?? string.Empty;
            var ok = string.IsNullOrEmpty(account.SecurityCode)
                ? BCrypt.Net.BCrypt.Verify(securityCode, account.PasswordHash)
                : account.SecurityCode == securityCode;
            if (!ok)
            {
                return this.Error(403, "wrong_security_code", "Incorrect security code.");
            }

            if (await this.IsInGuildAsync(character).ConfigureAwait(false))
            {
                return this.Error(409, "in_guild", "Leave the guild before deleting this character.");
            }

            account.Characters.Remove(character);
            await context.DeleteAsync(character).ConfigureAwait(false);
            await context.SaveChangesAsync().ConfigureAwait(false);
            return this.Ok(new { ok = true });
        }

        /// <summary>Registers a new account.</summary>
        /// <param name="request">The registration data.</param>
        /// <returns>The created account, or an error.</returns>
        [HttpPost]
        [Route("accounts")]
        public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request)
        {
            var login = request?.Login?.Trim() ?? string.Empty;
            if (login.Length is < 3 or > 10)
            {
                return this.Error(422, "invalid_login", "Login must be 3-10 characters.");
            }

            if ((request!.Password?.Length ?? 0) < 4)
            {
                return this.Error(422, "invalid_password", "Password is too short.");
            }

            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            if (await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false) is not null)
            {
                return this.Error(422, "login_taken", "That login name is already taken.");
            }

            var account = context.CreateNew<Account>();
            account.LoginName = login;
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            account.EMail = request.Email ?? string.Empty;
            account.SecurityCode = request.SecurityCode ?? string.Empty;
            account.RegistrationDate = DateTime.UtcNow;
            account.State = AccountState.Normal;
            await context.SaveChangesAsync().ConfigureAwait(false);
            return this.Ok(ToAccountDto(account));
        }

        /// <summary>Authenticates an account by login + password.</summary>
        /// <param name="request">Login and password.</param>
        /// <returns>The account on success, or 401.</returns>
        [HttpPost]
        [Route("accounts/authenticate")]
        public async Task<IActionResult> AuthenticateAsync([FromBody] AuthRequest request)
        {
            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(request?.Login ?? string.Empty, request?.Password ?? string.Empty).ConfigureAwait(false);
            return account is null ? this.Error(401, "invalid_credentials", "Invalid login or password.") : this.Ok(ToAccountDto(account));
        }

        /// <summary>Changes an account's password (requires the current password).</summary>
        /// <param name="login">The account login.</param>
        /// <param name="request">Current + new password.</param>
        /// <returns>Ok, or an error.</returns>
        [HttpPatch]
        [Route("accounts/{login}/password")]
        public Task<IActionResult> UpdatePasswordAsync(string login, [FromBody] PasswordChangeRequest request)
            => this.UpdateAccountAsync(login, request?.CurrentPassword, account =>
            {
                if ((request!.NewPassword?.Length ?? 0) < 4)
                {
                    return this.Error(422, "invalid_password", "Password is too short.");
                }

                account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                return null;
            });

        /// <summary>Changes an account's email (requires the current password).</summary>
        /// <param name="login">The account login.</param>
        /// <param name="request">Current password + new email.</param>
        /// <returns>Ok, or an error.</returns>
        [HttpPatch]
        [Route("accounts/{login}/email")]
        public Task<IActionResult> UpdateEmailAsync(string login, [FromBody] EmailChangeRequest request)
            => this.UpdateAccountAsync(login, request?.CurrentPassword, account =>
            {
                account.EMail = request!.Email ?? string.Empty;
                return null;
            });

        /// <summary>Changes an account's security code (requires the current password).</summary>
        /// <param name="login">The account login.</param>
        /// <param name="request">Current password + new security code.</param>
        /// <returns>Ok, or an error.</returns>
        [HttpPatch]
        [Route("accounts/{login}/security-code")]
        public Task<IActionResult> UpdateSecurityCodeAsync(string login, [FromBody] SecurityCodeChangeRequest request)
            => this.UpdateAccountAsync(login, request?.CurrentPassword, account =>
            {
                account.SecurityCode = request!.SecurityCode ?? string.Empty;
                return null;
            });

        /// <summary>Returns a leaderboard (players by resets, killers by PK, or guilds by score).</summary>
        /// <param name="type">players | killers | guilds.</param>
        /// <param name="limit">Max rows.</param>
        /// <returns>The ranking rows.</returns>
        [HttpGet]
        [Route("rankings")]
        public async Task<IActionResult> RankingsAsync([FromQuery] string type = "players", [FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 200);
            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);

            if (type == "guilds")
            {
                try
                {
                    var guilds = (await context.GetAsync<DataModel.Entities.Guild>().ConfigureAwait(false))
                        .OrderByDescending(g => g.Score)
                        .Take(limit)
                        .Select((g, i) => new { rank = i + 1, name = g.Name, score = g.Score, members = g.Members?.Count ?? 0 })
                        .ToList();
                    return this.Ok(guilds);
                }
                catch
                {
                    return this.Ok(Array.Empty<object>());
                }
            }

            var characters = (await context.GetAsync<Character>().ConfigureAwait(false))
                .Where(c => c.CharacterStatus != CharacterStatus.Banned);

            if (type == "killers")
            {
                var killers = characters
                    .Where(c => c.PlayerKillCount > 0)
                    .OrderByDescending(c => c.PlayerKillCount)
                    .Take(limit)
                    .Select((c, i) => new { rank = i + 1, name = c.Name, className = c.CharacterClass is { } cc ? (string)cc.Name : null, kills = c.PlayerKillCount });
                return this.Ok(killers);
            }

            var players = characters
                .Select(c => new { c, resets = AttributeValue(c, Stats.Resets.Id), level = AttributeValue(c, Stats.Level.Id) })
                .OrderByDescending(x => x.resets).ThenByDescending(x => x.level)
                .Take(limit)
                .Select((x, i) => new { rank = i + 1, name = x.c.Name, className = x.c.CharacterClass is { } cc ? (string)cc.Name : null, x.resets, x.level });
            return this.Ok(players);
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

        private NotFoundObjectResult NotFound(string code, string message) => this.NotFound(new { error = code, message });

        private IActionResult Error(int status, string code, string message) => this.StatusCode(status, new { error = code, message });

        private async ValueTask<IPlayerContext> NewPlayerContextAsync()
        {
            var config = await this.GetConfigAsync().ConfigureAwait(false);
            return this._persistenceProvider.CreateNewPlayerContext(config);
        }

        /// <summary>Loads an account, verifies the current password, applies a change, and saves.</summary>
        private async Task<IActionResult> UpdateAccountAsync(string login, string? currentPassword, Func<Account, IActionResult?> apply)
        {
            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login).ConfigureAwait(false);
            if (account is null)
            {
                return NotFound("not_found", "Account not found.");
            }

            if (!BCrypt.Net.BCrypt.Verify(currentPassword ?? string.Empty, account.PasswordHash))
            {
                return this.Error(403, "wrong_password", "Current password is incorrect.");
            }

            var error = apply(account);
            if (error is not null)
            {
                return error;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            return this.Ok(new { ok = true });
        }

        private async ValueTask<GameConfiguration> GetConfigAsync()
            => (GameConfiguration)await this._configSource.GetOwnerAsync().ConfigureAwait(false);

        private int ConfigInt(string key, int fallback) => int.TryParse(this._configuration[key], out var v) ? v : fallback;

        private bool ConfigBool(string key, bool fallback) => bool.TryParse(this._configuration[key], out var v) ? v : fallback;

        private void SetAttribute(IContext ctx, GameConfiguration config, Character character, AttributeDefinition definition, float value)
        {
            var attribute = character.Attributes.FirstOrDefault(a => a.Definition?.Id == definition.Id);
            if (attribute is not null)
            {
                attribute.Value = value;
                return;
            }

            var configDefinition = config.Attributes.FirstOrDefault(a => a.Id == definition.Id) ?? definition;
            var created = ctx.CreateNew<StatAttribute>(configDefinition, value);
            character.Attributes.Add(created);
        }

        private void AddToAttribute(Character character, AttributeDefinition definition, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            var attribute = character.Attributes.FirstOrDefault(a => a.Definition?.Id == definition.Id);
            if (attribute is not null)
            {
                attribute.Value += amount;
            }
        }

        /// <summary>Shared write pipeline: reject if online, load the owned offline character, apply, save.</summary>
        private async ValueTask<IActionResult> WriteAsync(string? login, string name, Func<IContext, Character, ValueTask<IActionResult?>> action)
        {
            if (string.IsNullOrEmpty(login))
            {
                return this.Error(422, "login_required", "Account login is required.");
            }

            if (await this.IsAccountOnlineAsync(login).ConfigureAwait(false))
            {
                return this.Error(409, "character_online", "The account is online. Log out of the game first.");
            }

            using var context = await this.NewPlayerContextAsync().ConfigureAwait(false);
            var account = await context.GetAccountByLoginNameAsync(login!).ConfigureAwait(false);
            var character = account?.Characters.FirstOrDefault(c => c.Name == name);
            if (character is null)
            {
                return NotFound("not_found", "Character not found.");
            }

            var error = await action(context, character).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            return this.Ok(BuildCharacterDto(character, null));
        }

        private async ValueTask<bool> IsAccountOnlineAsync(string? login)
        {
            if (string.IsNullOrEmpty(login))
            {
                return false;
            }

            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var players = await server.Context.GetPlayersAsync().ConfigureAwait(false);
                if (players.Any(p => p.Account?.LoginName == login))
                {
                    return true;
                }
            }

            return false;
        }

        private async ValueTask<bool> IsInGuildAsync(Character character)
        {
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                if (server.Context is IGameServerContext gameServerContext)
                {
                    var position = await gameServerContext.GuildServer.GetGuildPositionAsync(character.GetId()).ConfigureAwait(false);
                    return position != GuildPosition.Undefined;
                }
            }

            return false;
        }

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

        /// <summary>Request body carrying just the owning account login.</summary>
        public class LoginRequest
        {
            /// <summary>Gets or sets the owning account login.</summary>
            public string? Login { get; set; }
        }

        /// <summary>Request body for adding stat points.</summary>
        public class AddPointsRequest : LoginRequest
        {
            /// <summary>Gets or sets the strength points to add.</summary>
            public int Strength { get; set; }

            /// <summary>Gets or sets the agility points to add.</summary>
            public int Agility { get; set; }

            /// <summary>Gets or sets the vitality points to add.</summary>
            public int Vitality { get; set; }

            /// <summary>Gets or sets the energy points to add.</summary>
            public int Energy { get; set; }

            /// <summary>Gets or sets the leadership points to add.</summary>
            public int Leadership { get; set; }
        }

        /// <summary>Request body for renaming a character.</summary>
        public class RenameRequest : LoginRequest
        {
            /// <summary>Gets or sets the new character name.</summary>
            public string? NewName { get; set; }
        }

        /// <summary>Request body for deleting a character.</summary>
        public class DeleteRequest : LoginRequest
        {
            /// <summary>Gets or sets the account security code.</summary>
            public string? SecurityCode { get; set; }
        }

        /// <summary>Request body for registering an account.</summary>
        public class RegisterRequest
        {
            /// <summary>Gets or sets the login name.</summary>
            public string? Login { get; set; }

            /// <summary>Gets or sets the password.</summary>
            public string? Password { get; set; }

            /// <summary>Gets or sets the email.</summary>
            public string? Email { get; set; }

            /// <summary>Gets or sets the security code.</summary>
            public string? SecurityCode { get; set; }
        }

        /// <summary>Request body for authenticating.</summary>
        public class AuthRequest
        {
            /// <summary>Gets or sets the login name.</summary>
            public string? Login { get; set; }

            /// <summary>Gets or sets the password.</summary>
            public string? Password { get; set; }
        }

        /// <summary>Request body for changing the password.</summary>
        public class PasswordChangeRequest
        {
            /// <summary>Gets or sets the current password.</summary>
            public string? CurrentPassword { get; set; }

            /// <summary>Gets or sets the new password.</summary>
            public string? NewPassword { get; set; }
        }

        /// <summary>Request body for changing the email.</summary>
        public class EmailChangeRequest
        {
            /// <summary>Gets or sets the current password.</summary>
            public string? CurrentPassword { get; set; }

            /// <summary>Gets or sets the new email.</summary>
            public string? Email { get; set; }
        }

        /// <summary>Request body for changing the security code.</summary>
        public class SecurityCodeChangeRequest
        {
            /// <summary>Gets or sets the current password.</summary>
            public string? CurrentPassword { get; set; }

            /// <summary>Gets or sets the new security code.</summary>
            public string? SecurityCode { get; set; }
        }
    }
}
