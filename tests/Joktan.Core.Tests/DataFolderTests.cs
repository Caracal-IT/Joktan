using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class DataFolderTests
{
    [Fact]
    public void Tests_never_use_the_real_data_folder()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan");

        Assert.NotEqual(real, DataFolder.Path);
        Assert.StartsWith(Path.GetTempPath(), DataFolder.Path);
    }

    [Fact]
    public void The_variable_chooses_the_folder()
    {
        var previous = Environment.GetEnvironmentVariable(DataFolder.Variable);
        try
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, "/some/folder");
            Assert.Equal("/some/folder", DataFolder.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, previous);
        }
    }

    [Fact]
    public void Without_the_variable_it_is_a_Joktan_folder_in_the_application_data()
    {
        var previous = Environment.GetEnvironmentVariable(DataFolder.Variable);
        try
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, "");
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan"),
                DataFolder.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, previous);
        }
    }
}
