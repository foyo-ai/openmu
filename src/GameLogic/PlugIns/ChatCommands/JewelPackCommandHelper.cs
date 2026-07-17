// <copyright file="JewelPackCommandHelper.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using MUnique.OpenMU.GameLogic.PlayerActions.Items;

/// <summary>
/// Shared helpers for the <c>/pack</c> and <c>/unpack</c> jewel commands: the alias-to-jewel-mix
/// mapping (the mix numbers configured in <c>GameConfigurationInitializer.CreateJewelMixes</c>) and
/// the single <see cref="ItemStackAction"/> that both commands reuse.
/// </summary>
internal static class JewelPackCommandHelper
{
    /// <summary>
    /// The shared, stateless action that performs the actual pack/unpack (same logic as the Lahap NPC).
    /// </summary>
    public static readonly ItemStackAction StackAction = new();

    /// <summary>
    /// A human-readable list of the accepted jewel aliases, shown in the usage messages.
    /// </summary>
    public const string AliasList = "bless, soul, life, creation, guardian, gem, harmony, chaos, lower, higher";

    private static readonly IReadOnlyDictionary<string, byte> AliasToMixNumber = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["bless"] = 0,
        ["soul"] = 1,
        ["life"] = 2,
        ["creation"] = 3,
        ["guardian"] = 4,
        ["gemstone"] = 5,
        ["gem"] = 5,
        ["harmony"] = 6,
        ["chaos"] = 7,
        ["lower"] = 8,
        ["lrs"] = 8,
        ["higher"] = 9,
        ["hrs"] = 9,
    };

    /// <summary>
    /// Resolves a jewel alias (e.g. "bless") to its jewel-mix number.
    /// </summary>
    /// <param name="alias">The alias typed by the player.</param>
    /// <param name="mixNumber">The resolved jewel-mix number.</param>
    /// <returns><c>true</c> if the alias is known; otherwise <c>false</c>.</returns>
    public static bool TryResolveMixNumber(string? alias, out byte mixNumber)
    {
        mixNumber = 0;
        return !string.IsNullOrWhiteSpace(alias) && AliasToMixNumber.TryGetValue(alias.Trim(), out mixNumber);
    }
}
