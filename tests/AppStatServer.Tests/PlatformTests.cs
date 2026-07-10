using AppStatServer.Data;

namespace AppStatServer.Tests;

public class PlatformTests
{
    [Test]
    [Arguments("Android (API level 34)", "Android")]
    [Arguments("Microsoft Windows 10.0.26200", "Windows")]
    [Arguments("Ubuntu Linux 22.04", "Linux")]
    [Arguments("Mac OS X 14.2", "macOS")]
    [Arguments("iPhone OS 17.2", "iOS")]
    // A browser user-agent embeds "Windows NT" itself, so it must bucket as Web, not Windows.
    [Arguments("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36", "Web")]
    [Arguments("Mozilla/5.0 (X11; Linux x86_64)", "Web")]
    public async Task Categorizes_raw_os_into_platform_bucket(string os, string expected)
    {
        await Assert.That(Platform.Categorize(os)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Blank_os_is_unknown(string? os)
    {
        await Assert.That(Platform.Categorize(os)).IsEqualTo(Platform.Unknown);
    }

    [Test]
    public async Task Unrecognised_os_is_other()
    {
        await Assert.That(Platform.Categorize("PlayStation 5")).IsEqualTo("Other");
    }
}
