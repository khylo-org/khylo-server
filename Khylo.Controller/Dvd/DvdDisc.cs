using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

#pragma warning disable SA1201 // The helper types sit next to the code that reads the disc with them

namespace Khylo.Controller.Dvd;

/// <summary>
/// Reads a DVD-Video disc: an ISO image, or a VIDEO_TS folder. It reads the disc's own index files to find
/// the titles, chapters, languages and the exact bytes of every scene, so playback needs no mounting.
/// </summary>
public sealed class DvdDisc
{
    private const int Sector = 2048;

    private static readonly ConcurrentDictionary<string, (long Stamp, DvdDisc? Disc)> _cache = new(StringComparer.OrdinalIgnoreCase);

    private DvdDisc(string path)
    {
        Path = path;
    }

    /// <summary>Gets the ISO or folder this disc was read from.</summary>
    public string Path { get; }

    /// <summary>Gets every title, in disc order.</summary>
    public IReadOnlyList<DvdTitle> Titles { get; private set; } = [];

    /// <summary>Gets the longest title, which is almost always the feature.</summary>
    public DvdTitle? MainTitle => Titles.OrderByDescending(t => t.Seconds).FirstOrDefault();

    /// <summary>Gets the menu video files, biggest first.</summary>
    public IReadOnlyList<DvdFile> MenuVobs { get; private set; } = [];

    /// <summary>Gets a value indicating whether the disc is copy-protected (scrambled), so it can't be played.</summary>
    public bool IsScrambled { get; private set; }

    /// <summary>Gets a value indicating whether the path is an ISO image rather than a folder.</summary>
    public bool IsImage { get; private set; }

