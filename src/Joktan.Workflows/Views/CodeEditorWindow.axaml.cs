using System;
using Avalonia.Controls;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

public partial class CodeEditorWindow : Window
{
    public CodeEditorWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        InsertVariableBox.SelectionChanged += (_, _) => InsertVariable(InsertVariableBox, Editor);
    }

    /// <summary>Puts the variable chosen in a box at the editor's caret, as the code's language reads it, and clears the box.</summary>
    public static void InsertVariable(ComboBox box, Controls.CodeEditor editor)
    {
        if (box.SelectedItem is not string name || box.DataContext is not StepViewModel step)
            return;
        box.SelectedItem = null;
        editor.Insert(step.Reference(name));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is StepViewModel step)
            Title = string.Format(Strings.Step_EditorTitle, step.Name);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.Editor.Focus();
    }
}
