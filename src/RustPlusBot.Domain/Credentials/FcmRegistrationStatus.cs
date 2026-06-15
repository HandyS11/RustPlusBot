namespace RustPlusBot.Domain.Credentials;

/// <summary>Lifecycle state of a user's FCM listener registration.</summary>
public enum FcmRegistrationStatus
{
    /// <summary>Credentials accepted; the listener should run.</summary>
    Active = 0,

    /// <summary>FCM rejected the credentials; the user must reconnect.</summary>
    Expired = 1,

    /// <summary>Removed by the user; the listener must not run.</summary>
    Disabled = 2,
}
