# C2 F4 07 - ServerMetadataResponse (by server)

## Is sent when

This packet is sent by the server after the client requested the server display metadata.

## Causes the following actions on the client side

The client shows the servers with the name, pvp flag, group and subtitle provided by the server, instead of the values from its local ServerList.bmd.

## Structure

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 1 |   Byte   | 0xC2  | [Packet type](PacketTypes.md) |
| 1 | 2 |    Short   |      | Packet header - length of the packet |
| 3 | 1 |    Byte   | 0xF4  | Packet header - packet type identifier |
| 4 | 1 |    Byte   | 0x07  | Packet header - sub packet type identifier |
| 5 | 2 | ShortBigEndian |  | ServerCount |
| 7 | ServerMetadataInfo.Length *  | Array of ServerMetadataInfo |  | Servers |

### ServerMetadataInfo Structure

Contains the display metadata of a server: id, pvp flag, group, sort order, name and subtitle.

Length: 96 Bytes

| Index | Length | Data Type | Value | Description |
|-------|--------|-----------|-------|-------------|
| 0 | 2 | ShortLittleEndian |  | ServerId |
| 2 | 1 | Byte |  | PvpFlag; Bit 0 marks the server as Non-PvP (0 = PvP, 1 = Non-PvP), matching the client's IsNonPvP logic. |
| 3 | 1 | Byte |  | GroupId |
| 4 | 1 | Byte |  | SortOrder |
| 5 | 32 | String |  | Name |
| 37 | 59 | String |  | Subtitle |