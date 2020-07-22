using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemnantESP
{
    public class Memory : IDisposable
    {
        [DllImport("kernel32")] static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, Int32 bInheritHandle, Int32 dwProcessId);
        [DllImport("kernel32")] static extern Int32 ReadProcessMemory(IntPtr hProcess, UInt64 lpBaseAddress, [In, Out] Byte[] buffer, Int32 size, out Int32 lpNumberOfBytesRead);
        [DllImport("kernel32")] static extern Boolean WriteProcessMemory(IntPtr hProcess, UInt64 lpBaseAddress, Byte[] buffer, Int32 nSize, out Int32 lpNumberOfBytesWritten);
        [DllImport("kernel32")] static extern Int32 CloseHandle(IntPtr hObject);

        private IntPtr procHandle = IntPtr.Zero;
        public Process Process { get; private set; }
        public UInt64 BaseAddress { get { return (UInt64)Process.MainModule.BaseAddress; } }
        public Memory(String name)
        {
            var procs = Process.GetProcessesByName(name);
            Process = procs.FirstOrDefault();
            if (Process == null) return;
            OpenProcess(Process.Id);
        }
        public void OpenProcess(Int32 procId)
        {
            procHandle = OpenProcess(0x38, 1, procId);
        }
        public void WriteProcessMemory(UInt64 addr, Byte[] buffer)
        {
            WriteProcessMemory(procHandle, addr, buffer, 4, out Int32 bytesRead);
        }
        public unsafe Object ReadProcessMemory(Type type, UInt64 addr)
        {
            if (type == typeof(String))
            {
                List<Byte> bytes = new List<Byte>();
                var isUtf16 = false;
                for (UInt32 i = 0; i < 32; i++)
                {
                    var letters8 = ReadProcessMemory<UInt64>(addr + i * 8);
                    var tempBytes = BitConverter.GetBytes(letters8);
                    for (int j = 0; j < 8; j++)
                    {
                        if (tempBytes[j] == 0 && j == 1 && bytes.Count == 1)
                            isUtf16 = true;
                        if (isUtf16 && j % 2 == 1)
                            continue;
                        if (tempBytes[j] == 0)
                            return (Object)Encoding.UTF8.GetString(bytes.ToArray());
                        if ((tempBytes[j] < 32 || tempBytes[j] > 126) && tempBytes[j] != '\n')
                            return (Object)"null";
                        bytes.Add(tempBytes[j]);
                    }
                }
                return (Object)Encoding.UTF8.GetString(bytes.ToArray());
            }
            var buffer = new Byte[Marshal.SizeOf(type)];
            ReadProcessMemory(procHandle, addr, buffer, Marshal.SizeOf(type), out Int32 bytesRead);
            var structPtr = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var obj = Marshal.PtrToStructure(structPtr.AddrOfPinnedObject(), type);
            var members = obj.GetType().GetFields();
            foreach (var member in members)
            {
                if (member.FieldType == typeof(String))
                {
                    var offset = Marshal.OffsetOf(type, member.Name).ToInt32();
                    var ptr = BitConverter.ToUInt32(buffer.Skip(offset).Take(4).ToArray(), 0);
                    if (ptr == 0xffffffff || ptr == 0)
                    {
                        member.SetValueDirect(__makeref(obj), "null");
                        continue;
                    }
                    /* var val = member.GetValue(obj);
                     var validStr = true;
                     for (int i = 0; i < 16; i++)
                     {
                         if (buffer[offset + i] == 0 && i > 1)
                             break;
                         if (buffer[offset + i] < 32 || buffer[offset + i] > 126)
                         {
                             validStr = false;
                             break;
                         }
                     }
                     if (validStr)
                         continue;*/
                    var strPtr = Marshal.ReadIntPtr(structPtr.AddrOfPinnedObject(), offset);
                    var str = ReadProcessMemory<String>((UInt32)strPtr);
                    if (str != "null" && str != "")
                        member.SetValueDirect(__makeref(obj), str);
                }
                /*if (member.FieldType.IsPointer)
                {
                    var address = System.Reflection.Pointer.Unbox(member.GetValue(obj));
                    var value = ReadProcessMemory(member.FieldType.GetElementType(), (UInt32)address);
                }*/
            }
            structPtr.Free();
            return obj;
        }
        public T ReadProcessMemory<T>(UInt64 addr)
        {
            return (T)ReadProcessMemory(typeof(T), addr);
        }

        public List<UInt64> SearchProcessMemory(String pattern, UInt64 start, UInt64 end, Boolean absolute = true)
        {
            var arrayOfBytes = pattern.Split(' ').Select(b => b.Contains("?") ? -1 : Convert.ToInt32(b, 16)).ToArray();
            var addresses = new List<UInt64>();
            var iters = 1 + ((end - start) / 0x1000);
            if (iters == 0) iters++;
            for (uint i = 0; i < iters; i++)
            {
                var buffer = new Byte[0x1000];
                ReadProcessMemory(procHandle, (UInt32)(start + i * 0x1000), buffer, 0x1000, out Int32 bytesRead);
                var results = Scan(buffer, arrayOfBytes).Select(j => (UInt64)j + start + i * 0x1000).ToList();
                if (start + (i + 1) * 0x1000 > end && results.Count > 0)
                    results.RemoveAll(r => r > end);
                addresses.AddRange(results);
            }
            if (absolute)
                return addresses;
            else
                return addresses.Select(a => a - start).ToList();
        }
        public List<UInt64> ReSearchProcessMemory(List<UInt64> existing, String pattern)
        {
            var arrayOfBytes = pattern.Split(' ').Select(b => b.Contains("?") ? -1 : Convert.ToInt32(b, 16)).ToArray();
            var addresses = new List<UInt64>();
            foreach (var val in existing)
            {
                var buffer = new Byte[4];
                ReadProcessMemory(procHandle, (UInt32)val, buffer, 4, out Int32 bytesRead);
                var results = Scan(buffer, arrayOfBytes).Select(j => (UInt64)j + val).ToList();
                addresses.AddRange(results);
            }
            return addresses;
        }
        public String DumpSurroundString(UInt64 start)
        {
            var buffer = new Byte[0x100];
            ReadProcessMemory(procHandle, (UInt32)(start - 0x80), buffer, buffer.Length, out Int32 bytesRead);
            var val = "";
            for (int i = 0x7f; i > 0; i--)
            {
                if (buffer[i] == 0)
                    break;
                val = Encoding.UTF8.GetString(buffer, i, 1) + val;
            }
            for (int i = 0x80; i < 0x100; i++)
            {
                if (buffer[i] == 0)
                    break;
                val += Encoding.UTF8.GetString(buffer, i, 1);
            }
            return val;
        }
        public String GetString(UInt64 start)
        {
            var buffer = new Byte[0x1000];
            ReadProcessMemory(procHandle, (UInt32)(start), buffer, buffer.Length, out Int32 bytesRead);
            return String.Join(",", buffer.Select(b => "0x" + b.ToString("X2")));
        }
        void IDisposable.Dispose()
        {
            CloseHandle(procHandle);
        }

        static Byte[] FileBytes;
        public static List<Int32> Scan(Byte[] buf, Int32[] pattern)
        {
            var addresses = new List<Int32>();

            for (int i = 0; i <= buf.Length - pattern.Length; i++)
            {
                var found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j] == -1)
                        continue;
                    if (buf[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    addresses.Add(i);
            }
            return addresses;
        }

        public static void SetImageFile(String file)
        {
            FileBytes = System.IO.File.ReadAllBytes(file);
        }
        public static UInt32 GetImageBase()
        {
            var pe = BitConverter.ToUInt16(FileBytes, 0x3c);
            return BitConverter.ToUInt32(FileBytes, pe + 0x34);
        }
        public static UInt32 FindAddr(String sig, Int32 offset, Boolean isOffset = false)
        {
            var arrayOfBytes = sig.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(b => b.Contains("?") ? -1 : Convert.ToInt32(b, 16)).ToArray();
            var offs = Scan(FileBytes, arrayOfBytes);
            if (isOffset)
                return BitConverter.ToUInt32(FileBytes, offs.First() + offset);
            var addr = BitConverter.ToUInt32(FileBytes, offs.First() + offset) - GetImageBase();
            return addr;
        }
        public static UInt32 FindAddr(String sig)
        {
            var arrayOfBytes = sig.Split(' ').Select(b => b.Contains("?") ? -1 : Convert.ToInt32(b, 16)).ToArray();
            var offs = Scan(FileBytes, arrayOfBytes);
            return (UInt32)offs.First() + GetImageBase();
        }
        public IntPtr FindPattern(String pattern)
        {
            //var skip = pattern.ToLower().Contains("cc") ? 0xcc : pattern.ToLower().Contains("aa") ? 0xaa : 0;
            var sigScan = new SigScan(Process, Process.MainModule.BaseAddress, Process.MainModule.ModuleMemorySize);
            var arrayOfBytes = pattern.Split(' ').Select(b => b.Contains("?") ? (Byte)0 : (Byte)Convert.ToInt32(b, 16)).ToArray();
            var strMask = String.Join("", pattern.Split(' ').Select(b => b.Contains("?") ? '?' : 'x'));
            return sigScan.FindPattern(arrayOfBytes, strMask, 0);
        }
        public List<IntPtr> FindPatterns(String pattern)
        {
            //var skip = pattern.ToLower().Contains("cc") ? 0xcc : pattern.ToLower().Contains("aa") ? 0xaa : 0;
            var sigScan = new SigScan(Process, Process.MainModule.BaseAddress, Process.MainModule.ModuleMemorySize);
            var arrayOfBytes = pattern.Split(' ').Select(b => b.Contains("?") ? (Byte)0 : (Byte)Convert.ToInt32(b, 16)).ToArray();
            var strMask = String.Join("", pattern.Split(' ').Select(b => b.Contains("?") ? '?' : 'x'));
            return sigScan.FindPatterns(arrayOfBytes, strMask, 0);
        }
    }
}