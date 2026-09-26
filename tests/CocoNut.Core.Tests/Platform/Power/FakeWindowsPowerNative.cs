using CocoNut.Platform.Power.Native;

namespace CocoNut.Core.Tests.Platform.Power;

internal sealed class FakeWindowsPowerNative : IWindowsPowerNative
{
    public bool HibernateSupported { get; set; } = true;

    public bool SetSuspendStateResult { get; set; } = true;

    public int CallCount { get; private set; }

    public bool? LastHibernate { get; private set; }

    public bool? LastForceCritical { get; private set; }

    public bool? LastDisableWakeEvent { get; private set; }

    public bool IsHibernateSupported() => HibernateSupported;

    public bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent)
    {
        CallCount++;
        LastHibernate = hibernate;
        LastForceCritical = forceCritical;
        LastDisableWakeEvent = disableWakeEvent;
        return SetSuspendStateResult;
    }
}
