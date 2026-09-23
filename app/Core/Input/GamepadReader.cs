using SDL;
using static SDL.SDL3;

namespace SpeakForever.Input;

/// <summary>
/// Reads controllers through SDL3, the input library most PC games use: Xbox, PlayStation 3, 4
/// and 5, Nintendo Switch and hundreds of others, with the community's SDL_GameControllerDB for
/// the less common ones. It only reads: nothing is sent to the controller (no light bar, no
/// player lights, no switching it into another report mode), since WoW is reading it at the same
/// time. Create, use and dispose it on one thread.
/// </summary>
public sealed unsafe class GamepadReader : IDisposable
{
    const short TriggerThreshold = 3855; // XInput's 30 of 255, on SDL's 0-32767 scale

    static readonly (SDL_GamepadButton Button, uint Bit)[] Buttons =
    [
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, Gamepad.A), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, Gamepad.B),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST, Gamepad.X), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH, Gamepad.Y),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, Gamepad.LB), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, Gamepad.RB),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, Gamepad.Back), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, Gamepad.Start),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK, Gamepad.LS), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK, Gamepad.RS),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, Gamepad.Up), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, Gamepad.Down),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, Gamepad.Left), (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, Gamepad.Right),
    ];

    SDL_Gamepad* pad;

    /// <param name="mappingsFile">SDL_GameControllerDB's gamecontrollerdb.txt, if it's there.</param>
    /// <exception cref="InvalidOperationException">SDL couldn't start.</exception>
    public GamepadReader(string? mappingsFile)
    {
        // WoW has the focus while you play: read the controller anyway.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        // Read-only: don't switch PlayStation controllers to enhanced reports (it changes what the
        // game receives over Bluetooth), and leave their lights alone.
        SDL_SetHint("SDL_JOYSTICK_ENHANCED_REPORTS", "0");
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5_PLAYER_LED", "0");
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH_HOME_LED", "0");
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH_PLAYER_LED", "0");
        // Device changes are watched on SDL's own thread, so nothing depends on a window's messages.
        SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD)) throw new InvalidOperationException($"Couldn't start SDL: {SDL_GetError()}");
        SDL_SetGamepadEventsEnabled(false); // polled instead: nothing queues up
        if (mappingsFile is not null && File.Exists(mappingsFile)) SDL_AddGamepadMappingsFromFile(mappingsFile);
    }

    /// <summary>The open controller's name, such as "Xbox Series X Controller".</summary>
    public string? Name { get; private set; }

    /// <summary>The open controller's family, for its button icons.</summary>
    public ButtonStyle Style { get; private set; }

    public bool IsOpen => pad is not null;

    /// <summary>Opens the controller at that position among those connected, or with -1 the first. False if there's none.</summary>
    public bool TryOpen(int preferred)
    {
        Close();
        SDL_PumpEvents();
        using var ids = SDL_GetGamepads();
        if (ids is null || ids.Count == 0) return false;
        int index = preferred >= 0 ? preferred : 0;
        if (index >= ids.Count) return false;
        pad = SDL_OpenGamepad(ids[index]);
        if (pad is null) return false;
        Name = SDL_GetGamepadName(pad) ?? "Controller";
        Style = StyleOf(SDL_GetGamepadType(pad));
        return true;
    }

    /// <summary>The buttons held and the right stick now, or null if the controller has gone.</summary>
    public PadState? Read()
    {
        if (pad is null) return null;
        SDL_PumpEvents();
        SDL_UpdateGamepads();
        if (!SDL_GamepadConnected(pad))
        {
            Close();
            return null;
        }
        uint mask = 0;
        foreach (var (button, bit) in Buttons)
            if (SDL_GetGamepadButton(pad, button)) mask |= bit;
        if (SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER) > TriggerThreshold) mask |= Gamepad.LT;
        if (SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) > TriggerThreshold) mask |= Gamepad.RT;
        // SDL's stick Y grows downwards; PadState's grows upwards, like XInput's.
        float x = Math.Max(-1f, SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX) / 32767f);
        float y = Math.Max(-1f, -SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY) / 32767f);
        return new PadState(mask, x, Math.Min(1f, y));
    }

    internal static ButtonStyle StyleOf(SDL_GamepadType type) => type switch
    {
        SDL_GamepadType.SDL_GAMEPAD_TYPE_PS3 => ButtonStyle.PlayStation3,
        SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4 => ButtonStyle.PlayStation4,
        SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 => ButtonStyle.PlayStation5,
        SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
            or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR
            or SDL_GamepadType.SDL_GAMEPAD_TYPE_GAMECUBE => ButtonStyle.Nintendo,
        _ => ButtonStyle.Xbox,
    };

    void Close()
    {
        if (pad is not null) SDL_CloseGamepad(pad);
        pad = null;
    }

    public void Dispose()
    {
        Close();
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD);
    }
}
