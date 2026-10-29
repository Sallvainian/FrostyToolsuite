using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;
using Sharprompt;
using AesNi = System.Runtime.Intrinsics.X86.Aes;

namespace FrostyCli;

internal static unsafe partial class Program
{
    private const int c_scanChunkSize = 64 * 1024 * 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoVec
    {
        public void* Base;
        public nuint Length;
    }

    [LibraryImport("libc", EntryPoint = "process_vm_readv", SetLastError = true)]
    private static partial nint ProcessVmReadv(int inPid, IoVec* inLocal, ulong inLocalCount, IoVec* inRemote,
        ulong inRemoteCount, ulong inFlags);

    /// <summary>
    /// Recovers the initfs key from the memory of the running game, by testing every 16 byte window as an aes key
    /// against the encrypted initfs. Linux only, since it reads the memory of the wine process directly.
    /// </summary>
    private static void FindInitFsKey(FileInfo? inGameFileInfo, int? inPid, FileInfo? inOutputFileInfo)
    {
        if (!OperatingSystem.IsLinux())
        {
            Logger.LogErrorInternal("find-initfs-key is only supported on Linux.");
            return;
        }

        FileInfo? game = inGameFileInfo ?? RequestFile("Input the path to the games executable");
        if (game?.Exists != true || game.DirectoryName is null)
        {
            Logger.LogErrorInternal("Game does not exist.");
            return;
        }

        // the profile decides how the initfs file is deobfuscated
        FrostyLogger.Logger = new Logger();
        Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(game.Name)))
        {
            return;
        }

        string? initFsPath = new[] { "Patch", "Data" }
            .Select(dir => Path.Combine(game.DirectoryName, dir, "initfs_Win32"))
            .FirstOrDefault(File.Exists);
        if (initFsPath is null)
        {
            Logger.LogErrorInternal("Could not find initfs_Win32 in the games Patch or Data folder.");
            return;
        }

        DbObject? initFs = DbObject.Deserialize(initFsPath);
        byte[]? encrypted = initFs?.IsDict() == true ? initFs.AsDict().AsBlob("encrypted", null) : null;
        if (encrypted is null || encrypted.Length < 32)
        {
            Logger.LogErrorInternal("The initfs is not encrypted, no key is needed.");
            return;
        }

        int pid = inPid ?? FindGameProcess(game.Name) ?? Prompt.Input<int>("Input pid of the currently running game");

        List<(nint Start, nint End, string Path)> regions = GetReadableRegions(pid, game.Name);
        long total = regions.Sum(r => (long)(r.End - r.Start));
        Logger.LogInfoInternal($"Scanning {total / (1024 * 1024)} MiB of memory in pid {pid} for the initfs key");

        Vector128<byte> c0 = Vector128.Create(encrypted, 0);
        Vector128<byte> c1 = Vector128.Create(encrypted, 16);

        byte[]? found = null;
        long scanned = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        // split big heaps into chunks so every core gets work, each chunk reads 15 bytes into the next one so keys
        // crossing a chunk boundary are still found
        List<(nint Start, int Size)> chunks = new();
        foreach ((nint start, nint end, _) in regions)
        {
            for (nint address = start; address < end; address += c_scanChunkSize)
            {
                chunks.Add((address, (int)Math.Min(c_scanChunkSize + 15, end - address)));
            }
        }

        // the exe image comes first in the list, so a hit there stops the scan early
        Parallel.ForEach(chunks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            () => new byte[c_scanChunkSize + 15],
            (chunk, state, buffer) =>
            {
                if (state.IsStopped)
                {
                    return buffer;
                }

                int read = ReadRemote(pid, chunk.Start, buffer, chunk.Size);
                if (read < 16)
                {
                    return buffer;
                }

                byte[]? key = ScanBuffer(buffer.AsSpan(0, read), c0, c1, encrypted);
                if (key is not null)
                {
                    Interlocked.CompareExchange(ref found, key, null);
                    state.Stop();
                }

                Interlocked.Add(ref scanned, read);
                return buffer;
            },
            _ => { });

        Logger.LogInfoInternal(
            $"Scanned {scanned / (1024 * 1024)} MiB in {stopwatch.Elapsed.TotalSeconds:F1}s");

        if (found is null)
        {
            Logger.LogErrorInternal(
                "No initfs key found. Make sure the game is running and has reached the main menu, then try again.");
            return;
        }

        string output = inOutputFileInfo?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), "initfs.key");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, found);
        Logger.LogInfoInternal($"Found initfs key, saved to {output}");
    }

    private static int? FindGameProcess(string inExeName)
    {
        // /proc/<pid>/comm is truncated to 15 characters
        string comm = inExeName.Length > 15 ? inExeName[..15] : inExeName;
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid))
            {
                continue;
            }

            try
            {
                if (File.ReadAllText(Path.Combine(dir, "comm")).TrimEnd('\n') == comm)
                {
                    Logger.LogInfoInternal($"Found {inExeName} with pid {pid}");
                    return pid;
                }
            }
            catch (IOException)
            {
                // process exited while enumerating
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static List<(nint Start, nint End, string Path)> GetReadableRegions(int inPid, string inExeName)
    {
        List<(nint, nint, string)> regions = new();
        foreach (string line in File.ReadLines($"/proc/{inPid}/maps"))
        {
            string[] arr = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (arr[1][0] != 'r')
            {
                continue;
            }

            string path = arr.Length > 5 ? arr[5].Trim() : string.Empty;

            // device mappings (gpu memory) can hang or fault when read, the vdso pages are useless
            if (path.StartsWith("/dev/") || path.StartsWith("[v"))
            {
                continue;
            }

            int index = arr[0].IndexOf('-');
            nint start = nint.Parse(arr[0][..index], System.Globalization.NumberStyles.HexNumber);
            nint end = nint.Parse(arr[0][(index + 1)..], System.Globalization.NumberStyles.HexNumber);
            regions.Add((start, end, path));
        }

        // scan the exe image first, the rest by address
        return regions.OrderBy(r => r.Item3.EndsWith(inExeName) ? 0 : 1).ToList();
    }

    private static int ReadRemote(int inPid, nint inAddress, byte[] outBuffer, int inSize)
    {
        fixed (byte* ptr = outBuffer)
        {
            IoVec local = new() { Base = ptr, Length = (nuint)inSize };
            IoVec remote = new() { Base = (void*)inAddress, Length = (nuint)inSize };
            return (int)ProcessVmReadv(inPid, &local, 1, &remote, 1, 0);
        }
    }

    private static byte[]? ScanBuffer(ReadOnlySpan<byte> inBuffer, Vector128<byte> inC0, Vector128<byte> inC1,
        byte[] inEncrypted)
    {
        ReadOnlySpan<byte> file = "$file"u8;
        Span<byte> plain = stackalloc byte[32];

        for (int i = 0; i + 16 <= inBuffer.Length; i++)
        {
            ReadOnlySpan<byte> candidate = inBuffer.Slice(i, 16);

            // zeroes dominate memory and a random key almost never has more than 2 of them
            if (CountZeroes(candidate) > 2)
            {
                continue;
            }

            Vector128<byte> key = Vector128.Create(candidate);

            // cbc with iv = key, so the first plaintext block is D(c0) ^ key
            DecryptTwoBlocks(key, inC0, inC1, out Vector128<byte> p0, out Vector128<byte> p1);
            p0 ^= key;
            p1 ^= inC0;

            // initfs is an anonymous list of anonymous dicts holding a "$file" dict
            if (p0.GetElement(0) != 0x81)
            {
                continue;
            }

            p0.CopyTo(plain);
            p1.CopyTo(plain[16..]);
            if (plain.IndexOf(file) < 0)
            {
                continue;
            }

            byte[] keyBytes = candidate.ToArray();
            if (Verify(keyBytes, inEncrypted))
            {
                return keyBytes;
            }
        }

        return null;
    }

    private static int CountZeroes(ReadOnlySpan<byte> inSpan)
    {
        Vector128<byte> v = Vector128.Create(inSpan);
        return BitOperations.PopCount(Vector128.Equals(v, Vector128<byte>.Zero).ExtractMostSignificantBits());
    }

    private static bool Verify(byte[] inKey, byte[] inEncrypted)
    {
        try
        {
            using BlockStream stream = new(new Block<byte>(inEncrypted.ToArray()));
            stream.Decrypt(inKey, PaddingMode.PKCS7);
            stream.Position = 0;
            return DbObject.Deserialize(stream)?.IsList() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DecryptTwoBlocks(Vector128<byte> inKey, Vector128<byte> inC0, Vector128<byte> inC1,
        out Vector128<byte> outP0, out Vector128<byte> outP1)
    {
        if (!AesNi.IsSupported)
        {
            using System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create();
            Span<byte> keyBytes = stackalloc byte[16];
            Span<byte> data = stackalloc byte[32];
            inKey.CopyTo(keyBytes);
            inC0.CopyTo(data);
            inC1.CopyTo(data[16..]);
            aes.Key = keyBytes.ToArray();
            aes.DecryptEcb(data, data, PaddingMode.None);
            outP0 = Vector128.Create((ReadOnlySpan<byte>)data[..16]);
            outP1 = Vector128.Create((ReadOnlySpan<byte>)data[16..]);
            return;
        }

        // aes-128 key expansion, KeygenAssist needs constant round constants
        Vector128<byte> k0 = inKey;
        Vector128<byte> k1 = ExpandKey(k0, AesNi.KeygenAssist(k0, 0x01));
        Vector128<byte> k2 = ExpandKey(k1, AesNi.KeygenAssist(k1, 0x02));
        Vector128<byte> k3 = ExpandKey(k2, AesNi.KeygenAssist(k2, 0x04));
        Vector128<byte> k4 = ExpandKey(k3, AesNi.KeygenAssist(k3, 0x08));
        Vector128<byte> k5 = ExpandKey(k4, AesNi.KeygenAssist(k4, 0x10));
        Vector128<byte> k6 = ExpandKey(k5, AesNi.KeygenAssist(k5, 0x20));
        Vector128<byte> k7 = ExpandKey(k6, AesNi.KeygenAssist(k6, 0x40));
        Vector128<byte> k8 = ExpandKey(k7, AesNi.KeygenAssist(k7, 0x80));
        Vector128<byte> k9 = ExpandKey(k8, AesNi.KeygenAssist(k8, 0x1B));
        Vector128<byte> k10 = ExpandKey(k9, AesNi.KeygenAssist(k9, 0x36));

        Vector128<byte> d9 = AesNi.InverseMixColumns(k9);
        Vector128<byte> d8 = AesNi.InverseMixColumns(k8);
        Vector128<byte> d7 = AesNi.InverseMixColumns(k7);
        Vector128<byte> d6 = AesNi.InverseMixColumns(k6);
        Vector128<byte> d5 = AesNi.InverseMixColumns(k5);
        Vector128<byte> d4 = AesNi.InverseMixColumns(k4);
        Vector128<byte> d3 = AesNi.InverseMixColumns(k3);
        Vector128<byte> d2 = AesNi.InverseMixColumns(k2);
        Vector128<byte> d1 = AesNi.InverseMixColumns(k1);

        Vector128<byte> s0 = inC0 ^ k10;
        Vector128<byte> s1 = inC1 ^ k10;
        s0 = AesNi.Decrypt(s0, d9); s1 = AesNi.Decrypt(s1, d9);
        s0 = AesNi.Decrypt(s0, d8); s1 = AesNi.Decrypt(s1, d8);
        s0 = AesNi.Decrypt(s0, d7); s1 = AesNi.Decrypt(s1, d7);
        s0 = AesNi.Decrypt(s0, d6); s1 = AesNi.Decrypt(s1, d6);
        s0 = AesNi.Decrypt(s0, d5); s1 = AesNi.Decrypt(s1, d5);
        s0 = AesNi.Decrypt(s0, d4); s1 = AesNi.Decrypt(s1, d4);
        s0 = AesNi.Decrypt(s0, d3); s1 = AesNi.Decrypt(s1, d3);
        s0 = AesNi.Decrypt(s0, d2); s1 = AesNi.Decrypt(s1, d2);
        s0 = AesNi.Decrypt(s0, d1); s1 = AesNi.Decrypt(s1, d1);
        outP0 = AesNi.DecryptLast(s0, k0);
        outP1 = AesNi.DecryptLast(s1, k0);
    }

    private static Vector128<byte> ExpandKey(Vector128<byte> inKey, Vector128<byte> inAssist)
    {
        Vector128<byte> assist = Sse2.Shuffle(inAssist.AsInt32(), 0xFF).AsByte();
        Vector128<byte> key = inKey;
        key ^= Sse2.ShiftLeftLogical128BitLane(key, 4);
        key ^= Sse2.ShiftLeftLogical128BitLane(key, 4);
        key ^= Sse2.ShiftLeftLogical128BitLane(key, 4);
        return key ^ assist;
    }
}
