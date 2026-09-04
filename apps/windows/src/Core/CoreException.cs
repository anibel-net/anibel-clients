namespace Anibel.App.Core;

/// <summary>Raised for `ok:false` responses from the Rust core.</summary>
public sealed class CoreException : Exception
{
    public CoreException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public string UserMessage => Services.Ui.FriendlyError(Code, Message);
}

/// <summary>Core response envelope: { id, ok, value | error }.</summary>
public sealed record CoreErrorDto(string Code, string Message);
