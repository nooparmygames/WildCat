using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Developer passwords and the developer secret checks in DeveloperStorage.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class DeveloperSecurityTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public DeveloperSecurityTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task DeveloperPasswordIsHashedAndCanBeChanged()
        {
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory, "first-password"));

            string stored = (await developer.GetDeveloperData()).PasswordHash;
            Assert.StartsWith("pbkdf2-sha512$210000$", stored);
            Assert.True(await developer.IsPasswordCorrect("first-password"));
            Assert.False(await developer.IsPasswordCorrect("wrong-password"));

            await developer.ChangePassword("second-password");
            Assert.False(await developer.IsPasswordCorrect("first-password"));
            Assert.True(await developer.IsPasswordCorrect("second-password"));
        }

        [Fact]
        public async Task DeveloperWithoutPasswordCannotLogInWithOne()
        {
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory));

            Assert.Equal("", (await developer.GetDeveloperData()).PasswordHash);
            Assert.False(await developer.IsPasswordCorrect(""));
            Assert.False(await developer.IsPasswordCorrect("anything"));
        }

        [Fact]
        public async Task DeveloperReadsWritesAndRemovesNeedTheSecret()
        {
            var developerKey = await TestSetup.CreateDeveloper(GrainFactory);
            var secret = (await GrainFactory.GetGrain<IDeveloper>(developerKey).GetDeveloperData()).DeveloperSecret;
            var storage = GrainFactory.GetGrain<IDeveloperStorage>(developerKey);
            var wrong = Guid.NewGuid();

            await storage.SetString("motd", "hello", secret);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetString("motd", "changed", wrong));

            // Game clients read single values without a secret (message 34).
            Assert.Equal("hello", await storage.GetString("motd"));
            // The developer read (message 64) needs it.
            Assert.Equal("hello", await storage.GetStringForDeveloper("motd", secret));
            Assert.Null(await storage.GetStringForDeveloper("missing", secret));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetStringForDeveloper("motd", wrong));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetStringForDeveloper("motd", Guid.Empty));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAllStringData(wrong));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAllKeys(wrong));

            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RemoveString("motd", wrong));
            Assert.Equal("hello", await storage.GetString("motd"));
            await storage.RemoveString("motd", secret);
            Assert.Null(await storage.GetString("motd"));

            await storage.SetBinary("blob", new byte[] { 1, 2, 3 }, secret);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RemoveBinary("blob", wrong));
            Assert.Equal(new byte[] { 1, 2, 3 }, await storage.GetBinary("blob"));
            await storage.RemoveBinary("blob", secret);
            Assert.Null(await storage.GetBinary("blob"));
        }

        [Fact]
        public async Task RegeneratedSecretReplacesTheOldOneAtOnce()
        {
            var developerKey = await TestSetup.CreateDeveloper(GrainFactory);
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            var storage = GrainFactory.GetGrain<IDeveloperStorage>(developerKey);
            var oldSecret = (await developer.GetDeveloperData()).DeveloperSecret;
            await storage.SetString("k", "v1", oldSecret);

            var newSecret = await developer.GenerateSecret();

            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetString("k", "v2", oldSecret));
            await storage.SetString("k", "v2", newSecret);
            Assert.Equal("v2", await storage.GetStringForDeveloper("k", newSecret));
        }

        [Fact]
        public async Task StorageOfAnUnknownDeveloperAcceptsNoSecret()
        {
            var storage = GrainFactory.GetGrain<IDeveloperStorage>(Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetString("k", "v", Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetStringForDeveloper("k", Guid.Empty));
            Assert.Null(await storage.GetString("k"));
        }
    }
}
