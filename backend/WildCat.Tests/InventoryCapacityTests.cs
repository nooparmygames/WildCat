using Orleans;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests;

[Collection(ClusterCollection.Name)]
public class InventoryCapacityTests(ClusterFixture fixture)
{
    private IGrainFactory Grains => fixture.Cluster.Client;

    private IPlayerInventory Inventory(string game) => Grains.GetGrain<IPlayerInventory>(
        ResourcesUtility.GetPlayerInventoryKey(game, TestSetup.UniqueId("inventory-player")));

    private IItem Item(string game, string name) => Grains.GetGrain<IItem>(ResourcesUtility.GetItemKey(game, name));

    private async Task<IPackageContainer> Package(string game, IInventoryItem[] rewards,
        bool locked = false, PlayerInventoryState.InInventoryItemState[] unlockItems = null,
        PlayerInventoryState.InInventoryItemState[] price = null)
    {
        var package = Grains.GetGrain<IPackageContainer>(ResourcesUtility.GetPackageKey(game, TestSetup.UniqueId("package")));
        await package.Initialize("Package", "Inventory test", rewards.Select(x => new PackageContainerState.PackageItemInfo
        {
            Item = x,
            Count = 7
        }).ToList(), locked, unlockItems?.ToList(), false, 0, price?.ToList(), -1).WaitAsync(TestSetup.Timeout);
        return package;
    }

    private static int Count(PlayerInventoryState state, IInventoryItem item) =>
        state.items.SingleOrDefault(x => x.Item.GetPrimaryKeyString() == item.GetPrimaryKeyString()).Count;

