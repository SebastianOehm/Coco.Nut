namespace CocoNut.Core.Ups;

/// <summary>
/// How the output power (watts) of the UPS is determined. Selected once per connection, in the order declared here
/// (after <see cref="Unavailable"/>): see WinNUT PR #112 and issue #150.
/// </summary>
public enum PowerMethod
{
    /// <summary>No method available to determine power.</summary>
    Unavailable,
    /// <summary><c>ups.realpower</c> is read directly.</summary>
    RealPower,
    /// <summary><c>output.realpower</c> is read directly.</summary>
    RealOutputPower,
    /// <summary><c>ups.realpower.nominal</c> × <c>ups.load</c> / 100.</summary>
    RealPowerNominalLoadPercent,
    /// <summary><c>input.current.nominal</c> × <c>input.voltage.nominal</c> × power factor × <c>ups.load</c> / 100.</summary>
    InputNominalVaLoadPercent,
    /// <summary><c>output.current</c> × <c>output.voltage</c> × power factor.</summary>
    OutputVaCalculation,
}
