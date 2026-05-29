using System.Runtime.InteropServices;

namespace ServerPanel.Services;

public class SystemInfoService
{
    public string GetOperatingSystem()
    {
        return RuntimeInformation.OSDescription;
    }
}