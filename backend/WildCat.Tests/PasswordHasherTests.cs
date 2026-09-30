using System.Security.Cryptography;
using System.Text;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PasswordHasher"/>. Each full hash or verify costs 210,000 PBKDF2
    /// iterations, so the tests use as few as they need.
    /// </summary>
    public class PasswordHasherTests
    {
        [Fact]
        public void HashHasTheStoredFormatAndVerifiesOnlyTheSamePassword()
        {
            string hash = PasswordHasher.Hash("correct horse battery staple");

            string[] parts = hash.Split('$');
            Assert.Equal(4, parts.Length);
            Assert.Equal("pbkdf2-sha512", parts[0]);
            Assert.Equal("210000", parts[1]);
            Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
            Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
            Assert.DoesNotContain("horse", hash);

            Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
            Assert.False(PasswordHasher.Verify("correct horse battery stapler", hash));
        }

        [Fact]
        public void SamePasswordGetsADifferentSaltEachTime()
        {
            Assert.NotEqual(PasswordHasher.Hash("same password"), PasswordHasher.Hash("same password"));
        }

        [Fact]
        public void VerifyReadsTheIterationCountFromTheStoredHash()
        {
            // A hash made with another iteration count, as an older or newer setting would make.
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("pw"), salt, 1000, HashAlgorithmName.SHA512, 32);
            string stored = $"pbkdf2-sha512$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

            Assert.True(PasswordHasher.Verify("pw", stored));
            Assert.False(PasswordHasher.Verify("pW", stored));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EmptyPasswordsAreRejected(string password)
        {
            Assert.False(PasswordHasher.IsAcceptable(password));
            Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(password));
            Assert.False(PasswordHasher.Verify(password, "pbkdf2-sha512$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA"));
        }

        [Fact]
        public void PasswordsLongerThan256CharactersAreRejected()
        {
            Assert.True(PasswordHasher.IsAcceptable(new string('a', 256)));
            Assert.False(PasswordHasher.IsAcceptable(new string('a', 257)));
            Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(new string('a', 257)));
            Assert.False(PasswordHasher.Verify(new string('a', 257), "pbkdf2-sha512$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8")] // a legacy unsalted SHA1
        [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
        [InlineData("pbkdf2-sha512$abc$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
        [InlineData("pbkdf2-sha512$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
        [InlineData("pbkdf2-sha512$-5$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
        [InlineData("pbkdf2-sha512$1000$not base64!$AAAA")]
        [InlineData("pbkdf2-sha512$1000$$AAAA")]
        [InlineData("pbkdf2-sha512$1000$AAAAAAAAAAAAAAAAAAAAAA==")]
        public void MalformedStoredHashesNeverVerify(string stored)
        {
            Assert.False(PasswordHasher.Verify("password", stored));
        }
    }
}
