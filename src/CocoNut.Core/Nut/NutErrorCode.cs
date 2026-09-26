namespace CocoNut.Core.Nut;

/// <summary>Error codes of the NUT network protocol (<c>ERR &lt;code&gt;</c>), see docs/net-protocol.txt of NUT.</summary>
public enum NutErrorCode
{
    Unrecognized,
    AccessDenied,
    UnknownUps,
    VarNotSupported,
    CmdNotSupported,
    InvalidArgument,
    InstCmdFailed,
    SetFailed,
    ReadOnly,
    TooLong,
    FeatureNotSupported,
    FeatureNotConfigured,
    AlreadySslMode,
    DriverNotConnected,
    DataStale,
    AlreadyLoggedIn,
    InvalidPassword,
    AlreadySetPassword,
    InvalidUsername,
    AlreadySetUsername,
    UsernameRequired,
    PasswordRequired,
    UnknownCommand,
    InvalidValue,
}
