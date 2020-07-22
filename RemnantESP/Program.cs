using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace RemnantESP
{
    static class Program
    {
        static void Main()
        {
            var oldProcs = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Where(p=>p.Id != Process.GetCurrentProcess().Id);
            foreach(var oldProc in oldProcs) oldProc.Kill();
            var procName = "Remnant-Win64-Shipping";
            new Engine(new Memory(procName)).UpdateAddresses();
            var esp = new ESP();
            esp.Run();
        }
    }
}
