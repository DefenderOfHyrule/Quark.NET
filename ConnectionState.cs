namespace Quark;

public sealed class ConnectionState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Label, string ConsoleId)> _usbDevices = new();
    private readonly Dictionary<string, string> _networkClients = new(); 

    public void AddUsb(string deviceId, string label, string consoleId)
    {
        lock (_lock) _usbDevices[deviceId] = (label, consoleId);
    }

    public void RemoveUsb(string deviceId)
    {
        lock (_lock) _usbDevices.Remove(deviceId);
    }

    public void AddNetwork(string ip, string consoleId)
    {
        lock (_lock) _networkClients[ip] = consoleId;
    }

    public void RemoveNetwork(string ip)
    {
        lock (_lock) _networkClients.Remove(ip);
    }

    public void RenameNetwork(string oldId, string newId)
    {
        lock (_lock)
        {
            var entry = _networkClients.FirstOrDefault(kv => kv.Value == oldId);
            if (entry.Key != null)
            {
                _networkClients[entry.Key] = newId;
            }
        }
    }

    public Snapshot GetSnapshot()
    {
        lock (_lock)
            return new Snapshot(
                _usbDevices.Values.Select(v => (v.Label, v.ConsoleId)).ToList(),
                _networkClients.Select(kv => (kv.Key, kv.Value)).ToList());
    }

    public sealed record Snapshot(
        List<(string Label, string ConsoleId)> UsbDevices,
        List<(string Ip, string ConsoleId)>    NetworkClients)
    {
        public int  UsbCount   => UsbDevices.Count;
        public int  NetCount   => NetworkClients.Count;
        public bool HasUsb     => UsbCount > 0;
        public bool HasNetwork => NetCount > 0;
        public bool IsIdle     => !HasUsb && !HasNetwork;
        public int  TotalCount => UsbCount + NetCount;
        public bool IsMulti    => TotalCount > 1;

        public string PillText()
        {
            if (IsIdle) return "Listening for connection...";

            string usbPart = UsbCount == 1
                ? $"● USB:  {UsbDevices[0].ConsoleId}"
                : UsbCount > 1
                ? $"● USB ({UsbCount})"
                : "";

            string netPart = NetCount == 1
                ? $"◈ Network:  {NetworkClients[0].ConsoleId}"
                : NetCount > 1
                ? $"◈ Network ({NetCount})"
                : "";

            if (!string.IsNullOrEmpty(usbPart) && !string.IsNullOrEmpty(netPart))
                return $"{usbPart}  {netPart}";

            return string.IsNullOrEmpty(usbPart) ? netPart : usbPart;
        }

        public string PillColor()
        {
            if (IsIdle)                 return "#607D8B";
            if (HasUsb  && !HasNetwork) return "#4CAF50";
            if (!HasUsb && HasNetwork)  return "#42A5F5";
            return "#FFB74D";
        }

        public List<string> DropdownRows()
        {
            var rows = new List<string>();
            foreach (var (label, id) in UsbDevices)
                rows.Add($"● USB:  {id} - {label}");
            foreach (var (ip, id) in NetworkClients)
                rows.Add($"◈ Network:  {id} - {ip}");
            return rows;
        }
    }
}
