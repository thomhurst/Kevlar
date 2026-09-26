using TUnit.Core;

namespace Kevlar.Tests;

public static class ActivityTestConfiguration
{
    [Before(HookType.TestDiscovery)]
    public static void Configure(BeforeTestDiscoveryContext context)
    {
        // The HTML reporter listens to every ActivitySource. These suites must control
        // sampling themselves and measure the production path without a listener.
        context.Settings.Reporting.HtmlReportEnabled = false;
    }
}
