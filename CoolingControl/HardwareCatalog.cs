namespace CoolingControl;

using CoolingControl.Platform;

public sealed class HardwareCatalog
{
    private List<HardwareChannel> _rows = [];

    public IReadOnlyList<HardwareChannel> Rows => _rows;

    public void Refresh(IPlatformAdapter adapter)
    {
        _rows = adapter.GetHardwareCatalog().ToList();
    }
}
