using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

public class IconsTests
{
    [Fact]
    public Task Every_icon_is_a_shape_on_the_24_by_24_grid() => Headless.Run(() =>
    {
        var icons = typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (f.Name, Geometry: (Geometry)f.GetValue(null)!)).ToList();

        Assert.NotEmpty(icons);
        Assert.All(icons, icon =>
        {
            Assert.True(icon.Geometry.Bounds.Width > 0, icon.Name);
            Assert.True(icon.Geometry.Bounds.Right <= 24, icon.Name);
            Assert.True(icon.Geometry.Bounds.Bottom <= 24, icon.Name);
        });
    });
}
