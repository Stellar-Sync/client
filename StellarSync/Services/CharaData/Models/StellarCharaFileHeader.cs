namespace StellarSync.Services.CharaData.Models;

public record StellarCharaFileHeader(byte Version, StellarCharaFileData CharaFileData)
{
    public static readonly byte CurrentVersion = 1;

    public byte Version { get; set; } = Version;
    public StellarCharaFileData CharaFileData { get; set; } = CharaFileData;
    public string FilePath { get; private set; } = string.Empty;

    public void WriteToStream(BinaryWriter writer)
    {
        writer.Write('M');
        writer.Write('C');
        writer.Write('D');
        writer.Write('F');
        writer.Write(Version);
        var charaFileDataArray = CharaFileData.ToByteArray();
        writer.Write(charaFileDataArray.Length);
        writer.Write(charaFileDataArray);
    }

    public static StellarCharaFileHeader? FromBinaryReader(string path, BinaryReader reader)
    {
        var chars = new string(reader.ReadChars(4));
        if (!string.Equals(chars, "MCDF", StringComparison.Ordinal)) throw new InvalidDataException("Not a Stellar Chara File");

        StellarCharaFileHeader? decoded = null;

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();

            decoded = new(version, StellarCharaFileData.FromByteArray(reader.ReadBytes(dataLength)))
            {
                FilePath = path,
            };
        }

        return decoded;
    }

    public static void AdvanceReaderToData(BinaryReader reader)
    {
        reader.ReadChars(4);
        var version = reader.ReadByte();
        if (version == 1)
        {
            var length = reader.ReadInt32();
            _ = reader.ReadBytes(length);
        }
    }
}