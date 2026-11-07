using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Joktan.Workflows.Model;
using TextMateSharp.Grammars;

namespace Joktan.Workflows.Controls;

/// <summary>
/// An editor for the code of a script step: line numbers, and the colours of C#, JavaScript or Go, in a dark or light
/// theme as the app is. <see cref="Code"/> binds both ways.
/// </summary>
public sealed class CodeEditor : UserControl
{
    public static readonly StyledProperty<string> CodeProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(Code), "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ScriptLanguage> LanguageProperty =
        AvaloniaProperty.Register<CodeEditor, ScriptLanguage>(nameof(Language));

    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;
    private bool _updating;

    public CodeEditor()
    {
        // The editor's own look, so that wherever it is used it needs nothing added to the app.
        Styles.Add(new StyleInclude(new Uri("avares://Joktan.Workflows/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });

        Editor = new TextEditor
        {
            ShowLineNumbers = true,
            WordWrap = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(4),
        };
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableRectangularSelection = true;
        Editor.Bind(TextEditor.FontFamilyProperty, this.GetResourceObservable("CodeFontFamily"));
        Editor.Bind(TextEditor.FontSizeProperty, this.GetResourceObservable("CodeFontSize"));
        Editor.TextChanged += (_, _) =>
        {
            if (_updating)
                return;
            _updating = true;
            SetCurrentValue(CodeProperty, Editor.Text);
            _updating = false;
        };
        Content = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = Editor,
            [!Border.BorderBrushProperty] = this.GetResourceObservable("BorderBrush").ToBinding(),
        };

        _registry = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = Editor.InstallTextMate(_registry);
        ApplyLanguage();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>The editor itself, for what the view needs beyond the code, such as the caret.</summary>
    public TextEditor Editor { get; }

    public string Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public ScriptLanguage Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>Puts text at the caret, in place of what is selected, and goes back to typing.</summary>
    public void Insert(string text)
    {
        Editor.TextArea.Selection.ReplaceSelectionWithText(text);
        Editor.Focus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty && !_updating)
        {
            // Code set from outside replaces what is shown, keeping the caret where it can.
            var text = change.GetNewValue<string>() ?? "";
            if (Editor.Text != text)
            {
                _updating = true;
                var caret = Editor.CaretOffset;
                Editor.Document.Text = text;
                Editor.CaretOffset = Math.Min(caret, text.Length);
                _updating = false;
            }
        }
        else if (change.Property == LanguageProperty)
        {
            ApplyLanguage();
        }
    }

    private void ApplyLanguage()
    {
        var extension = Language switch
        {
            ScriptLanguage.Go => ".go",
            ScriptLanguage.JavaScript => ".js",
            _ => ".cs",
        };
        if (_registry.GetLanguageByExtension(extension) is { } language)
            _textMate.SetGrammar(_registry.GetScopeByLanguageId(language.Id));
    }

    // The colours follow the app's theme, light or dark, with the editor on the app's own surface.
    private void ApplyTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        _textMate.SetTheme(_registry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        if (this.TryFindResource("SurfaceAltBrush", ActualThemeVariant, out var surface) && surface is IBrush background)
            Editor.Background = background;
        if (this.TryFindResource("TextBrush", ActualThemeVariant, out var text) && text is IBrush foreground)
            Editor.Foreground = foreground;
    }
}
