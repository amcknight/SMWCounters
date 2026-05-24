using System;

using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(SmwCountersComponentFactory))]

namespace LiveSplit.UI.Components;

public class SmwCountersComponentFactory : IComponentFactory
{
    public string ComponentName => "SMW Counters";

    public string Description => "Watches SNES WRAM in an emulator process and counts SMW deaths, moons, and more — pick which to show.";

    public ComponentCategory Category => ComponentCategory.Other;

    public IComponent Create(LiveSplitState state) => new SmwCountersComponent(state);

    public string UpdateName => ComponentName;

    public string XMLURL => string.Empty;

    public string UpdateURL => string.Empty;

    public Version Version => Version.Parse("0.1.0");
}
