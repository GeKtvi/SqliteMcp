using SqliteMcp.Tests.Infrastructure;
using TUnit.Core.Enums;

namespace SqliteMcp.Tests.Scenarios;

public class ConnectionEdgeCaseTests
{
    [Test, RunOn(OS.Windows)]
    public async Task OpenDb_SameKeySamePathDifferentCasing_Reuses()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var lowerPath = harness.DbPath("case-test.db");
        var upperPath = lowerPath.ToUpperInvariant();

        var first = JsonTestExtensions.ParseJson(harness.Lifecycle.OpenDb(lowerPath, "shared")).RootElement;
        var second = JsonTestExtensions.ParseJson(harness.Lifecycle.OpenDb(upperPath, "shared")).RootElement;

        await Assert.That(first.GetString("path")).IsEqualTo(second.GetString("path"));
        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }
}
