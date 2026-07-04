// <copyright file="CharacterRankingEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence;

/// <summary>
/// A lightweight ranking row for one character, projected directly by the database
/// instead of loading the whole character object graph.
/// </summary>
/// <param name="Name">The character name.</param>
/// <param name="CharacterClassId">The identifier of the character class.</param>
/// <param name="PlayerKillCount">The player kill count.</param>
/// <param name="Resets">The value of the reset stat attribute.</param>
/// <param name="Level">The value of the level stat attribute.</param>
public record CharacterRankingEntry(string Name, Guid? CharacterClassId, int PlayerKillCount, float Resets, float Level);
