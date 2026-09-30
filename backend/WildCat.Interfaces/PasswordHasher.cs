using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Server-side password hashing for players and developers. Clients send the plain password
    /// over TLS; only the hash produced here is stored.
    /// </summary>
    /// <remarks>
    /// PBKDF2 with HMAC-SHA512, 210,000 iterations, a random 16-byte salt per password and a
    /// 32-byte derived key. The stored form is "pbkdf2-sha512$iterations$salt$hash" with base64
    /// salt and hash. <see cref="Verify"/> reads the iteration count from the stored string, so the
    /// count can be raised later without invalidating existing hashes.
    /// </remarks>
    public static class PasswordHasher
    {
        public const int Iterations = 210_000;
        public const int MaxPasswordLength = 256;

        private const string Scheme = "pbkdf2-sha512";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

        /// <summary>
        /// True when the password is non-empty and at most <see cref="MaxPasswordLength"/> characters.
        /// </summary>
        public static bool IsAcceptable(string password)
        {
            return !string.IsNullOrEmpty(password) && password.Length <= MaxPasswordLength;
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when <see cref="IsAcceptable"/> is false. Call it
        /// before claiming a user name or email so a bad password does not leave a half-made account.
        /// </summary>
        public static void EnsureAcceptable(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("The password cannot be empty.", nameof(password));
            if (password.Length > MaxPasswordLength)
                throw new ArgumentException($"The password cannot be longer than {MaxPasswordLength} characters.", nameof(password));
        }

        public static string Hash(string password)
        {
            EnsureAcceptable(password);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, Algorithm, HashSize);
            return string.Join('$',
                Scheme,
                Iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        /// <summary>
        /// Runs <see cref="Hash"/> on the thread pool. PBKDF2 takes a few hundred milliseconds by
        /// design, and a grain that hashed inline would hold its turn (and every queued call) for
        /// that long. Awaiting this from a grain returns to the grain's scheduler afterwards; the
        /// hashing itself touches no grain state.
        /// </summary>
        public static Task<string> HashAsync(string password)
        {
            return Task.Run(() => Hash(password));
        }

        /// <summary>
        /// Runs <see cref="Verify"/> on the thread pool, for the same reason as <see cref="HashAsync"/>.
        /// Read the stored hash from grain state before calling, as the argument does.
        /// </summary>
        public static Task<bool> VerifyAsync(string password, string storedHash)
        {
            return Task.Run(() => Verify(password, storedHash));
        }

        /// <summary>
        /// Checks a password against a hash made by <see cref="Hash"/>. Returns false, never throws,
        /// for an unacceptable password or a stored value that is empty or not in the expected format.
        /// </summary>
        public static bool Verify(string password, string storedHash)
        {
            if (!IsAcceptable(password) || string.IsNullOrEmpty(storedHash))
                return false;

            string[] parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme)
                return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) || iterations < 1)
                return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }
            if (salt.Length == 0 || expected.Length == 0)
                return false;

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
