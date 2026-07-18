// <copyright file="AddCrywolfAndBalgassWarpsUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// This update adds warp list entries for Crywolf, the Barracks of Balgass and the Balgass Refuge,
/// so players can reach these maps with the <c>/move &lt;name&gt;</c> chat command instead of walking
/// (Lorencia → Valley of Loren → Crywolf, then the quest NPCs). The base configuration had no warp
/// for these maps.
/// </summary>
/// <remarks>
/// These warps only affect the <c>/move</c> chat command (which resolves by name, server-side). They
/// do NOT appear in the client's move (M) window, because that list is baked into the client's
/// encrypted movereq_*.bmd data file and is not server-configurable.
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("3D2B7C64-9F1A-4E58-8C0D-6A2B9E4F1C37")]
public class AddCrywolfAndBalgassWarpsUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Add Crywolf and Balgass Warps";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Adds Crywolf, Barracks of Balgass and Balgass Refuge to the warp list, reachable with the /move command.";

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddCrywolfAndBalgassWarps;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 18, 00, 00, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        // index, name, costs, level, mapNumber, gate X1/Y1 (to pick the right exit gate on the map).
        AddWarp(context, gameConfiguration, 35, "Crywolf", 5000, 10, 34, 231, 37);
        AddWarp(context, gameConfiguration, 36, "Barracks", 15000, 400, 41, 29, 79);
        AddWarp(context, gameConfiguration, 37, "Refuge", 15000, 400, 42, 104, 178);
        return ValueTask.CompletedTask;
    }

    private static void AddWarp(IContext context, GameConfiguration gameConfiguration, int index, string name, int costs, int levelRequirement, short mapNumber, byte gateX1, byte gateY1)
    {
        // Idempotent: don't add twice if the update is somehow re-applied.
        if (gameConfiguration.WarpList.Any(w => w.Index == index))
        {
            return;
        }

        var gate = gameConfiguration.Maps
            .FirstOrDefault(m => m.Number == mapNumber)?
            .ExitGates.FirstOrDefault(g => g.X1 == gateX1 && g.Y1 == gateY1);
        if (gate is null)
        {
            return;
        }

        var warpInfo = context.CreateNew<WarpInfo>();
        warpInfo.Index = index;
        warpInfo.Name = name;
        warpInfo.Costs = costs;
        warpInfo.LevelRequirement = levelRequirement;
        warpInfo.Gate = gate;
        gameConfiguration.WarpList.Add(warpInfo);
    }
}
