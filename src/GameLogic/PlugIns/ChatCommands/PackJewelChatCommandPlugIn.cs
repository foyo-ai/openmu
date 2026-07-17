// <copyright file="PackJewelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the <c>/pack</c> command: bundles single jewels from the
/// inventory into a packed jewel, without having to visit the Lahap NPC. Reuses the same server
/// logic as the NPC (<see cref="MUnique.OpenMU.GameLogic.PlayerActions.Items.ItemStackAction"/>),
/// but skips the NPC requirement and the combine fee.
/// <list type="bullet">
///   <item><c>/pack &lt;jewel&gt; &lt;10|20|30&gt;</c> packs that many jewels into one packed jewel.</item>
/// </list>
/// </summary>
[Guid("6E8D0C2F-3A4B-4C1D-9E7F-2B5A6C8D0E13")]
[PlugIn]
[Display(Name = "Pack Jewels Command", Description = "Handles the /pack command to bundle jewels into a packed jewel (no NPC, no fee).")]
[ChatCommandHelp(Command, typeof(PackJewelChatCommandPlugIn.Arguments), CharacterStatus.Normal)]
public class PackJewelChatCommandPlugIn : ChatCommandPlugInBase<PackJewelChatCommandPlugIn.Arguments>
{
    private const string Command = "/pack";

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
            await player.ShowBlueMessageAsync($"Usage: /pack <jewel> <10|20|30>. Jewels: {JewelPackCommandHelper.AliasList}").ConfigureAwait(false);
            return;
        }

        if (arguments.Size is not (10 or 20 or 30))
        {
            await player.ShowBlueMessageAsync("Pack size must be 10, 20 or 30.").ConfigureAwait(false);
            return;
        }

        var mix = player.GameContext.Configuration.JewelMixes.FirstOrDefault(m => m.Number == mixNumber);

        var success = await JewelPackCommandHelper.StackAction
            .StackItemsAsync(player, mixNumber, (byte)arguments.Size, requireOpenedNpc: false, chargeFee: false)
            .ConfigureAwait(false);

        if (success)
        {
            await player.ShowBlueMessageAsync($"Packed {arguments.Size} {mix?.SingleJewel?.Name} into 1 {mix?.MixedJewel?.Name}.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Arguments for the <c>/pack</c> command, parsed positionally.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the jewel alias to pack (e.g. bless, soul, chaos, life).
        /// </summary>
        public string? Jewel { get; set; }

        /// <summary>
        /// Gets or sets the number of jewels to pack: 10, 20 or 30.
        /// </summary>
        public int Size { get; set; }
    }
}
