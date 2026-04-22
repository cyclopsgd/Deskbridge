using System.Runtime.InteropServices;

namespace Deskbridge.Protocols.Rdp.Interop;

/// <summary>
/// Manual declaration of IMsRdpExtendedSettings. Not present in the aximp-generated
/// interop assemblies. Required for DesktopScaleFactor and DeviceScaleFactor properties
/// which communicate the client monitor's DPI to the remote session.
/// [CITED: learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property]
/// </summary>
[ComImport, Guid("302D8188-0052-4807-806A-362B628F9AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IMsRdpExtendedSettings
{
    [DispId(1)]
    object get_Property([MarshalAs(UnmanagedType.BStr)] string bstrPropertyName);

    [DispId(1)]
    void set_Property(
        [MarshalAs(UnmanagedType.BStr)] string bstrPropertyName,
        [In] ref object pValue);
}
