// <copyright file="ServerInfo.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Interfaces;

/// <summary>
/// The state info about a server.
/// </summary>
public record ServerInfo(ushort Id, string Description, int CurrentConnections, int MaximumConnections)
{
    /// <summary>
    /// Gets or sets the count of current connections.
    /// </summary>
    public int CurrentConnections { get; set; } = CurrentConnections;

    /// <summary>
    /// Gets or sets a value indicating whether PvP is enabled on this server.
    /// Used for the client server-select metadata (Non-PvP marking).
    /// </summary>
    public bool PvpEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the group id which the client uses to group servers on the
    /// server-select screen.
    /// </summary>
    public byte GroupId { get; set; }

    /// <summary>
    /// Gets or sets the sort order of the server on the server-select screen.
    /// </summary>
    public byte SortOrder { get; set; }

    /// <summary>
    /// Gets or sets an optional subtitle shown next to the server name on the
    /// server-select screen.
    /// </summary>
    public string Subtitle { get; set; } = string.Empty;
}