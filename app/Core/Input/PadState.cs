namespace SpeakForever.Input;

/// <summary>Button mask, and the right stick from -1 to 1 with up positive (the radial menu steers with it).</summary>
public readonly record struct PadState(uint Buttons, float RightX, float RightY);
