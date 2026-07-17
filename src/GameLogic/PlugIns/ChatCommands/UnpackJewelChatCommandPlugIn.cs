// <copyright file="UnpackJewelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the <c>/unpack</c> command: splits a packed jewel from the
/// inventory back into its single jewels, without having to visit the Lahap NPC. Reuses the same
/// server logic as the NPC (<see cref="MUnique.OpenMU.GameLogic.PlayerActions.Items.ItemStackAction"/>),
/// but skips the NPC requirement and the dismantle fee.
/// <list type="bullet">
///   <item><c>/unpack &lt;jewel&gt;</c> unpacks the first matching packed jewel found in the inventory.</item>
/// </list>
/// </summary>
[Guid("A1C4E6B8-7D2F-4E39-8B0A-5C3D9F1E2A47")]
[PlugIn]
[Display(Name = "Unpack Jewels Command", Description = "Handles the /unpack command to split a packed jewel back into single jewels (no NPC, no fee).")]
[ChatCommandHelp(Command, typeof(UnpackJewelChatCommandPlugIn.Arguments), CharacterStatus.Normal)]
public class UnpackJewelChatCommandPlugIn : ChatCommandPlugInBase<UnpackJewelChatCommandPlugIn.Arguments>
{
    private const string Command = "/unpack";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, Arguments arguments)
    {
        if (player.SelectedCharacter is null || player.Inventory is null)
        {
            return;
        }

        if (!JewelPackCommandHelper.TryResolveMixNumber(arguments.Jewel, out var mixNumber))
        {
            await player.ShowBlueMessageAsync($"Usage: /unpack <jewel>. Jewels: {JewelPackCommandHelper.AliasList}").ConfigureAwait(false);
            return;
        }

        var mix = player.GameContext.Configuration.JewelMixes.FirstOrDefault(m => m.Number == mixNumber);
        if (mix?.MixedJewel is null)
        {
            await player.ShowBlueMessageAsync("That jewel cannot be unpacked on this server.").ConfigureAwait(false);
            return;
        }

        var packed = player.Inventory.Items.FirstOrDefault(item => item.Definition == mix.MixedJewel);
        if (packed is null)
        {
            await player.ShowBlueMessageAsync($"You have no {mix.MixedJewel.Name} in your inventory.").ConfigureAwait(false);
            return;
        }

        var pieces = (packed.Level + 1) * 10;

        var success = await JewelPackCommandHelper.StackAction
            .UnstackItemsAsync(player, mixNumber, packed.ItemSlot, requireOpenedNpc: false, chargeFee: false)
            .ConfigureAwait(false);

        if (success)
        {
            await player.ShowBlueMessageAsync($"Unpacked into {pieces} {mix.SingleJewel?.Name}.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Arguments for the <c>/unpack</c> command, parsed positionally.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the jewel alias to unpack (e.g. bless, soul, chaos, life).
        /// </summary>
        public string? Jewel { get; set; }
    }
}
