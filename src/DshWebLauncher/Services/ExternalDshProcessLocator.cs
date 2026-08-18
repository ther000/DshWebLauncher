using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace DshWebLauncher.Services;

public interface IExternalDshProcessLocator
{
    Process? TryFind(Uri webUri);
}

public sealed class ExternalDshProcessLocator : IExternalDshProcessLocator
{
    private const int AddressFamilyInterNetwork = 2;
    private const int AddressFamilyInterNetworkV6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint InsufficientBuffer = 122;

    public Process? TryFind(Uri webUri)
    {
        if (!IsLocalHost(webUri.Host)) return null;

        foreach (var processId in GetListeningProcessIds(webUri.Port))
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase)) return process;
                process.Dispose();
            }
            catch (ArgumentException)
            {
                // 进程可能在端口枚举后退出。
            }
            catch (InvalidOperationException)
            {
                // 无法读取进程信息时不进行接管。
            }
        }

        return null;
    }

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "0.0.0.0" or "::") return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;

        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName()).Contains(address);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    private static IEnumerable<int> GetListeningProcessIds(int port)
    {
        var result = new HashSet<int>();
        ReadTable(AddressFamilyInterNetwork, port, result);
        ReadTable(AddressFamilyInterNetworkV6, port, result);
        return result;
    }

    private static void ReadTable(int addressFamily, int port, ISet<int> result)
    {
        var size = 0;
        var status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);
        if (status != InsufficientBuffer || size <= 0) return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            status = GetExtendedTcpTable(buffer, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);
            if (status != 0) return;

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            if (addressFamily == AddressFamilyInterNetwork)
            {
                var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                    if (ConvertPort(row.LocalPort) == port) result.Add((int)row.OwningProcessId);
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<Tcp6RowOwnerPid>();
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<Tcp6RowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                    if (ConvertPort(row.LocalPort) == port) result.Add((int)row.OwningProcessId);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ConvertPort(uint value) => (ushort)IPAddress.NetworkToHostOrder((short)value);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningProcessId;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);
}
