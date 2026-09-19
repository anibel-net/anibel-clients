using System.Text.Json;
using Anibel.App.Core;

namespace Anibel.App.Services;

public sealed record SessionSnapshot(ulong Revision, bool Authenticated, string? Username, string? UserId, string? Avatar);

/// <summary>Core session projection plus Windows protected credential storage.</summary>
public sealed class SessionService(ICoreClient core, ICredentialStore credentials)
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private SessionSnapshot? _snapshot;
    public ulong Revision => _snapshot?.Revision ?? 0;
    public bool HasSession => _snapshot?.Authenticated == true;
    public string? Username => _snapshot?.Username;
    public string? UserId => _snapshot?.UserId;
    public string? Avatar => _snapshot?.Avatar;

    public async Task RestoreAsync()
    {
        await _operations.WaitAsync();
        try
        {
            var user = await credentials.ReadAsync();
            if (user is not null)
                await core.CallAsync<JsonElement>("setToken", new
                {
                    token = user.Token,
                    username = user.Username,
                    id = user.Id,
                    avatar = user.Avatar
                });
            await RefreshLockedAsync();
        }
        finally { _operations.Release(); }
    }
    public async Task LoginAsync(string username, string password)
    {
        await _operations.WaitAsync();
        try
        {
            var user = await core.LoginAsync(username, password);
            try
            {
                await credentials.WriteAsync(user);
            }
            finally { await RefreshLockedAsync(); }
        }
        finally { _operations.Release(); }
    }
    public async Task LogoutAsync()
    {
        await _operations.WaitAsync();
        try
        {
            await core.LogoutAsync();
            await RefreshLockedAsync();
        }
        finally { _operations.Release(); }
    }
    public async Task RefreshAsync()
    {
        await _operations.WaitAsync();
        try
        {
            await RefreshLockedAsync();
        }
        finally { _operations.Release(); }
    }
    private async Task RefreshLockedAsync()
    {
        var current = await core.CallAsync<SessionSnapshot>("session");
        if (current == _snapshot || current.Revision < Revision)
            return;
        _snapshot = current;
        if (!current.Authenticated)
            await credentials.ClearAsync();
        else if (await credentials.ReadAsync() is { } stored && stored.Id == current.UserId
            && (stored.Avatar != current.Avatar || stored.Username != current.Username))
            await credentials.WriteAsync(stored with { Avatar = current.Avatar, Username = current.Username ?? stored.Username });
    }
}
