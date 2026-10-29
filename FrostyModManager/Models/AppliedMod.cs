using CommunityToolkit.Mvvm.ComponentModel;

namespace FrostyModManager.Models;

/// <summary>
/// An entry in the load order of a pack.
/// </summary>
public sealed partial class AppliedMod : ObservableObject
{
    public ModItem Mod { get; }

    [ObservableProperty] private bool m_isEnabled;

    /// <summary>
    /// 1-based position in the load order.
    /// </summary>
    [ObservableProperty] private int m_order;

    public AppliedMod(ModItem inMod, bool inIsEnabled)
    {
        Mod = inMod;
        m_isEnabled = inIsEnabled;
    }
}
