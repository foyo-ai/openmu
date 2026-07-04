// <copyright file="OfflinePlayerMode.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Offline;

/// <summary>
/// Defines what an <see cref="OfflinePlayer"/> does after the real client disconnects.
/// </summary>
public enum OfflinePlayerMode
{
    /// <summary>
    /// The ghost keeps fighting monsters with the MU Helper configuration.
    /// </summary>
    Leveling,

    /// <summary>
    /// The ghost stands still and keeps the personal store open.
    /// The session stops automatically when the store closes (e.g. sold out).
    /// </summary>
    Store,
}