    /// <summary>Whether a path looks like a disc image.</summary>
    /// <param name="path">The path.</param>
    /// <returns>True for .iso and .img.</returns>
    public static bool IsDiscImagePath(string? path)
    {
        var ext = System.IO.Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) || ext.Equals(".img", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The ffmpeg input that reads part of a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="start">First byte.</param>
    /// <param name="end">Byte after the last.</param>
    /// <returns>The URL.</returns>
    public static string SubfileUrl(string path, long start, long end)
        => string.Create(CultureInfo.InvariantCulture, $"subfile,,start,{start},end,{end},,:{path}");

    /// <summary>Opens a disc (cached until the file changes).</summary>
    /// <param name="path">An ISO file, a VIDEO_TS folder, or a folder holding one.</param>
    /// <returns>The disc, or null if it isn't a readable DVD-Video disc.</returns>
    public static DvdDisc? TryOpen(string path)
    {
        try
        {
            var stamp = Directory.Exists(path)
                ? Directory.GetLastWriteTimeUtc(path).Ticks
                : new FileInfo(path).Length ^ File.GetLastWriteTimeUtc(path).Ticks;
            if (_cache.TryGetValue(path, out var hit) && hit.Stamp == stamp)
            {
                return hit.Disc;
            }

            DvdDisc? disc = null;
            try
            {
                disc = new DvdDisc(path);
                disc.Load();
            }
            catch (Exception)
            {
                disc = null;
            }

            _cache[path] = (stamp, disc);
            return disc;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Gets the byte ranges of a title's menu (or a title's first picture) for taking a still.</summary>
    /// <returns>The segment to read, or null.</returns>
    public DvdSegment? GetMenuSegment()
    {
        var vob = MenuVobs.FirstOrDefault();
        if (vob is null)
        {
            return null;
        }

        // The first few seconds hold the menu's background; no need to read all of it
        var length = Math.Min(vob.Length, 16L * 1024 * 1024);
        return new DvdSegment
        {
            Path = vob.PhysicalPath,
            Start = vob.Offset,
            Length = length,
            IsWholeFile = !IsImage && length == vob.Length
        };
    }

    private static string? Language3(string? two)
    {
        if (string.IsNullOrEmpty(two) || two.Length != 2 || !char.IsLetter(two[0]) || !char.IsLetter(two[1]))
        {
            return null;
        }

        try
        {
            var three = new CultureInfo(two.ToLowerInvariant()).ThreeLetterISOLanguageName;
            return string.IsNullOrEmpty(three) || three.Length != 3 ? null : three;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string? LanguageName(string? two)
    {
        if (Language3(two) is null)
        {
            return null;
        }

        try
        {
            return new CultureInfo(two!.ToLowerInvariant()).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static long U32(byte[] b, int o) => ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];

    private static int Bcd(int v) => ((v >> 4) * 10) + (v & 0xF);

    /// <summary>Playing time in a DVD index file: hours, minutes, seconds and frames, each in BCD.</summary>
    private static double ReadTime(byte[] b, int o)
    {
        var fpsCode = b[o + 3] >> 6;
        var fps = fpsCode == 3 ? 30000.0 / 1001 : fpsCode == 1 ? 25.0 : 0;
        var frames = Bcd(b[o + 3] & 0x3F);
        return (Bcd(b[o]) * 3600) + (Bcd(b[o + 1]) * 60) + Bcd(b[o + 2]) + (fps > 0 ? frames / fps : 0);
    }

    private static byte[] ReadAll(DvdFile file, long maxBytes = 8 * 1024 * 1024)
    {
        using var fs = new FileStream(file.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(file.Offset, SeekOrigin.Begin);
        var length = (int)Math.Min(file.Length, maxBytes);
        var buffer = new byte[length];
        fs.ReadExactly(buffer, 0, length);
        return buffer;
    }

    private static List<DvdFile> ListIsoFiles(string isoPath)
    {
        var files = new List<DvdFile>();
        using var fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var pvd = new byte[Sector];
        fs.Seek(16L * Sector, SeekOrigin.Begin);
        fs.ReadExactly(pvd, 0, Sector);
        if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
        {
            throw new InvalidDataException("Not an ISO 9660 image");
        }

        // The root directory record sits at byte 156 of the primary volume descriptor
        var rootLba = (long)BitConverter.ToUInt32(pvd, 156 + 2);
        var rootLen = (long)BitConverter.ToUInt32(pvd, 156 + 10);

        byte[] ReadDir(long lba, long length)
        {
            var data = new byte[length];
            fs.Seek(lba * Sector, SeekOrigin.Begin);
            fs.ReadExactly(data, 0, (int)length);
            return data;
        }

        IEnumerable<(string Name, bool IsDir, long Lba, long Size)> Entries(byte[] data)
        {
            var pos = 0;
            while (pos < data.Length)
            {
                var len = data[pos];
                if (len == 0)
                {
                    pos = ((pos / Sector) + 1) * Sector;
                    continue;
                }

                var nameLen = data[pos + 32];
                var name = Encoding.Latin1.GetString(data, pos + 33, nameLen);
                if (nameLen > 1 || (nameLen == 1 && data[pos + 33] > 1))
                {
                    var semi = name.IndexOf(';', StringComparison.Ordinal);
                    if (semi >= 0)
                    {
                        name = name[..semi];
                    }

                    yield return (name, (data[pos + 25] & 2) != 0, BitConverter.ToUInt32(data, pos + 2), BitConverter.ToUInt32(data, pos + 10));
                }

                pos += len;
            }
        }

        foreach (var dir in Entries(ReadDir(rootLba, rootLen)).Where(e => e.IsDir && e.Name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var f in Entries(ReadDir(dir.Lba, dir.Size)).Where(e => !e.IsDir))
            {
                files.Add(new DvdFile { Name = f.Name.ToUpperInvariant(), PhysicalPath = isoPath, Offset = f.Lba * Sector, Length = f.Size });
            }
        }

        return files;
    }

    private static List<DvdFile> ListFolderFiles(string path)
    {
        var folder = path;
        if (!System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')).Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            folder = Directory.EnumerateDirectories(path).FirstOrDefault(d => System.IO.Path.GetFileName(d).Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
                     ?? throw new DirectoryNotFoundException("No VIDEO_TS folder");
        }

        return Directory.EnumerateFiles(folder)
            .Select(f => new FileInfo(f))
            .Select(f => new DvdFile { Name = f.Name.ToUpperInvariant(), PhysicalPath = f.FullName, Offset = 0, Length = f.Length })
            .ToList();
    }

    private static byte[]? ReadIfo(IReadOnlyList<DvdFile> files, string name)
    {
        // The .BUP is an exact backup of the .IFO, so use it if the .IFO can't be read
        foreach (var candidate in new[] { name + ".IFO", name + ".BUP" })
        {
            var file = files.FirstOrDefault(f => f.Name == candidate);
            if (file is null)
            {
                continue;
            }

            try
            {
                return ReadAll(file);
            }
            catch (Exception)
            {
                // try the backup
            }
        }

        return null;
    }

    private void Load()
    {
        IsImage = IsDiscImagePath(Path) && File.Exists(Path);
        var files = IsImage ? ListIsoFiles(Path) : ListFolderFiles(Path);

        var vmg = ReadIfo(files, "VIDEO_TS") ?? throw new InvalidDataException("No VIDEO_TS.IFO");
        if (Encoding.ASCII.GetString(vmg, 0, 12) != "DVDVIDEO-VMG")
        {
            throw new InvalidDataException("Not a DVD-Video disc");
        }

        MenuVobs = files
            .Where(f => f.Name == "VIDEO_TS.VOB" || (f.Name.StartsWith("VTS_", StringComparison.Ordinal) && f.Name.EndsWith("_0.VOB", StringComparison.Ordinal)))
            .OrderByDescending(f => f.Length)
            .ToList();

        // Title search table: which title set and which title inside it
        var ttBase = (int)(U32(vmg, 0xC4) * Sector);
        var titleCount = U16(vmg, ttBase);
        var entries = new List<(int Angles, int Ptts, int Set, int Ttn)>();
        for (var i = 0; i < titleCount; i++)
        {
            var o = ttBase + 8 + (i * 12);
            entries.Add((vmg[o + 1], U16(vmg, o + 2), vmg[o + 6], vmg[o + 7]));
        }

        var sets = new Dictionary<int, TitleSet?>();
        TitleSet? GetSet(int n)
        {
            if (!sets.TryGetValue(n, out var set))
            {
                try
                {
                    set = ParseTitleSet(files, n);
                }
                catch (Exception)
                {
                    set = null;
                }

                sets[n] = set;
            }

            return set;
        }

        var titles = new List<DvdTitle>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (_, _, setNumber, ttn) = entries[i];
            var set = GetSet(setNumber);
            if (set is null || ttn < 1 || ttn > set.Ptts.Count)
            {
                continue;
            }

            var title = BuildTitle(i + 1, setNumber, set, set.Ptts[ttn - 1]);
            if (title is not null)
            {
                titles.Add(title);
            }
        }

        Titles = titles;

        // Most store-bought discs are scrambled; the picture data is unreadable without the disc's key
        var main = MainTitle;
        var first = main?.Segments.FirstOrDefault();
        if (first is not null)
        {
            IsScrambled = LooksScrambled(first);
        }
    }

    private static bool LooksScrambled(DvdSegment segment)
    {
        try
        {
            using var fs = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Sector];
            for (var s = 0; s < 64; s++)
            {
                fs.Seek(segment.Start + (s * Sector), SeekOrigin.Begin);
                if (fs.Read(buffer, 0, Sector) < Sector)
                {
                    break;
                }

                // Pack header, then look for a video or private-stream PES packet and its scrambling bits
                if (buffer[0] != 0 || buffer[1] != 0 || buffer[2] != 1 || buffer[3] != 0xBA)
                {
                    continue;
                }

                for (var i = 14; i < Sector - 8; i++)
                {
                    if (buffer[i] == 0 && buffer[i + 1] == 0 && buffer[i + 2] == 1 && (buffer[i + 3] == 0xE0 || buffer[i + 3] == 0xBD))
                    {
                        if (((buffer[i + 6] >> 4) & 3) != 0)
                        {
                            return true;
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private TitleSet ParseTitleSet(List<DvdFile> files, int n)
    {
        var ifo = ReadIfo(files, string.Create(CultureInfo.InvariantCulture, $"VTS_{n:00}_0")) ?? throw new InvalidDataException("No title set index");
        if (Encoding.ASCII.GetString(ifo, 0, 12) != "DVDVIDEO-VTS")
        {
            throw new InvalidDataException("Bad title set");
        }

        var set = new TitleSet();

        // Video attributes
        var v0 = ifo[0x200];
        var v1 = ifo[0x201];
        var pal = ((v0 >> 4) & 3) == 1;
        set.Wide = ((v0 >> 2) & 3) == 3;
        set.Fps = pal ? 25.0 : 30000.0 / 1001;
        var fullHeight = pal ? 576 : 480;
        (set.Width, set.Height) = ((v1 >> 3) & 3) switch
        {
            0 => (720, fullHeight),
            1 => (704, fullHeight),
            2 => (352, fullHeight),
            _ => (352, fullHeight / 2)
        };

        // Audio and subtitle attributes
        var audioCount = Math.Min(U16(ifo, 0x202), 8);
        for (var i = 0; i < audioCount; i++)
        {
            var o = 0x204 + (i * 8);
            var format = ifo[o] >> 5;
            var hasLang = ((ifo[o] >> 2) & 3) == 1;
            var lang = hasLang ? Encoding.ASCII.GetString(ifo, o + 2, 2) : null;
            var ext = ifo[o + 5];
            set.AllAudio.Add(new DvdAudioTrack
            {
                Index = i,
                Codec = format switch { 0 => "ac3", 2 or 3 => "mp2", 4 => "pcm_dvd", 6 => "dts", _ => "ac3" },
                Channels = (ifo[o + 1] & 7) + 1,
                Language = Language3(lang),
                LanguageName = LanguageName(lang),
                Note = ext switch { 2 => "For the visually impaired", 3 => "Director's commentary", 4 => "Director's commentary", _ => null }
            });
        }

        var subCount = Math.Min(U16(ifo, 0x254), 32);
        for (var i = 0; i < subCount; i++)
        {
            var o = 0x256 + (i * 6);
            var hasLang = (ifo[o] & 3) == 1;
            var lang = hasLang ? Encoding.ASCII.GetString(ifo, o + 2, 2) : null;
            var ext = ifo[o + 5];
            set.AllSubtitles.Add(new DvdSubtitleTrack
            {
                Index = i,
                Language = Language3(lang),
                LanguageName = LanguageName(lang),
                Note = ext switch { 2 => "Large", 3 => "For children", 5 => "Captions", 6 => "Captions", 7 => "Captions", 9 => "Forced", 13 or 14 => "Director's commentary", _ => null }
            });
        }

        // Which of a title's pieces are in which VOB file: VTS_nn_1.VOB, _2, _3 ... play one after another
        for (var part = 1; part <= 9; part++)
        {
            var vob = files.FirstOrDefault(f => f.Name == string.Create(CultureInfo.InvariantCulture, $"VTS_{n:00}_{part}.VOB"));
            if (vob is null)
            {
                break;
            }

            set.VobFiles.Add(vob);
            set.TotalSectors += vob.Length / Sector;
        }

        // Title program chains
        var pgcitBase = (int)(U32(ifo, 0xCC) * Sector);
        var pgcCount = U16(ifo, pgcitBase);
        for (var i = 0; i < pgcCount; i++)
        {
            var start = pgcitBase + (int)U32(ifo, pgcitBase + 8 + (i * 8) + 4);
            set.Pgcs.Add(ParsePgc(ifo, start));
        }

        // Chapters of every title: which program chain and which program each chapter starts at
        var pttBase = (int)(U32(ifo, 0xC8) * Sector);
        var titleCount = U16(ifo, pttBase);
        var lastByte = (int)U32(ifo, pttBase + 4);
        for (var t = 0; t < titleCount; t++)
        {
            var from = (int)U32(ifo, pttBase + 8 + (t * 4));
            var to = t + 1 < titleCount ? (int)U32(ifo, pttBase + 8 + ((t + 1) * 4)) : lastByte + 1;
            var list = new List<(int, int)>();
            for (var o = from; o + 4 <= to; o += 4)
            {
                list.Add((U16(ifo, pttBase + o), U16(ifo, pttBase + o + 2)));
            }

            set.Ptts.Add(list);
        }

        return set;
    }

    private static Pgc ParsePgc(byte[] b, int start)
    {
        var pgc = new Pgc();
        var programs = b[start + 2];
        var cells = b[start + 3];
        pgc.Seconds = ReadTime(b, start + 4);

        for (var i = 0; i < 8; i++)
        {
            var c = U16(b, start + 0xC + (i * 2));
            pgc.AudioAvailable[i] = (c & 0x8000) != 0;
            pgc.AudioStream[i] = (c >> 8) & 7;
        }

        for (var i = 0; i < 32; i++)
        {
            var c = U32(b, start + 0x1C + (i * 4));
            pgc.SubAvailable[i] = (c & 0x80000000) != 0;
            pgc.SubStream[i] = (int)((c >> 16) & 0x1F);
        }

        var programMap = U16(b, start + 0xE6);
        var cellPlayback = U16(b, start + 0xE8);
        if (programMap > 0)
        {
            for (var i = 0; i < programs; i++)
            {
                pgc.ProgramEntryCells.Add(b[start + programMap + i]);
            }
        }

        if (cellPlayback > 0)
        {
            var inAngles = false;
            var elapsed = 0.0;
            for (var i = 0; i < cells; i++)
            {
                var o = start + cellPlayback + (i * 24);
                var blockMode = b[o] >> 6;
                var blockType = (b[o] >> 4) & 3;
                var cell = new Cell
                {
                    Seconds = ReadTime(b, o + 4),
                    FirstSector = U32(b, o + 8),
                    LastSector = U32(b, o + 20)
                };

                // Several camera angles are stored one after another; play only the first
                if (blockType == 1)
                {
                    if (blockMode == 1)
                    {
                        inAngles = true;
                    }
                    else if (inAngles)
                    {
                        cell.Skip = true;
                    }

                    if (blockMode == 3)
                    {
                        inAngles = false;
                    }
                }

                pgc.CellStarts.Add(elapsed);
                if (!cell.Skip)
                {
                    elapsed += cell.Seconds;
                }

                pgc.Cells.Add(cell);
            }

            pgc.Seconds = pgc.Cells.Where(c => !c.Skip).Sum(c => c.Seconds);
        }

        return pgc;
    }

    private DvdTitle? BuildTitle(int number, int setNumber, TitleSet set, List<(int Pgcn, int Pgn)> ptts)
    {
        var chain = ptts.Select(p => p.Pgcn).Where(p => p >= 1 && p <= set.Pgcs.Count).Distinct().ToList();
        if (chain.Count == 0 || set.VobFiles.Count == 0)
        {
            return null;
        }

        var chapters = new List<DvdChapter>();
        var segments = new List<DvdSegment>();
        var elapsed = 0.0;
        var pgcStart = new Dictionary<int, double>();

        foreach (var pgcn in chain)
        {
            var pgc = set.Pgcs[pgcn - 1];
            pgcStart[pgcn] = elapsed;
            foreach (var cell in pgc.Cells.Where(c => !c.Skip))
            {
                AddCell(segments, set, cell);
            }

            elapsed += pgc.Seconds;
        }

        for (var i = 0; i < ptts.Count; i++)
        {
            var (pgcn, pgn) = ptts[i];
            if (!pgcStart.TryGetValue(pgcn, out var baseTime))
            {
                continue;
            }

            var pgc = set.Pgcs[pgcn - 1];
            var entryCell = pgn >= 1 && pgn <= pgc.ProgramEntryCells.Count ? pgc.ProgramEntryCells[pgn - 1] : 1;
            var within = entryCell >= 1 && entryCell <= pgc.CellStarts.Count ? pgc.CellStarts[entryCell - 1] : 0;
            chapters.Add(new DvdChapter { Number = chapters.Count + 1, StartSeconds = baseTime + within });
        }

        if (segments.Count == 0 || elapsed <= 0)
        {
            return null;
        }

        var first = set.Pgcs[chain[0] - 1];
        var audio = new List<DvdAudioTrack>();
        for (var i = 0; i < set.AllAudio.Count; i++)
        {
            if (first.AudioAvailable[i])
            {
                audio.Add(set.AllAudio[i]);
            }
        }

        var subtitles = new List<DvdSubtitleTrack>();
        for (var i = 0; i < set.AllSubtitles.Count; i++)
        {
            if (first.SubAvailable[i])
            {
                subtitles.Add(set.AllSubtitles[i]);
            }
        }

        return new DvdTitle
        {
            Number = number,
            TitleSet = setNumber,
            Seconds = elapsed,
            Chapters = chapters,
            Audio = audio.Count > 0 ? audio : set.AllAudio,
            Subtitles = subtitles,
            Width = set.Width,
            Height = set.Height,
            FrameRate = set.Fps,
            IsWidescreen = set.Wide,
            Segments = segments
        };
    }

    /// <summary>Adds the bytes of one cell, merging with the piece before it when they sit side by side.</summary>
    private void AddCell(List<DvdSegment> segments, TitleSet set, Cell cell)
    {
        // Sectors count across the title's VOB files as if they were one long file
        long fileStartSector = 0;
        var totalSectors = cell.LastSector - cell.FirstSector + 1;
        if (totalSectors <= 0)
        {
            return;
        }

        foreach (var vob in set.VobFiles)
        {
            var sectors = vob.Length / Sector;
            var from = Math.Max(cell.FirstSector, fileStartSector);
            var to = Math.Min(cell.LastSector, fileStartSector + sectors - 1);
            if (from <= to)
            {
                var byteStart = vob.Offset + ((from - fileStartSector) * Sector);
                var length = (to - from + 1) * Sector;
                var share = cell.Seconds * ((double)(to - from + 1) / totalSectors);
                var whole = !IsImage && from == fileStartSector && to == fileStartSector + sectors - 1;

                var last = segments.Count > 0 ? segments[^1] : null;
                if (last is not null && !whole && !last.IsWholeFile && last.Path == vob.PhysicalPath && last.Start + last.Length == byteStart)
                {
                    last.Length += length;
                    last.Seconds += share;
                }
                else
                {
                    segments.Add(new DvdSegment
                    {
                        Path = vob.PhysicalPath,
                        Start = byteStart,
                        Length = length,
                        Seconds = share,
                        IsWholeFile = whole
                    });
                }
            }

            fileStartSector += sectors;
        }
    }

#pragma warning disable SA1401 // Plain data holders used only while reading the disc
    private sealed class Cell
    {
        public double Seconds;
        public long FirstSector;
        public long LastSector;
        public bool Skip;
    }

    private sealed class Pgc
    {
        public double Seconds;
        public List<Cell> Cells = [];
        public List<int> ProgramEntryCells = [];
        public List<double> CellStarts = [];
        public int[] AudioStream = new int[8];
        public bool[] AudioAvailable = new bool[8];
        public int[] SubStream = new int[32];
        public bool[] SubAvailable = new bool[32];
    }

    private sealed class TitleSet
    {
        public List<Pgc> Pgcs = [];
        public List<List<(int Pgcn, int Pgn)>> Ptts = [];
        public List<DvdAudioTrack> AllAudio = [];
        public List<DvdSubtitleTrack> AllSubtitles = [];
        public int Width;
        public int Height;
        public double Fps;
        public bool Wide;
        public List<DvdFile> VobFiles = [];
        public long TotalSectors;
    }

#pragma warning restore SA1401

}
