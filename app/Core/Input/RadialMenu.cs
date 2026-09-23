using SpeakForever.Logging;

namespace SpeakForever.Input;

/// <summary>
/// Follows WoW's gamepad radial menu, per Blizzard_Gamepad/UI/Radials/GamepadRadial.lua (build
/// 1.60.1), so that picking Chat from it counts as opening chat. It opens on page 2 of 3 (LB/RB
/// turn pages and wrap), the right stick points at one of 8 segments and releasing it back to
/// centre picks it. Clicking the stick cancels the selection; picking an empty slot does nothing,
/// so the menu stays open.
/// </summary>
public sealed class RadialMenu
{
    static readonly string?[][] Slots =
    [
        ["Dungeon Finder", "Legacy", "Shop", "Guild", "Social", "Buffs", "Tracking", "Collections"],
        ["Bags", "Professions", "Character", "Talents", "Quests & Maps", "Chat", "Game Menu", "Spellbook"],
        ["Clock", "Calendar", null, null, "PvP", null, null, null],
    ];
    const int Pages = 3, HomePage = 2, ChatSegment = 6;
    const double DeadzoneSq = 0.04, ThresholdSq = 0.25, SegmentDegrees = 45;

    int page, segment, pending;
    bool cancelled;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        page = HomePage;
        segment = pending = 0;
        cancelled = false;
        Log.Info("Radial menu open.");
    }

    /// <summary>Forgets the menu, for when the app stops watching and can't know what happens to it.</summary>
    public void Reset() => IsOpen = false;

    /// <summary>A button change while the menu is open; it has the pad to itself, so nothing else sees these.</summary>
    /// <param name="toggle">WoW's radial button, which closes it again.</param>
    /// <param name="back">B, which also closes it.</param>
    public void OnButtons(uint prev, uint cur, Chord toggle, Chord back)
    {
        bool Pressed(uint bit) => (cur & bit) != 0 && (prev & bit) == 0;

        if (toggle.FiredBy(prev, cur) || back.FiredBy(prev, cur))
        {
            IsOpen = false;
            Log.Info("Radial menu closed.");
        }
        else if (Pressed(Gamepad.LB) || Pressed(Gamepad.RB))
        {
            page = Pressed(Gamepad.LB) ? (page == 1 ? Pages : page - 1) : (page == Pages ? 1 : page + 1);
            Log.Info($"Radial page {page} of {Pages}.");
        }
        else if (Pressed(Gamepad.RS))
        {
            segment = 0;
            cancelled = true;
            Log.Info("Radial: stick clicked, selection cancelled until it recentres.");
        }
    }

    /// <summary>The right stick, every poll while open. True when Chat was just picked (the menu has closed).</summary>
    public bool OnRightStick(float x, float y)
    {
        if (!IsOpen) return false;
        double distanceSq = x * x + y * y;
        if (cancelled)
        {
            if (distanceSq > DeadzoneSq) return false; // no new selection until the stick recentres
            cancelled = false;
        }
        if (distanceSq < DeadzoneSq && segment != 0)
        {
            bool chat = page == HomePage && segment == ChatSegment;
            string? picked = Slots[page - 1][segment - 1];
            segment = 0;
            if (picked is null)
            {
                Log.Info($"Radial: empty slot on page {page}; the menu stays open.");
                return false;
            }
            IsOpen = false;
            Log.Info(chat ? "Chat open (from the radial menu)." : $"Radial menu closed: picked {picked} on page {page}.");
            return chat;
        }
        if (distanceSq > ThresholdSq)
        {
            double degrees = Math.Atan2(y, x) * 180 / Math.PI;
            if (degrees < -SegmentDegrees / 2) degrees += 360;
            int pointedAt = (int)Math.Floor((degrees + SegmentDegrees) / SegmentDegrees + 0.5);
            // A released stick springs back past centre, and between polls the overshoot can land on
            // the opposite segment; WoW, reading once a frame, doesn't see it. Only a direction seen on
            // two polls in a row counts.
            if (pointedAt == pending) segment = pointedAt;
            pending = pointedAt;
        }
        else pending = 0;
        return false;
    }
}
