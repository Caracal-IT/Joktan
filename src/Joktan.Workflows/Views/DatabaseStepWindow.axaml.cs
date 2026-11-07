using System;
using Avalonia.Controls;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

public partial class DatabaseStepWindow : Window
{
    public DatabaseStepWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DatabaseStepViewModel step)
            Title = string.Format(Strings.DatabaseStep_WindowTitle, step.Name);
    }

    // The connection it read the database through is closed with it.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is DatabaseStepViewModel step)
            _ = step.DisposeAsync().AsTask();
    }
}
