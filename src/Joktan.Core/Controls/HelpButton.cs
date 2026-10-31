using System;
using Avalonia.Controls;

namespace Joktan.Core.Controls;

/// <summary>Opens help on the topic its window names, or on the module whose page is being shown. Looks like any other button.</summary>
public sealed class HelpButton : Button
{
    public HelpButton()
    {
        Content = "Help";
        ToolTip.SetTip(this, "Open help for what is on screen (F1)");
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        base.OnClick();
        HelpContext.Open(TopLevel.GetTopLevel(this) as Window);
    }
}
