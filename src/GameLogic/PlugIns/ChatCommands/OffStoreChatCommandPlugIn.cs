// <copyright file="OffStoreChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the <c>/offstore</c> chat command.
/// <list type="bullet">
///   <item>Requires the personal store to be open.</item>
///   <item>Logs the login-server entry off so the real player can re-connect at any time.</item>
///   <item>Disconnects the real client connection.</item>
///   <item>Creates a silent ghost player which keeps the store open at the current position.</item>
///   <item>The ghost stops when the store sells out or the player logs back in.</item>
/// </list>
/// </summary>
[Guid("6D2B8F41-9C7A-4E53-B0D8-4A1F7C2E9B36")]
[PlugIn]
[Display(
    Name = nameof(PlugInResources.OffStoreChatCommandPlugIn_Name),
    Description = nameof(PlugInResources.OffStoreChatCommandPlugIn_Description),
    ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public sealed class OffStoreChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/offstore";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        if (player.SelectedCharacter is null)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OfflineLevelingNoCharacterSelected)).ConfigureAwait(false);
            return;
        }

        if (!player.IsAlive)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OffStoreMustBeAlive)).ConfigureAwait(false);
            return;
        }

        if (player.CurrentMap is null)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OfflineLevelingNotOnMap)).ConfigureAwait(false);
            return;
        }

        if (!(player.ShopStorage?.StoreOpen ?? false))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OffStoreRequiresOpenStore)).ConfigureAwait(false);
            return;
        }

        var loginName = player.Account?.LoginName;
        if (loginName is null)
        {
            return;
        }

        var manager = player.GameContext.OfflinePlayerManager;

        if (manager.IsActive(loginName))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OffStoreAlreadyActive)).ConfigureAwait(false);
            return;
        }

        await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OffStoreStarted)).ConfigureAwait(false);

        if (!await manager.StartAsync(player, loginName, OfflinePlayerMode.Store).ConfigureAwait(false))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.OffStoreFailed)).ConfigureAwait(false);
        }
    }
}
