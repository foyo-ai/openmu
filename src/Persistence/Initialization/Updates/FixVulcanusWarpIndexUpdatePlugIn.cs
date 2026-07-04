// <copyright file="FixVulcanusWarpIndexUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// This update fixes the Vulcanus warp index so the map is reachable through the
/// move (M) command window.
/// </summary>
/// <remarks>
/// The client's move request data (movereq_*.bmd) sends warp index 42 for
/// Vulcanus, but the warp list registered it at index 37. Because the indices
/// did not match, the server answered every attempt with "Unknown warp index"
/// and the character stayed put. Aligning the server index to 42 (which was
/// otherwise unused) lets the warp resolve. The destination gate (294) is
/// unchanged, so this does not affect the duel system, which reaches Vulcanus
/// and the Duel Arena through gates rather than the warp list.
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("17FE4497-47E1-4358-B3FE-53F4A451B5A6")]
public class FixVulcanusWarpIndexUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Fix Vulcanus Warp Index";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Changes the Vulcanus warp index to 42 so the map can be reached through the move (M) command window.";

    /// <summary>
    /// The warp index the client sends for Vulcanus.
    /// </summary>
    private const short ClientVulcanusWarpIndex = 42;

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.FixVulcanusWarpIndex;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 04, 00, 00, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        var vulcanus = gameConfiguration.WarpList.FirstOrDefault(w => w.Name == "Vulcanus");
        if (vulcanus is not null)
        {
            vulcanus.Index = ClientVulcanusWarpIndex;
        }

        return ValueTask.CompletedTask;
    }
}
