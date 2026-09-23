namespace SpeakForever.Input;

/// <summary>
/// Follows WoW's gamepad chat panel from the same presses the game sees. Its menus reuse A and
/// B: in the channel menu the first A opens a submenu (General, Custom, Language) and the next
/// picks an item, landing back in the text box; B backs out one level. When unsure, it errs
/// towards "not in the text box", which only costs a refused dictation.
/// </summary>
sealed class ChatPanel
{
    const int TextBox = 0, Menu = 1, Submenu = 2;

    // Written by the controller thread; the app also closes it (rebinding) and reads it.
    volatile bool open;
    volatile int depth;

    /// <summary>The panel is up, possibly with one of its menus open.</summary>
    public bool IsOpen => open;

    /// <summary>The panel is up with its text box focused: the only place dictation may type.</summary>
    public bool InTextBox => open && depth == TextBox;

    public void Open()
    {
        depth = TextBox;
        open = true;
    }

    public void Close()
    {
        open = false;
        depth = TextBox;
    }

    public ChatAction OnButtons(uint prev, uint cur, ControllerBindings b)
    {
        if (b.OpenChat.FiredBy(prev, cur))
        {
            Open();
            return ChatAction.None;
        }
        if (!open) return b.Dictate.FiredBy(prev, cur) ? ChatAction.DictateWhileClosed : ChatAction.None;
        if (b.Dictate.FiredBy(prev, cur)) return depth == TextBox ? ChatAction.Dictate : ChatAction.DictateInMenu;
        if (depth == TextBox && b.Menus.Any(m => m.FiredBy(prev, cur)))
        {
            depth = Menu;
            return ChatAction.MenuOpened;
        }
        if (b.Send.FiredBy(prev, cur))
        {
            if (depth == TextBox) open = false;
            else depth = depth == Menu ? Submenu : TextBox;
        }
        else if (b.Back.FiredBy(prev, cur))
        {
            if (depth == TextBox) open = false;
            else depth--;
        }
        return ChatAction.None;
    }
}
