using System;

using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(SmwCountersComponentFactory))]

namespace LiveSplit.UI.Components;

public class SmwCountersComponentFactory : IComponentFactory
{
    public string ComponentName => "SMW Counters";

    public string Description => "Counts SMW deaths, exits, jumps, and 3-up moons from your emulator.";

    public ComponentCategory Category => ComponentCategory.Other;

    public IComponent Create(LiveSplitState state) => new SmwCountersComponent(state);

    public string UpdateName => ComponentName;

    public string XMLURL => string.Empty;

    public string UpdateURL => string.Empty;

    public Version Version => Version.Parse("0.2.0");
}
