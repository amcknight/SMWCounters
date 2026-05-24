using System.Collections.Generic;

using LiveSplit.ComponentUtil;

// Offset tables ported verbatim from kaizosplits Components/SMW/SMW/SNES/Offset.cs.
// All offset research credit belongs to that project.

namespace LiveSplit.SmwCounters.Snes;

internal static class Offsets
{
    // Process-name candidates to scan for, in priority order.
    public static readonly string[] KnownProcessNames =
    {
        "retroarch",
        "snes9x",
        "snes9x-x64",
        "snes9x-rr",
        "bsnes",
        "higan",
        "emuhawk",
    };

    // emulator EXE MainModule.ModuleMemorySize -> version label.
    public static readonly Dictionary<int, string> Version = new()
    {
        { 15675392, "1.9.4"  }, // retroarch
        { 16793600, "1.16.0" }, // retroarch
        { 17264640, "1.17.0" }, // retroarch
        { 18350080, "1.21.0" }, // retroarch
        { 20008960, "1.22.2" }, // retroarch
        {  6991872, "1.57"   }, // snes9x
        {  9027584, "1.60"   }, // snes9x
        {  9158656, "1.61"   }, // snes9x
        { 10399744, "1.62.3" }, // snes9x
        { 12537856, "1.59.2" }, // snes9x x64
        { 12836864, "1.60"   }, // snes9x x64
        { 12955648, "1.61"   }, // snes9x x64
        { 29069312, "1.62"   }, // snes9x x64
        { 15474688, "1.62.3" }, // snes9x x64 (also 1.62.2)
        {  9646080, "1.60"   }, // snes9x-rr
        { 13565952, "1.60"   }, // snes9x-rr x64
        { 10096640, "107"    }, // bsnes
        { 10338304, "107.1"  }, // bsnes
        { 47230976, "107.2"  }, // bsnes (also 107.3)
        {131543040, "110"    }, // bsnes
        { 51924992, "111"    }, // bsnes
        { 52056064, "112"    }, // bsnes
        { 52477952, "115"    }, // bsnes
        { 16019456, "106"    }, // higan
        { 15360000, "106.112"}, // higan
        { 22388736, "107"    }, // higan
        { 23142400, "108"    }, // higan
        { 23166976, "109"    }, // higan
        { 23224320, "110"    }, // higan
        {  7061504, "2.3"    }, // BizHawk
        {  7249920, "2.3.1"  }, // BizHawk
        {  6938624, "2.3.2"  }, // BizHawk
    };

    // Direct WRAM base address (module + offset, computed at attach time).
    public static readonly Dictionary<string, long> Mem = new()
    {
        { "higan 106",    0x94D144 },
        { "higan 106.112",0x8AB144 },
        { "higan 107",    0xB0ECC8 },
        { "higan 108",    0xBC7CC8 },
        { "higan 109",    0xBCECC8 },
        { "higan 110",    0xBDBCC8 },
        { "bsnes 107",    0x72BECC },
        { "bsnes 107.1",  0x762F2C },
        { "bsnes 107.2",  0x765F2C },
        { "bsnes 107.3",  0x765F2C },
        { "bsnes 110",    0xA9BD5C },
        { "bsnes 111",    0xA9DD5C },
        { "bsnes 112",    0xAAED7C },
        { "bsnes 115",    0xB16D7C },
        { "emuhawk 2.3",  0x36F11500240 },
        { "emuhawk 2.3.1",0x36F11500240 },
        { "emuhawk 2.3.2",0x36F11500240 },
    };

    // Pointer chain to dereference for WRAM base.
    public static readonly Dictionary<string, DeepPointer> MemPtr = new()
    {
        { "snes9x 1.60",   new DeepPointer("snes9x.exe", 0x54DB54, 0x0) },
        { "snes9x 1.61",   new DeepPointer("snes9x.exe", 0x507BC4, 0x0) },
        { "snes9x 1.62.3", new DeepPointer("snes9x.exe",  0x12698, 0x0) },
        { "snes9x-x64 1.59.2", new DeepPointer("snes9x-x64.exe",  0x8D86F8, 0x0) },
        { "snes9x-x64 1.60",   new DeepPointer("snes9x-x64.exe",  0x8D86F8, 0x0) },
        { "snes9x-x64 1.61",   new DeepPointer("snes9x-x64.exe",  0x883158, 0x0) },
        { "snes9x-x64 1.62",   new DeepPointer("snes9x-x64.exe", 0x1758D40, 0x0) },
        { "snes9x-x64 1.62.2", new DeepPointer("snes9x-x64.exe",  0xA62390, 0x0) },
        { "snes9x-x64 1.62.3", new DeepPointer("snes9x-x64.exe",  0xA62390, 0x0) },
    };

    // RetroArch: pointer to the loaded core's filename (e.g. "snes9x_libretro.dll").
    public static readonly Dictionary<string, DeepPointer> CorePathPtr = new()
    {
        { "retroarch 1.9.4",  new DeepPointer("retroarch.exe", 0xD6A900) },
        { "retroarch 1.16.0", new DeepPointer("retroarch.exe", 0xE8F7E9) },
        { "retroarch 1.17.0", new DeepPointer("retroarch.exe", 0xEEB59A) },
        { "retroarch 1.21.0", new DeepPointer("retroarch.exe", 0xFB157C) },
        { "retroarch 1.22.2", new DeepPointer("retroarch.exe", 0x114F8B9) },
    };

    // RetroArch: pointer to the loaded core's version string.
    public static readonly Dictionary<string, DeepPointer> CoreVersionPtr = new()
    {
        { "retroarch 1.9.4",  new DeepPointer("retroarch.exe", 0xD67600) },
        { "retroarch 1.16.0", new DeepPointer("retroarch.exe", 0xE8C4E9) },
        { "retroarch 1.17.0", new DeepPointer("retroarch.exe", 0xEFD5A9) },
        { "retroarch 1.21.0", new DeepPointer("retroarch.exe", 0xFBE399) },
        { "retroarch 1.22.2", new DeepPointer("retroarch.exe", 0x1150BB9) },
    };

    // Core-DLL relative offset for WRAM base.
    public static readonly Dictionary<string, int> CoreMem = new()
    {
        { "snes9x_libretro.dll 1.62.3 ec4ebfc", 0x3BA164 },
        { "snes9x_libretro.dll 1.63 49f4845",   0x3BB164 },
        { "bsnes_libretro.dll 115",             0x7D39DC },
    };

    public static readonly Dictionary<string, DeepPointer> CoreMemPtr = new()
    {
        { "snes9x2010_libretro.dll 1.52.4 d8b10c4", new DeepPointer("retroarch.exe", 0xEF9FF8, 0x8, 0x0) },
    };
}
