// <copyright file="ItemIsJewelTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using NUnit.Framework;
using GameConfiguration = MUnique.OpenMU.Persistence.BasicModel.GameConfiguration;
using Item = MUnique.OpenMU.Persistence.BasicModel.Item;
using ItemDefinition = MUnique.OpenMU.Persistence.BasicModel.ItemDefinition;
using JewelMix = MUnique.OpenMU.Persistence.BasicModel.JewelMix;

/// <summary>
/// Tests for <see cref="ItemExtensions.IsJewel"/>. Reproduces the offline-pickup bug where the
/// mixed misc item group 14 (jewels + Devil's Eye/Key + potions) was all treated as "jewel".
/// </summary>
[TestFixture]
public class ItemIsJewelTests
{
    private GameConfiguration _config = null!;
    private ItemDefinition _bless = null!;
    private ItemDefinition _chaos = null!;
    private ItemDefinition _devilsEye = null!;
    private ItemDefinition _devilsKey = null!;
    private ItemDefinition _potion = null!;

    [SetUp]
    public void SetUp()
    {
        this._config = new GameConfiguration();

        // Real (group, number) pairs from the Season 6 configuration.
        this._bless = new ItemDefinition { Group = 14, Number = 13, Name = "Jewel of Bless" };
        this._chaos = new ItemDefinition { Group = 12, Number = 15, Name = "Jewel of Chaos" }; // note: different group!
        this._devilsEye = new ItemDefinition { Group = 14, Number = 17, Name = "Devil's Eye" };
        this._devilsKey = new ItemDefinition { Group = 14, Number = 18, Name = "Devil's Key" };
        this._potion = new ItemDefinition { Group = 14, Number = 35, Name = "Small Shield Potion" };

        // JewelMixes is the authoritative jewel whitelist (SingleJewel).
        this._config.JewelMixes.Add(new JewelMix { SingleJewel = this._bless });
        this._config.JewelMixes.Add(new JewelMix { SingleJewel = this._chaos });
    }

    private static Item ItemOf(ItemDefinition definition) => new() { Definition = definition };

    [Test]
    public void JewelOfBless_IsJewel()
    {
        Assert.That(ItemOf(this._bless).IsJewel(this._config), Is.True);
    }

    [Test]
    public void JewelOfChaos_InDifferentGroup_IsJewel()
    {
        // Regression: Chaos lives in group 12, so a plain "group == 14" check missed it.
        Assert.That(ItemOf(this._chaos).IsJewel(this._config), Is.True);
    }

    [Test]
    public void DevilsEye_Group14_IsNotJewel()
    {
        // The reported bug: "con mắt" was picked up because it shares group 14 with jewels.
        Assert.That(ItemOf(this._devilsEye).IsJewel(this._config), Is.False);
    }

    [Test]
    public void DevilsKey_Group14_IsNotJewel()
    {
        // The reported bug: "chìa khóa" was picked up because it shares group 14 with jewels.
        Assert.That(ItemOf(this._devilsKey).IsJewel(this._config), Is.False);
    }

    [Test]
    public void Group14Potion_IsNotJewel()
    {
        Assert.That(ItemOf(this._potion).IsJewel(this._config), Is.False);
    }

    [Test]
    public void ItemWithoutDefinition_IsNotJewel()
    {
        Assert.That(new Item().IsJewel(this._config), Is.False);
    }
}
