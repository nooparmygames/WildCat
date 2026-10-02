using System.Security.Cryptography;

namespace WildCat.Grains
{
    /// <summary>
    /// Codes for email activation and password reset.
    /// </summary>
    public static class VerificationCode
    {
        private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>
        /// Returns a random code of upper-case letters and digits. It uses the cryptographic
        /// generator so codes cannot be predicted from earlier ones.
        /// </summary>
        public static string Create(int length)
        {
            return RandomNumberGenerator.GetString(Characters, length);
        }
    }
}
