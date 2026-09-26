using CocoNut.Core.Ups;

namespace CocoNut.Core.Tests.Ups;

public class UpsCalculationsTests
{
    private static Dictionary<string, string> Vars(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    // ---- SelectPowerMethod --------------------------------------------------------------------------------------

    [Fact]
    public void SelectPowerMethod_OutputRealPowerPresent_PrefersRealOutputPower()
    {
        var vars = Vars(("output.realpower", "100"), ("ups.realpower", "100"));

        Assert.Equal(PowerMethod.RealOutputPower, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_UpsRealPowerOnly_UsesRealPower()
    {
        var vars = Vars(("ups.realpower", "100"));

        Assert.Equal(PowerMethod.RealPower, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_RealPowerNominalAndLoad_UsesRealPowerNominalLoadPercent()
    {
        var vars = Vars(("ups.realpower.nominal", "1000"), ("ups.load", "50"));

        Assert.Equal(PowerMethod.RealPowerNominalLoadPercent, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_RealPowerNominalWithoutLoad_FallsThroughToNextMethod()
    {
        var vars = Vars(("ups.realpower.nominal", "1000"), ("output.current", "5"), ("output.voltage", "230"));

        Assert.Equal(PowerMethod.OutputVaCalculation, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_InputNominalVaAndLoad_UsesInputNominalVaLoadPercent()
    {
        var vars = Vars(("input.current.nominal", "10"), ("input.voltage.nominal", "230"), ("ups.load", "50"));

        Assert.Equal(PowerMethod.InputNominalVaLoadPercent, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_OutputCurrentAndVoltage_UsesOutputVaCalculation()
    {
        var vars = Vars(("output.current", "5"), ("output.voltage", "230"));

        Assert.Equal(PowerMethod.OutputVaCalculation, UpsCalculations.SelectPowerMethod(vars));
    }

    [Fact]
    public void SelectPowerMethod_NoUsableVariables_ReturnsUnavailable()
    {
        var vars = Vars(("ups.status", "OL"));

        Assert.Equal(PowerMethod.Unavailable, UpsCalculations.SelectPowerMethod(vars));
    }

    // ---- ComputePower ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputePower_RealOutputPower_ParsesAndRounds()
    {
        var vars = Vars(("output.realpower", "123.42"));

        Assert.Equal(123.4, UpsCalculations.ComputePower(PowerMethod.RealOutputPower, vars, load: null));
    }

    [Fact]
    public void ComputePower_RealPower_ParsesAndRounds()
    {
        var vars = Vars(("ups.realpower", "200.37"));

        Assert.Equal(200.4, UpsCalculations.ComputePower(PowerMethod.RealPower, vars, load: null));
    }

    [Fact]
    public void ComputePower_RealPowerNominalLoadPercent_MultipliesByLoad()
    {
        var vars = Vars(("ups.realpower.nominal", "1000"));

        Assert.Equal(450.0, UpsCalculations.ComputePower(PowerMethod.RealPowerNominalLoadPercent, vars, load: 45.0));
    }

    [Fact]
    public void ComputePower_RealPowerNominalLoadPercent_MissingNominal_ReturnsNull()
    {
        var vars = Vars();

        Assert.Null(UpsCalculations.ComputePower(PowerMethod.RealPowerNominalLoadPercent, vars, load: 45.0));
    }

    [Fact]
    public void ComputePower_InputNominalVaLoadPercent_UsesPowerFactor()
    {
        var vars = Vars(("input.current.nominal", "10"), ("input.voltage.nominal", "230"));

        // 10 * 230 * 0.8 * 50 / 100 = 920
        Assert.Equal(920.0, UpsCalculations.ComputePower(PowerMethod.InputNominalVaLoadPercent, vars, load: 50.0));
    }

    [Fact]
    public void ComputePower_OutputVaCalculation_UsesPowerFactor()
    {
        var vars = Vars(("output.current", "5"), ("output.voltage", "230"));

        // 5 * 230 * 0.8 = 920
        Assert.Equal(920.0, UpsCalculations.ComputePower(PowerMethod.OutputVaCalculation, vars, load: null));
    }

    [Fact]
    public void ComputePower_Unavailable_ReturnsNull()
    {
        var vars = Vars(("output.realpower", "123"));

        Assert.Null(UpsCalculations.ComputePower(PowerMethod.Unavailable, vars, load: null));
    }

    [Fact]
    public void ComputePower_NullLoad_TreatedAsZero()
    {
        var vars = Vars(("ups.realpower.nominal", "1000"));

        Assert.Equal(0.0, UpsCalculations.ComputePower(PowerMethod.RealPowerNominalLoadPercent, vars, load: null));
    }

    // ---- EstimateBatteryChargeFromVoltage ---------------------------------------------------------------------------

    [Theory]
    [InlineData(13.0, 70.0)]
    [InlineData(12.0, 20.0)]
    [InlineData(24.0, 20.0)]
    [InlineData(100.0, 45.0)]
    public void EstimateBatteryChargeFromVoltage_HandComputedValues(double voltage, double expected)
    {
        Assert.Equal(expected, UpsCalculations.EstimateBatteryChargeFromVoltage(voltage));
    }

    [Fact]
    public void EstimateBatteryChargeFromVoltage_ClampsAboveOneHundred()
    {
        // nBatt = floor(23/12) = 1; (23 - 11.6) / 0.02 = 570 -> clamped to 100. WinNUT does not clamp; we do
        // (see the XML doc on UpsCalculations.EstimateBatteryChargeFromVoltage) so the UI never sees > 100%.
        Assert.Equal(100.0, UpsCalculations.EstimateBatteryChargeFromVoltage(23.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(6.0)]
    [InlineData(11.9)]
    public void EstimateBatteryChargeFromVoltage_BelowOneCell_ReturnsNull(double voltage)
    {
        Assert.Null(UpsCalculations.EstimateBatteryChargeFromVoltage(voltage));
    }

    // ---- EstimateRuntime -----------------------------------------------------------------------------------------

    [Fact]
    public void EstimateRuntime_LoadBandDefault_HandComputedValue()
    {
        // PowerDivider = 0.5 (load 50 is outside 51-75 and 76-100).
        // BattInstantCurrent = (230*50)/(24*100) = 115/24
        // seconds = floor(7*0.6*80*0.5*3600 / ((115/24)*100)) = floor(604800 / (11500/24)) = floor(1262.19...) = 1262
        var runtime = UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 50, batteryVoltage: 24, batteryCapacity: 7, batteryCharge: 80);

        Assert.Equal(TimeSpan.FromSeconds(1262), runtime);
    }

    [Fact]
    public void EstimateRuntime_LoadBand51To75_UsesPointThreeDivider()
    {
        var runtime = UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 60, batteryVoltage: 24, batteryCapacity: 7, batteryCharge: 80);

        Assert.Equal(TimeSpan.FromSeconds(1472), runtime);
    }

    [Fact]
    public void EstimateRuntime_LoadBand76To100_UsesPointFourDivider()
    {
        var runtime = UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 90, batteryVoltage: 24, batteryCapacity: 7, batteryCharge: 80);

        Assert.Equal(TimeSpan.FromSeconds(841), runtime);
    }

    [Fact]
    public void EstimateRuntime_ZeroLoad_SubstitutesPointOneInternally()
    {
        // WinNUT: ".Load = If(.Load <> 0, .Load, 0.1)" - only affects this formula's internal division.
        var runtime = UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 0, batteryVoltage: 24, batteryCapacity: 7, batteryCharge: 80);

        Assert.Equal(TimeSpan.FromSeconds(631095), runtime);
    }

    [Fact]
    public void EstimateRuntime_NonPositiveBatteryVoltage_ReturnsNull()
    {
        Assert.Null(UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 50, batteryVoltage: 0, batteryCapacity: 7, batteryCharge: 80));
        Assert.Null(UpsCalculations.EstimateRuntime(outputVoltage: 230, load: 50, batteryVoltage: -1, batteryCapacity: 7, batteryCharge: 80));
    }

    [Fact]
    public void EstimateRuntime_ZeroOutputVoltage_ReturnsNull()
    {
        // BattInstantCurrent would be zero, which WinNUT's own formula would divide by (producing Infinity).
        Assert.Null(UpsCalculations.EstimateRuntime(outputVoltage: 0, load: 50, batteryVoltage: 24, batteryCapacity: 7, batteryCharge: 80));
    }

    // ---- BuildInfo -------------------------------------------------------------------------------------------------

    [Fact]
    public void BuildInfo_PrefersUpsVariablesOverDeviceVariables()
    {
        var vars = Vars(
            ("ups.mfr", "APC"), ("device.mfr", "Ignored"),
            ("ups.model", "Back-UPS"), ("device.model", "Ignored"),
            ("ups.serial", "123456"), ("device.serial", "Ignored"),
            ("ups.firmware", "1.0"));

        var info = UpsCalculations.BuildInfo(vars);

        Assert.Equal(new UpsInfo("APC", "Back-UPS", "123456", "1.0"), info);
    }

    [Fact]
    public void BuildInfo_FallsBackToDeviceVariables()
    {
        var vars = Vars(("device.mfr", "APC"), ("device.model", "Back-UPS"), ("device.serial", "123456"));

        var info = UpsCalculations.BuildInfo(vars);

        Assert.Equal(new UpsInfo("APC", "Back-UPS", "123456", "Unknown"), info);
    }

    [Fact]
    public void BuildInfo_NoVariables_ReturnsUnknownForEverything()
    {
        var info = UpsCalculations.BuildInfo(Vars());

        Assert.Equal(new UpsInfo("Unknown", "Unknown", "Unknown", "Unknown"), info);
    }

    [Fact]
    public void BuildInfo_TrimsWhitespaceAndTreatsBlankAsMissing()
    {
        var vars = Vars(("ups.mfr", "   "), ("device.mfr", "  APC  "));

        var info = UpsCalculations.BuildInfo(vars);

        Assert.Equal("APC", info.Manufacturer);
    }

    // ---- BuildReading ----------------------------------------------------------------------------------------------

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildReading_AllVariablesPresent_NoEstimationNeeded()
    {
        var vars = Vars(
            ("ups.status", "OL"),
            ("battery.charge", "80"),
            ("battery.voltage", "24"),
            ("battery.runtime", "3600"),
            ("input.frequency", "50"),
            ("input.voltage", "230"),
            ("output.voltage", "230"),
            ("output.current", "5"),
            ("ups.load", "50"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.OutputVaCalculation, batteryCapacity: 7, nominalFrequencyFallback: 60, now: Now);

        Assert.Equal(Now, reading.Timestamp);
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal("OL", reading.RawStatus);
        Assert.Equal(80, reading.BatteryCharge);
        Assert.False(reading.BatteryChargeEstimated);
        Assert.Equal(24, reading.BatteryVoltage);
        Assert.Equal(TimeSpan.FromSeconds(3600), reading.BatteryRuntime);
        Assert.False(reading.BatteryRuntimeEstimated);
        Assert.Equal(50, reading.InputFrequency);
        Assert.Equal(230, reading.InputVoltage);
        Assert.Equal(230, reading.OutputVoltage);
        Assert.Equal(5, reading.OutputCurrent);
        Assert.Equal(50, reading.LoadPercent);
        Assert.Equal(920.0, reading.OutputPowerWatts);
    }

    [Fact]
    public void BuildReading_BatteryChargeMissing_IsEstimatedFromVoltageAndFlagged()
    {
        var vars = Vars(("battery.voltage", "13"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 50, now: Now);

        Assert.Equal(70.0, reading.BatteryCharge);
        Assert.True(reading.BatteryChargeEstimated);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public void BuildReading_BatteryChargeOutOfRange_IsEstimatedFromVoltage(string rawCharge)
    {
        var vars = Vars(("battery.charge", rawCharge), ("battery.voltage", "13"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 50, now: Now);

        Assert.Equal(70.0, reading.BatteryCharge);
        Assert.True(reading.BatteryChargeEstimated);
    }

    [Fact]
    public void BuildReading_BatteryChargeMissingAndNoVoltage_IsNull()
    {
        var reading = UpsCalculations.BuildReading(Vars(), PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 50, now: Now);

        Assert.Null(reading.BatteryCharge);
        Assert.False(reading.BatteryChargeEstimated);
    }

    [Fact]
    public void BuildReading_BatteryRuntimeMissing_IsEstimatedUsingResolvedCharge()
    {
        var vars = Vars(
            ("battery.voltage", "24"),
            ("output.voltage", "230"),
            ("ups.load", "50"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: 7, nominalFrequencyFallback: 50, now: Now);

        // battery.charge is missing and battery.voltage (24V = 2 cells) estimates it to 20% first, then runtime is
        // estimated from that 20%, exactly like WinNUT computes the charge estimate before the runtime estimate.
        Assert.Equal(20.0, reading.BatteryCharge);
        Assert.True(reading.BatteryChargeEstimated);
        Assert.NotNull(reading.BatteryRuntime);
        Assert.True(reading.BatteryRuntimeEstimated);

        var expectedRuntime = UpsCalculations.EstimateRuntime(230, 50, 24, 7, 20.0);
        Assert.Equal(expectedRuntime, reading.BatteryRuntime);
    }

    [Fact]
    public void BuildReading_ZeroLoad_ReportsZeroButEstimatesRuntimeWithSubstitution()
    {
        var vars = Vars(
            ("battery.charge", "80"),
            ("battery.voltage", "24"),
            ("output.voltage", "230"),
            ("ups.load", "0"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: 7, nominalFrequencyFallback: 50, now: Now);

        // The reported load must stay 0 - the 0 -> 0.1 substitution is internal to the runtime formula only.
        Assert.Equal(0.0, reading.LoadPercent);
        Assert.Equal(TimeSpan.FromSeconds(631095), reading.BatteryRuntime);
        Assert.True(reading.BatteryRuntimeEstimated);
    }

    [Fact]
    public void BuildReading_BatteryRuntimeMissingRequiredInputs_StaysNull()
    {
        var vars = Vars(("battery.charge", "80"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 50, now: Now);

        Assert.Null(reading.BatteryRuntime);
        Assert.False(reading.BatteryRuntimeEstimated);
    }

    [Fact]
    public void BuildReading_FrequencyFallsBackToInputFrequency()
    {
        var vars = Vars(("input.frequency", "50"), ("output.frequency", "60"), ("output.frequency.nominal", "55"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 45, now: Now);

        Assert.Equal(50, reading.InputFrequency);
    }

    [Fact]
    public void BuildReading_FrequencyFallsBackToOutputFrequency_WhenInputMissing()
    {
        var vars = Vars(("output.frequency", "60"), ("output.frequency.nominal", "55"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 45, now: Now);

        Assert.Equal(60, reading.InputFrequency);
    }

    [Fact]
    public void BuildReading_FrequencyFallsBackToOutputFrequencyNominal_WhenNeitherReported()
    {
        var vars = Vars(("output.frequency.nominal", "55"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 45, now: Now);

        Assert.Equal(55, reading.InputFrequency);
    }

    [Fact]
    public void BuildReading_FrequencyFallsBackToSettingsNominal_WhenNothingReported()
    {
        var reading = UpsCalculations.BuildReading(Vars(), PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 45, now: Now);

        Assert.Equal(45, reading.InputFrequency);
    }

    [Fact]
    public void BuildReading_UnknownStatusTokens_AreIgnoredInTheParsedStatus()
    {
        var vars = Vars(("ups.status", "OB FUTURE-STATUS"));

        var reading = UpsCalculations.BuildReading(vars, PowerMethod.Unavailable, batteryCapacity: null, nominalFrequencyFallback: 50, now: Now);

        Assert.Equal(UpsStatus.OB, reading.Status);
        Assert.Equal("OB FUTURE-STATUS", reading.RawStatus);
    }
}
