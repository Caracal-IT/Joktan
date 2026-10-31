using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core.Controls;
using Joktan.Dashboard.Telemetry;
using Joktan.Dashboard.ViewModels;
using Joktan.Dashboard.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Dashboard.Tests.Views;

public class DashboardViewTests
{
    [Fact]
    public Task Shows_the_tiles_and_what_is_exported() => Headless.Run(() =>
    {
        using var monitor = new MachineMonitor();
        var vm = new DashboardViewModel(monitor);
        var window = Headless.Show(new DashboardView(), vm);
        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();

        Assert.Contains("CPU usage", texts);
        Assert.Contains("system.cpu.utilization", texts);
        Assert.Equal(vm.Tiles.Count(t => t.HasTrend), Headless.All<Sparkline>(window).Count(s => s.IsEffectivelyVisible));
        window.Close();
    });
}
