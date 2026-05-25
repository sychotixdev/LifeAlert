using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace LifeAlert;

public class LifeAlertSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);

    /// <summary>
    /// How long to wait (ms) after pressing Escape for the escape menu to open.
    /// If the menu does not open within this window an error is logged and the sequence aborts.
    /// </summary>
    public RangeNode<int> EscapeMenuWaitMs { get; set; } = new(500, 100, 10000);

    /// <summary>
    /// How long to wait (ms) for the character-select screen to become active after clicking
    /// the logout / character-select button.
    /// </summary>
    public RangeNode<int> CharacterSelectWaitMs { get; set; } = new(5000, 1000, 30000);

    /// <summary>
    /// How long to wait (ms) after the character-select screen is detected before pressing Enter
    /// to log back in.
    /// </summary>
    public RangeNode<int> AfterCharSelectDelayMs { get; set; } = new(500, 100, 10000);
}
