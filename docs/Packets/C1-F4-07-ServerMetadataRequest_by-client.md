# C1 F4 07 - ServerMetadataRequest (by client)

## Is sent when

This packet is sent by the (open source) client right after it received the server list, to ask for the server display metadata (name, pvp flag, group, subtitle).

## Causes the following actions on the server side

The server will send a ServerMetadataResponse back to the client. Older servers ignore this packet, so the client keeps its local ServerList.bmd data.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC1  | [Packet type](PacketTypes.md) |
| 1 | 1 |    Byte   |   4   | Packet header - length of the packet |
| 2 | 1 |    Byte   | 0xF4  | Packet header - packet type identifier |
| 3 | 1 |    Byte   | 0x07  | Packet header - sub packet type identifier |