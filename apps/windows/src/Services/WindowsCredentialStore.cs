using System.Security.Cryptography;
using System.Text.Json;
using Anibel.App.Core;

namespace Anibel.App.Services;

public interface ICredentialStore
{
    Task<LoginUserDto?> ReadAsync();
    Task WriteAsync(LoginUserDto credential);
    Task ClearAsync();
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anibel", "credentials.bin");
    public async Task<LoginUserDto?> ReadAsync()
    {
        if (!File.Exists(FilePath))
            return null;
        var protectedBytes = await File.ReadAllBytesAsync(FilePath);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<LoginUserDto>(bytes);
    }
    public async Task WriteAsync(LoginUserDto credential)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credential), null, DataProtectionScope.CurrentUser);
        var temporary = FilePath + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes);
        File.Move(temporary, FilePath, overwrite: true);
    }
    public Task ClearAsync()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
        return Task.CompletedTask;
    }
}
