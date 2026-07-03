// <copyright file="ServerMetadataRequestHandler.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.ConnectServer.PacketHandler;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;

/// <summary>
/// Handles the request of the server display metadata (name, pvp flag, group,
/// subtitle) which the client uses on its server-select screen instead of its
/// local ServerList.bmd values.
/// </summary>
internal class ServerMetadataRequestHandler : IPacketHandler<Client>
{
    private readonly IConnectServer _connectServer;
    private readonly ILogger<ServerMetadataRequestHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerMetadataRequestHandler" /> class.
    /// </summary>
    /// <param name="connectServer">The connect server.</param>
    /// <param name="logger">The logger.</param>
    public ServerMetadataRequestHandler(IConnectServer connectServer, ILogger<ServerMetadataRequestHandler> logger)
    {
        this._connectServer = connectServer;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Client client, Memory<byte> packet)
    {
        this._logger.LogDebug("Client {0}:{1} requested Server Metadata", client.Address, client.Port);

        int WritePacket()
        {
            var metadata = this._connectServer.ServerList.SerializeMetadata();
            var span = client.Connection.Output.GetSpan(metadata.Length)[..metadata.Length];
            metadata.CopyTo(span);
            return span.Length;
        }

        await client.Connection.SendAsync(WritePacket).ConfigureAwait(false);
    }
}
