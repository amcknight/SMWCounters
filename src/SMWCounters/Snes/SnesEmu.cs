using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using LiveSplit.ComponentUtil;

// Emulator detection + WRAM-offset tables ported from kaizosplits
// (https://github.com/...kaizosplits, Components/SMW/SMW/SNES/{Emu,Offset}.cs).
// All credit for the offset research belongs to that project.

namespace LiveSplit.SmwCounters.Snes;

internal sealed class SnesEmu : ISnesMemory
{
    public string ProcessName { get; private set; }
    public string EmuVersion { get; private set; }
    public string Core { get; private set; }
    public string CoreVersion { get; private set; }
    public long WramBase { get; private set; }
    public string LastError { get; private set; }

    public Process Process { get; private set; }

    public bool IsAttached => Process != null && !Process.HasExited && WramBase != 0;

    public string Describe()
    {
        if (LastError != null && Process == null) { return LastError; }
        if (Process == null) { return "no emulator found"; }
        if (string.IsNullOrEmpty(EmuVersion)) { return $"{Process.ProcessName}: unknown version"; }
        string core = string.IsNullOrEmpty(Core) ? "" : $" / {Core} {CoreVersion}";
        return $"{ProcessName} {EmuVersion}{core}";
    }

    public void Detach()
    {
        Process = null;
        ProcessName = EmuVersion = Core = CoreVersion = null;
        WramBase = 0;
    }

    // Returns true if attached and WramBase is valid for reading.
    public bool TryAttach()
    {
        if (Process != null && Process.HasExited) { Detach(); }

        if (Process == null)
        {
            Process = FindKnownEmu();
            if (Process == null)
            {
                LastError = "no emulator found";
                return false;
            }
            ProcessName = Process.ProcessName.ToLowerInvariant();
        }

        try
        {
            if (string.IsNullOrEmpty(EmuVersion))
            {
                int size = Process.MainModuleWow64Safe().ModuleMemorySize;
                if (!Offsets.Version.TryGetValue(size, out string ver))
                {
                    LastError = $"unknown {ProcessName} build (module size {size})";
                    return false;
                }
                EmuVersion = ver;
            }

            if (ProcessName == "retroarch")
            {
                if (!ResolveRetroArchCore()) { return false; }
            }

            if (WramBase == 0)
            {
                if (!ResolveWramBase()) { return false; }
            }

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static Process FindKnownEmu()
    {
        foreach (string name in Offsets.KnownProcessNames)
        {
            Process p = Process.GetProcessesByName(name).FirstOrDefault();
            if (p != null) { return p; }
        }
        return null;
    }

    private bool ResolveRetroArchCore()
    {
        string emuKey = Key(ProcessName, EmuVersion);

        if (!Offsets.CorePathPtr.TryGetValue(emuKey, out DeepPointer corePathPtr))
        {
            LastError = $"no core-path pointer for '{emuKey}'";
            return false;
        }
        Core = Path.GetFileName(corePathPtr.DerefString(Process, 512) ?? "");

        if (!Offsets.CoreVersionPtr.TryGetValue(emuKey, out DeepPointer coreVerPtr))
        {
            LastError = $"no core-version pointer for '{emuKey}'";
            return false;
        }
        CoreVersion = coreVerPtr.DerefString(Process, 32) ?? "";

        if (string.IsNullOrWhiteSpace(Core)) { LastError = "no core loaded in RetroArch"; return false; }
        if (string.IsNullOrWhiteSpace(CoreVersion)) { LastError = $"no version for core '{Core}'"; return false; }
        return true;
    }

    private bool ResolveWramBase()
    {
        if (ProcessName == "retroarch")
        {
            string coreKey = Key(Core, CoreVersion);
            DeepPointer corePtr;
            if (Offsets.CoreMem.TryGetValue(coreKey, out int directOff))
            {
                corePtr = new DeepPointer(Core, directOff);
            }
            else if (!Offsets.CoreMemPtr.TryGetValue(coreKey, out corePtr))
            {
                LastError = $"no WRAM offset for core '{coreKey}'";
                return false;
            }
            if (!corePtr.DerefOffsets(Process, out IntPtr addr))
            {
                LastError = $"failed deref core '{coreKey}'";
                return false;
            }
            WramBase = addr.ToInt64();
        }
        else
        {
            string emuKey = Key(ProcessName, EmuVersion);
            if (Offsets.MemPtr.TryGetValue(emuKey, out DeepPointer ptr))
            {
                if (!ptr.DerefOffsets(Process, out IntPtr addr))
                {
                    LastError = $"failed deref '{emuKey}'";
                    return false;
                }
                WramBase = addr.ToInt64();
            }
            else if (Offsets.Mem.TryGetValue(emuKey, out long direct))
            {
                // kaizosplits ships these as already-absolute (assumes default EXE base, no ASLR).
                WramBase = direct;
            }
            else
            {
                LastError = $"no WRAM offset for '{emuKey}'";
                return false;
            }
        }

        if (WramBase == 0) { LastError = "WRAM base resolved to 0"; return false; }
        return true;
    }

    // Reads a byte from a SNES WRAM address (0x0000–0x1FFFF).
    public bool ReadWramByte(int snesOffset, out byte value)
    {
        value = 0;
        if (!IsAttached) { return false; }
        return Process.ReadValue((IntPtr)(WramBase + snesOffset), out value);
    }

    private static string Key(string a, string b) => $"{a} {b}";
}
