using WildCat.Interfaces;

namespace WildCat.Grains;

/// <summary>
/// Runs password work on the thread pool without accessing grain state. Grain callers resume
/// on their own scheduler after awaiting the result.
/// </summary>
public class PasswordHashing
{
    public Task<string> HashAsync(string password) => Task.Run(() => Hash(password));

    public Task<bool> VerifyAsync(string password, string storedHash) =>
        Task.Run(() => Verify(password, storedHash));

    protected virtual string Hash(string password) => PasswordHasher.Hash(password);

    protected virtual bool Verify(string password, string storedHash) =>
        PasswordHasher.Verify(password, storedHash);
}
