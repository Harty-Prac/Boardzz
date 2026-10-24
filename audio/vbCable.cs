using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace boardzz.audio;

// Bundled VB-CABLE (VB-Audio, vb-cable.com). VB-CABLE is a donationware, all participations are welcome.
// The driver pack ships in vendor\VBCABLE next to Boardzz.exe; its setup is run on request, elevated.
internal static class vbCable
{
    public const string attribution = "VB-CABLE by VB-Audio (vb-cable.com). VB-CABLE is a donationware, all participations are welcome.";
    public const string donateUrl = "https://vb-audio.com/Cable/";

    private static readonly string packFolder = Path.Combine(AppContext.BaseDirectory, "vendor", "VBCABLE");

    public static bool isInstalled(MMDeviceEnumerator enumerator) =>
        enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Any(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

    public static string? setupPath
    {
        get
        {
            string[] candidates = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ["VBCABLE_Setup_arm64.exe", "VBCABLE_Setup_x64.exe"],
                Architecture.X64 => ["VBCABLE_Setup_x64.exe"],
                _ => ["VBCABLE_Setup.exe"]
            };
            return candidates.Select(name => Path.Combine(packFolder, name)).FirstOrDefault(File.Exists);
        }
    }

    // Runs the bundled setup as administrator. Windows still shows its own driver-trust prompt.
    // Returns false if the user cancelled the admin prompt.
    public static async Task<bool> install()
    {
        string setup = setupPath ?? throw new FileNotFoundException("The VB-CABLE driver pack is missing from vendor\\VBCABLE.");

        try
        {
            using var process = Process.Start(new ProcessStartInfo(setup, "-i -h")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = packFolder
            });
            if (process != null) await process.WaitForExitAsync();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: user said no to UAC
        {
            return false;
        }
    }
}
