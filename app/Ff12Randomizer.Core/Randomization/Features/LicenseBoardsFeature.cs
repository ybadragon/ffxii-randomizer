using System.Buffers.Binary;
using System.Text;
using Ff12Randomizer.Core.GameData;

namespace Ff12Randomizer.Core.Randomization.Features;

/// <summary>
/// The 12 job boards, ps2data/image/ff12/test_battle/in/binaryfile/board_1..12.bin: "licd", u16 24, u16 24, then a 24 x 24
/// grid of u16 license ids, 0xFFFF for an empty cell (layout from Bartz24's FFModdingWPF, MIT). License records are
/// battle_pack.bin section 12 (0x18 bytes, type at 0x05: 19 Quickening, 20 Esper, 30 Second Board) and names are text
/// table 3. Board shapes never change, and Essentials (31, every board's starting cell), Espers and Second Board licenses
/// stay in their cells unless the Espers option moves Espers between Esper cells, so every board stays fully learnable.
/// Boards: Shuffle rearranges each board's own licenses; Randomize also mixes licenses between boards, keeping each
/// board's cell count. Quickenings: swapped among the Quickening cells of all boards.
/// </summary>
public sealed class LicenseBoardsFeature : IRandomizerFeature
{
    public const string Boards = "boards", Espers = "espers", Quickenings = "quickenings";
    public const int Boardcount = 12, Size = 24, HeaderSize = 8, Essentials = 31;
    public const int LicenseSection = 12, LicenseNamesTable = 3, TypeOffset = 0x05;
    public const byte QuickeningType = 19, EsperType = 20, SecondBoardType = 30;

    /// <summary>
    /// Job names are menu_command.bin text ids 10100-10111 (0x2774-0x277F) in board order: White Mage, Uhlan, Machinist...
    /// The order matches the boards' contents (board 1 holds the most White Magick, board 7 Time Magick, board 10 Black Magick).
    /// </summary>
    public const uint FirstJobNameId = 10100;
    public const string MenuCommandPath = "ps2data/image/ff12/test_battle/us/binaryfile/menu_command.bin";

    public static string BoardPath(int board) => $"ps2data/image/ff12/test_battle/in/binaryfile/board_{board}.bin";

    public bool IsEnabled(RandomizerSettings settings) =>
        BoardMode(settings) != "orig" || settings.IsOn(Espers) || settings.IsOn(Quickenings);

    private static string BoardMode(RandomizerSettings settings) => settings.ChoiceOf(OptionCatalog.AllGroups.First(g => g.Id == Boards));

    private sealed record Cell(byte[] Board, int Offset)
    {
        public ushort License
        {
            get => BinaryPrimitives.ReadUInt16LittleEndian(Board.AsSpan(Offset));
            set => BinaryPrimitives.WriteUInt16LittleEndian(Board.AsSpan(Offset), value);
        }
    }

    public void Apply(RandomizerContext context)
    {
        var pack = new BattlePack(context.Original.Extract(context.Original.Find(BattlePack.UsPath)!));
        var licenses = pack.Table(LicenseSection);
        byte TypeOf(ushort id) => id < licenses.Count ? licenses.Record(id)[TypeOffset] : (byte)0;

        var boards = Enumerable.Range(1, Boardcount).Select(n => context.Edit(BoardPath(n))).ToList();
        foreach (var (board, n) in boards.Select((b, i) => (b, i + 1)))
        {
            if (board.Length != HeaderSize + Size * Size * 2 || Encoding.ASCII.GetString(board, 0, 4) != "licd")
                throw new InvalidDataException($"board_{n}.bin isn't a license board.");
        }
        var before = boards.Select(b => b.ToArray()).ToList();
        var cells = boards.Select(b => Enumerable.Range(0, Size * Size).Select(i => new Cell(b, HeaderSize + i * 2))
            .Where(c => c.License != 0xFFFF).ToList()).ToList();

        bool Fixed(ushort id) => id == Essentials || TypeOf(id) is EsperType or SecondBoardType;

        string mode = BoardMode(context.Settings);
        if (mode != "orig")
        {
            var rng = context.RngFor(Boards);
            if (mode == "shuffle")
            {
                foreach (var boardCells in cells) ShuffleInto(rng, boardCells.Where(c => !Fixed(c.License) && TypeOf(c.License) != QuickeningType).ToList());
            }
            else
            {
                ShuffleInto(rng, cells.SelectMany(c => c).Where(c => !Fixed(c.License) && TypeOf(c.License) != QuickeningType).ToList());
            }
        }
        if (context.Settings.IsOn(Espers))
            ShuffleInto(context.RngFor(Espers), cells.SelectMany(c => c).Where(c => TypeOf(c.License) == EsperType).ToList());
        if (context.Settings.IsOn(Quickenings))
            ShuffleInto(context.RngFor(Quickenings), cells.SelectMany(c => c).Where(c => TypeOf(c.License) == QuickeningType).ToList());

        // Spoiler: per board (named by its job), each license that moved and where to (row, column), in the game's names,
        // with the kind of license before and after.
        string Name(ushort id) => pack.Text(LicenseNamesTable, id) ?? $"License {id}";
        var kinds = LicenseKinds.Read(pack);
        var menu = context.Original.Find(MenuCommandPath) is { } entry ? new St2eText(context.Original.Extract(entry)) : null;
        for (int b = 0; b < Boardcount; b++)
        {
            var moved = new List<object>();
            for (int i = 0; i < Size * Size; i++)
            {
                ushort now = BinaryPrimitives.ReadUInt16LittleEndian(boards[b].AsSpan(HeaderSize + i * 2));
                ushort was = BinaryPrimitives.ReadUInt16LittleEndian(before[b].AsSpan(HeaderSize + i * 2));
                // Licenses that share a name (ten Gambit Slots, sixteen Battle Lores) trading places changes nothing a player sees.
                if (now == was || Name(now) == Name(was)) continue;
                moved.Add(new { row = i / Size + 1, column = i % Size + 1, license = Name(now), kind = kinds.Of(now), was = Name(was), wasKind = kinds.Of(was) });
            }
            string? job = menu?[FirstJobNameId + (uint)b];
            if (moved.Count > 0) context.Spoil("licenseBoards", new { board = b + 1, job, changedCells = moved });
        }
    }

