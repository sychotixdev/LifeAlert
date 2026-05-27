using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace LifeAlert;

public class LifeAlertSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);
    public ToggleNode DisableInSekhemas { get; set; } = new(true);
}
