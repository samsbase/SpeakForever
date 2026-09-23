namespace VoiceForever.Input;

/// <summary>What a press in the chat panel asks the engine to do.</summary>
enum ChatAction
{
    None,

    /// <summary>The dictate button, with the text box open.</summary>
    Dictate,

    /// <summary>The dictate button, but chat isn't open.</summary>
    DictateWhileClosed,

    /// <summary>The dictate button, but one of the panel's menus is open.</summary>
    DictateInMenu,

    /// <summary>A menu opened over the text box: anything being dictated is dropped, the typed text stays.</summary>
    MenuOpened,
}