    private static void ShuffleInto(Rng rng, List<Cell> cells)
    {
        var values = cells.Select(c => c.License).ToList();
        rng.Shuffle(values);
        for (int i = 0; i < cells.Count; i++) cells[i].License = values[i];
    }

    public IEnumerable<string> Validate(RandomizerContext context)
    {
        // Same licenses overall, same shapes, and Essentials where it was on every board.
        var problems = new List<string>();
        var allBefore = new List<ushort>();
        var allNow = new List<ushort>();
        for (int n = 1; n <= Boardcount; n++)
        {
            var now = context.Edit(BoardPath(n));
            var was = context.Original.Extract(context.Original.Find(BoardPath(n))!);
            for (int i = 0; i < Size * Size; i++)
            {
                ushort a = BinaryPrimitives.ReadUInt16LittleEndian(was.AsSpan(HeaderSize + i * 2));
                ushort b = BinaryPrimitives.ReadUInt16LittleEndian(now.AsSpan(HeaderSize + i * 2));
                if ((a == 0xFFFF) != (b == 0xFFFF)) problems.Add($"Board {n} changed shape at cell {i}.");
                if ((a == Essentials) != (b == Essentials)) problems.Add($"Board {n} moved Essentials.");
                if (a != 0xFFFF) allBefore.Add(a);
                if (b != 0xFFFF) allNow.Add(b);
            }
        }
        if (!allBefore.Order().SequenceEqual(allNow.Order())) problems.Add("The boards no longer hold the same licenses overall.");
        return problems;
    }
}

/// <summary>
/// What kind of license each license is, for the spoiler. The game has no names for its license types (byte 0x05 only picks
/// a board icon), so kinds come from the data: licenses sharing a name stem form a kind ("White Magick 1".."9" → White
/// Magick; Gambit Slot; Swords). A one-off license is named by what it unlocks (equipment → Weapons, Armor or Accessories
/// by item category), by its type when that is the Quickening or Esper type, as Technicks or Magicks when its
/// value is the ability of the same name (abilities from 158 on are technicks), and otherwise it is an augment (+230 HP, Adrenaline).
/// </summary>
public sealed class LicenseKinds
{
    private const int AbilityNamesTable = 0;
    private readonly Dictionary<ushort, string> _kinds = [];

    public string Of(ushort license) => _kinds.GetValueOrDefault(license, "Other");

    public static LicenseKinds Read(BattlePack pack)
    {
        var result = new LicenseKinds();
        var table = pack.Table(LicenseBoardsFeature.LicenseSection);
        var catalog = ItemCatalog.Instance;
        var licenses = Enumerable.Range(0, table.Count).Select(i =>
        {
            var record = table.Record(i).ToArray();
            string name = pack.Text(LicenseBoardsFeature.LicenseNamesTable, i) ?? "";
            string stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimEnd();
            var items = Enumerable.Range(0, StartingLicenses.ItemSlots)
                .Select(s => BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(StartingLicenses.ItemsOffset + s * 2)))
                .Where(id => id != 0xFFFF).ToList();
            return (Id: (ushort)i, Name: name, Stem: stem, Type: record[LicenseBoardsFeature.TypeOffset], Items: items);
        }).ToList();
        // Abilities: a license whose value is an ability (battle text table 0) of the same name. Technicks follow the
        // magicks; the first technick's ability index is byte 0x04 of the technick table's first record.
        int technickBase = pack.Table(ShopPricesFeature.TechnickSection).Record(0)[0x04];
        bool IsAbility((ushort Id, string Name, string Stem, byte Type, List<ushort> Items) l) =>
            l.Items.Count > 0 && l.Items[0] < 0x1000 && pack.Text(AbilityNamesTable, l.Items[0]) == l.Name;
        var shared = licenses.GroupBy(l => l.Stem).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var l in licenses)
        {
            result._kinds[l.Id] = shared.Contains(l.Stem) && l.Stem.Length > 0 ? l.Stem
                : l.Type == LicenseBoardsFeature.QuickeningType ? "Quickenings"
                : l.Type == LicenseBoardsFeature.EsperType ? "Espers"
                : IsAbility(l) ? (l.Items[0] >= technickBase ? "Technicks" : "Magicks")
                : l.Items.Count > 0 && catalog.Find(l.Items[0])?.Category is ItemCategory.Weapon ? "Weapons"
                : l.Items.Count > 0 && catalog.Find(l.Items[0])?.Category is ItemCategory.Armor ? "Armor"
                : l.Items.Count > 0 && catalog.Find(l.Items[0])?.Category is ItemCategory.Accessory ? "Accessories"
                : "Augments";
        }
        return result;
    }
}
