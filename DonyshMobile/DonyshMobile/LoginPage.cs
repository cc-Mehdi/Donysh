using DonyshMobile.Core;

namespace DonyshMobile;

public sealed class LoginPage : ConnectionPage
{
    public LoginPage(SmsMonitorStore store) : base(store, setupMode: true)
    {
        Shell.SetNavBarIsVisible(this, false);
    }

    protected override bool OnBackButtonPressed() => true;
}
