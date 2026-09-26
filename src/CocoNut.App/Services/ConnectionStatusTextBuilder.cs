using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Builds the localized connection-state text shared by the main window's status line and the tray icon's
/// tooltip, from a <see cref="MonitorState"/> and the error (if any) that came with it.
/// </summary>
public static class ConnectionStatusTextBuilder
{
    /// <summary>
    /// The base state text (<see cref="Strings.Main_Status_NotConnected"/>/<see cref="Strings.Main_Status_Connecting"/>/
    /// <see cref="Strings.Main_Status_Connected"/>/<see cref="Strings.Main_Status_Reconnecting"/>), with a short
    /// description of <paramref name="error"/> appended when one is present.
    /// </summary>
    public static string Build(MonitorState state, Exception? error)
    {
        string baseText = state switch
        {
            MonitorState.Connecting => Strings.Main_Status_Connecting,
            MonitorState.Connected => Strings.Main_Status_Connected,
            MonitorState.Reconnecting => Strings.Main_Status_Reconnecting,
            _ => Strings.Main_Status_NotConnected,
        };

        string? errorText = DescribeError(error);
        return errorText is null ? baseText : $"{baseText} – {errorText}";
    }

    /// <summary>
    /// A short, localized description of <paramref name="error"/> where WinNUT had a dedicated status string
    /// (invalid credentials, unknown UPS name), falling back to the exception's own message otherwise.
    /// </summary>
    public static string? DescribeError(Exception? error) => error switch
    {
        null => null,
        NutException { ErrorCode: NutErrorCode.UnknownUps } => Strings.Main_Status_UnknownUps,
        NutException { ErrorCode: NutErrorCode.InvalidPassword or NutErrorCode.InvalidUsername or NutErrorCode.AccessDenied
            or NutErrorCode.UsernameRequired or NutErrorCode.PasswordRequired } => Strings.Main_Status_InvalidLogin,
        _ => error.Message,
    };
}
