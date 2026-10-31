using System;

namespace NivalisSwitchboard;

/// <summary>Where the pause menu's buttons go this frame (screen pixels, y up), and how far they're scrolled.</summary>
public readonly record struct PausePlan(double Shift, double Scroll, double MaxScroll, bool Scrolls);

/// <summary>
/// FB-55: the pause menu's buttons centred on the screen, below the game's "GAME PAUSED" heading; more than fit scroll
/// with the mouse wheel and fade out toward the edge they run past. Plain numbers, so it's tested without the game.
/// </summary>
public static class PauseLayout
{
    /// <summary>The band the buttons keep to, as shares of the screen's height from the bottom: under the heading, above the edge.</summary>
    public const double TopShare = 0.84, BottomShare = 0.05;
    /// <summary>How far in from the band's edge a button fades (share of the screen's height).</summary>
    public const double FadeShare = 0.09;
    /// <summary>One notch of the wheel (share of the screen's height).</summary>
    public const double NotchShare = 0.07;

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>
    /// <paramref name="top"/> and <paramref name="bottom"/>: the buttons' edges where the game put them (before any shift).
    /// Shift: how far to move them up. When they fit, they're centred on the screen (kept inside the band); when they don't,
    /// <paramref name="scroll"/> (0 = the first button at the top of the band) says how far down the list is shown.
    /// </summary>
    public static PausePlan Plan(double top, double bottom, double screenHeight, double scroll)
    {
        double bandTop = screenHeight * TopShare, bandBottom = screenHeight * BottomShare;
        double height = Math.Max(0, top - bottom), band = bandTop - bandBottom;
        if (height <= band)
        {
            double centre = Clamp(screenHeight / 2, bandBottom + height / 2, bandTop - height / 2);
            return new PausePlan(centre - (top + bottom) / 2, 0, 0, false);
        }
        double max = height - band;
        scroll = Clamp(scroll, 0, max);
        return new PausePlan(bandTop + scroll - top, scroll, max, true);
    }

    /// <summary>
    /// How visible a button is at <paramref name="y"/> (its middle, after the shift): fading in from the band's edge, only
    /// toward an edge with more buttons past it; 1 when nothing scrolls.
    /// </summary>
    public static double Alpha(double y, double screenHeight, PausePlan plan)
    {
        if (!plan.Scrolls) return 1;
        double bandTop = screenHeight * TopShare, bandBottom = screenHeight * BottomShare, fade = screenHeight * FadeShare;
        double a = 1;
        if (plan.Scroll > 0.5) a = Math.Min(a, (bandTop - y) / fade);                    // more above
        if (plan.Scroll < plan.MaxScroll - 0.5) a = Math.Min(a, (y - bandBottom) / fade); // more below
        return Clamp(a, 0, 1);
    }

    /// <summary>The scroll that brings a button (its edges after the shift) clear of the fades: a controller's selection kept in view.</summary>
    public static double ScrollToShow(double itemTop, double itemBottom, double screenHeight, PausePlan plan)
    {
        if (!plan.Scrolls) return 0;
        double bandTop = screenHeight * TopShare, bandBottom = screenHeight * BottomShare, fade = screenHeight * FadeShare;
        double scroll = plan.Scroll;
        if (itemTop > bandTop - fade) scroll -= itemTop - (bandTop - fade);
        else if (itemBottom < bandBottom + fade) scroll += bandBottom + fade - itemBottom;
        return Clamp(scroll, 0, plan.MaxScroll);
    }

    /// <summary>
    /// The scroll after the wheel turned (<paramref name="wheel"/>: the mouse's scroll, up shows the buttons above). Windows
    /// reports 120 a notch; a value under 10 is taken as notches already (an input system that counts them, a touchpad's
    /// fraction), so a touchpad glides instead of jumping a notch a frame (review).
    /// </summary>
    public static double Wheel(double scroll, double wheel, double screenHeight, PausePlan plan)
    {
        if (!plan.Scrolls || wheel == 0) return plan.Scroll;
        double notches = Math.Abs(wheel) >= 10 ? wheel / 120 : wheel;
        return Clamp(scroll - notches * screenHeight * NotchShare, 0, plan.MaxScroll);
    }
}