    [Fact]
    public async Task FullInventoryAcceptsStackUpdatesAndRejectsAdditionalStacks()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var gem = Item(game, "gem");
        await inventory.SetSize(1).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { coin, coin }, new[] { 1000000, 1000000 }).WaitAsync(TestSetup.Timeout));
        Assert.True(await inventory.AddItem(new IInventoryItem[] { coin }, new[] { 3 }).WaitAsync(TestSetup.Timeout));
        Assert.True((await inventory.GetInventory().WaitAsync(TestSetup.Timeout)).IsFull());
        await inventory.SetItem(new IInventoryItem[] { coin }, new[] { 12 }).WaitAsync(TestSetup.Timeout);

        Assert.False(await inventory.AddItem(new IInventoryItem[] { gem }, new[] { 1 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.AddItem(new IInventoryItem[] { coin, gem }, new[] { 5, 1 }).WaitAsync(TestSetup.Timeout));
        await inventory.SetItem(new IInventoryItem[] { coin, gem }, new[] { 99, 1 }).WaitAsync(TestSetup.Timeout);
        await fixture.Cluster.Deactivate(inventory);
        var saved = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Single(saved.items);
        Assert.Equal(12, Count(saved, coin));
    }

    [Fact]
    public async Task BatchCapacityCountsEveryNewStackBeforeChangingAnything()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var gem = Item(game, "gem");
        var potion = Item(game, "potion");
        await inventory.SetSize(2).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { coin }, new[] { 1 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.AddItem(new IInventoryItem[] { gem, potion }, new[] { 1, 1 }).WaitAsync(TestSetup.Timeout));
        Assert.Single((await inventory.GetInventory().WaitAsync(TestSetup.Timeout)).items);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { gem, gem }, new[] { 2, 3 }).WaitAsync(TestSetup.Timeout));
        Assert.Equal(5, Count(await inventory.GetInventory().WaitAsync(TestSetup.Timeout), gem));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task NonpositiveCapacityAllowsAnyNumberOfStacks(int capacity)
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        await inventory.SetSize(capacity).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { Item(game, "coin"), Item(game, "gem") }, new[] { 1, 1 })
            .WaitAsync(TestSetup.Timeout));
        Assert.False((await inventory.GetInventory().WaitAsync(TestSetup.Timeout)).IsFull());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurchasesCountStacksAfterRemovingTheirPrice(bool buyPackage)
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var price = new[] { new PlayerInventoryState.InInventoryItemState { Item = coin, Count = 10 } };
        IInventoryItem purchase;
        if (buyPackage)
        {
            purchase = await Package(game, Array.Empty<IInventoryItem>(), price: price);
        }
        else
        {
            var item = Item(game, "potion");
            await item.Initialize("Potion", "Inventory test", null, 100, true, false, TimeSpan.Zero,
                true, false, 0, TimeSpan.Zero, price.ToList()).WaitAsync(TestSetup.Timeout);
            purchase = item;
        }
        Task<bool> Buy() => buyPackage
            ? inventory.BuyPackage((IPackageContainer)purchase, 1)
            : inventory.BuyItem(new[] { (IItem)purchase }, new[] { 1 });

        await inventory.SetSize(1).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { coin }, new[] { 20 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await Buy().WaitAsync(TestSetup.Timeout));
        Assert.Equal(20, Count(await inventory.GetInventory().WaitAsync(TestSetup.Timeout), coin));
        Assert.True(await inventory.RemoveItem(new IInventoryItem[] { coin }, new[] { 10 }).WaitAsync(TestSetup.Timeout));
        Assert.True(await Buy().WaitAsync(TestSetup.Timeout));
        var result = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Single(result.items);
        Assert.Equal(1, Count(result, purchase));
        Assert.Equal(0, Count(result, coin));
    }

    [Fact]
    public async Task OpeningConsumesOnePackageAndPersistsBothConsumptionAndRewards()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var package = await Package(game, new IInventoryItem[] { coin });
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package }, new[] { 2 }).WaitAsync(TestSetup.Timeout));
        Assert.True(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        await fixture.Cluster.Deactivate(inventory);
        var once = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Equal(1, Count(once, package));
        Assert.Equal(7, Count(once, coin));

        Assert.True(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        await fixture.Cluster.Deactivate(inventory);
        var twice = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Single(twice.items);
        Assert.Equal(14, Count(twice, coin));
    }

    [Fact]
    public async Task LastPackageFreesAStackForItsRewards()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var package = await Package(game, new IInventoryItem[] { coin });
        await inventory.SetSize(1).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package }, new[] { 1 }).WaitAsync(TestSetup.Timeout));
        Assert.True(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        Assert.Equal(7, Count(await inventory.GetInventory().WaitAsync(TestSetup.Timeout), coin));
    }

    [Fact]
    public async Task RemainingPackagesStillNeedAStackAlongsideTheirRewards()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var package = await Package(game, new IInventoryItem[] { coin });
        await inventory.SetSize(1).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package }, new[] { 2 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        var result = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Single(result.items);
        Assert.Equal(2, Count(result, package));
    }

    [Fact]
    public async Task FailedOpenRestoresThePackageAndEveryUnlockItem()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var key = Item(game, "key");
        var coin = Item(game, "coin");
        var gem = Item(game, "gem");
        var package = await Package(game, new IInventoryItem[] { coin, gem }, locked: true,
            unlockItems: new[] { new PlayerInventoryState.InInventoryItemState { Item = key, Count = 1 } });
        await inventory.SetSize(2).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package, key }, new[] { 1, 2 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        await fixture.Cluster.Deactivate(inventory);
        var failed = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Equal(2, failed.items.Count);
        Assert.Equal(1, Count(failed, package));
        Assert.Equal(2, Count(failed, key));

        await inventory.SetSize(3).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        var opened = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Equal(3, opened.items.Count);
        Assert.Equal(1, Count(opened, key));
        Assert.Equal(7, Count(opened, coin));
        Assert.Equal(7, Count(opened, gem));
    }

    [Fact]
    public async Task APackageCannotSupplyItsOwnUnlockCostAfterItHasBeenConsumed()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var package = await Package(game, Array.Empty<IInventoryItem>());
        await package.SetProperties("Package", "Inventory test", new(), true,
            new() { new() { Item = package, Count = 1 } }, false, 0, new(), -1).WaitAsync(TestSetup.Timeout);
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package }, new[] { 1 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        Assert.Equal(1, Count(await inventory.GetInventory().WaitAsync(TestSetup.Timeout), package));
    }

    [Fact]
    public async Task MissingUnlockItemsLeaveThePackageUntouched()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var key = Item(game, "key");
        var package = await Package(game, new IInventoryItem[] { coin }, locked: true,
            unlockItems: new[] { new PlayerInventoryState.InInventoryItemState { Item = key, Count = 1 } });
        Assert.True(await inventory.AddItem(new IInventoryItem[] { package }, new[] { 1 }).WaitAsync(TestSetup.Timeout));
        Assert.False(await inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        await fixture.Cluster.Deactivate(inventory);
        var result = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Single(result.items);
        Assert.Equal(1, Count(result, package));
        Assert.Equal(0, Count(result, coin));
    }

    [Fact]
    public async Task RewardOverflowDoesNotConsumeThePackage()
    {
        string game = Guid.NewGuid().ToString();
        var inventory = Inventory(game);
        var coin = Item(game, "coin");
        var package = await Package(game, new IInventoryItem[] { coin });
        Assert.True(await inventory.AddItem(new IInventoryItem[] { coin, package }, new[] { int.MaxValue, 1 }).WaitAsync(TestSetup.Timeout));
        await Assert.ThrowsAsync<OverflowException>(() => inventory.OpenAndUnlockPackage(package).WaitAsync(TestSetup.Timeout));
        await fixture.Cluster.Deactivate(inventory);
        var result = await inventory.GetInventory().WaitAsync(TestSetup.Timeout);
        Assert.Equal(int.MaxValue, Count(result, coin));
        Assert.Equal(1, Count(result, package));
    }
}
