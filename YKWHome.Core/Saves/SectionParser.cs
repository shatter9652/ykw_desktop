namespace YKWHome.Core.Saves;

/// <summary>
/// Section tree node for YW1-3/YWB save files.
/// Mirrors the Python SectionParser from section_parser.py.
/// </summary>
public class SaveSection
{
    public int SectionType { get; set; }
    public int Offset { get; set; }
    public int Size { get; set; }
    public byte[] Data { get; set; } = [];
    public int Hi2Open { get; set; }
    public int Hi2Close { get; set; }

    public override string ToString() =>
        $"Section(0x{SectionType:X2}, offset=0x{Offset:X}, size=0x{Size:X})";
}

public class SectionNode
{
    public int SectionType { get; set; }
    public int BasePos { get; set; }
    public int Size { get; set; }
    public int Hi2Open { get; set; }
    public int Hi2Close { get; set; }
    public bool IsContainer { get; set; }
    public int PayloadOffset { get; set; }
    public byte[] Payload { get; set; } = [];
    public List<SectionNode> Children { get; set; } = [];
}

/// <summary>
/// Parse YW section tree from decrypted save data.
/// Port of section_parser.py.
/// </summary>
public class SectionParser
{
    private const ushort MagicOpen = 0xFFFE;
    private const ushort MagicClose = 0xFEFF;

    private readonly byte[] _data;
    private readonly int _baseOffset;

    public Dictionary<int, SaveSection> Sections { get; } = [];
    public List<SectionNode> Nodes { get; } = [];
    public SectionNode? Root { get; set; }

    public SectionParser(byte[] treeData, int startOffset = 0)
    {
        _data = treeData;
        _baseOffset = startOffset;
        Parse();
    }

    private void Parse()
    {
        if (_data.Length < 8)
            throw new InvalidDataException("Section tree too small");

        uint sizeType = BitConverter.ToUInt32(_data, 4);
        int rootType = (int)(sizeType & 0xFF);
        Root = ParseNode(0, _data.Length, rootType);
    }

    private SectionNode? ParseNode(int pos, int end, int parentType = 0)
    {
        if (pos + 8 > end) return null;

        uint openWord = BitConverter.ToUInt32(_data, pos);
        if ((ushort)(openWord & 0xFFFF) != MagicOpen) return null;

        uint sizeTypeWord = BitConverter.ToUInt32(_data, pos + 4);
        int sectionType = (int)(sizeTypeWord & 0xFF);
        int size = (int)(sizeTypeWord >> 8);

        int hi2Open = (int)((openWord >> 16) & 0xFFFF);
        int payloadStart = pos + 8;
        int payloadEnd = Math.Min(payloadStart + size, end);

        if (payloadStart + 2 > end) return null;

        ushort peek = BitConverter.ToUInt16(_data, payloadStart);

        var node = new SectionNode
        {
            SectionType = sectionType,
            BasePos = pos,
            Size = size,
            Hi2Open = hi2Open,
            Hi2Close = hi2Open,
            IsContainer = false,
            PayloadOffset = payloadStart,
        };

        if (peek == MagicClose)
        {
            // Empty container
            node.IsContainer = true;
            node.Size = 0;
            if (payloadStart + 4 <= payloadEnd)
            {
                uint closeWord = BitConverter.ToUInt32(_data, payloadStart);
                node.Hi2Close = (int)((closeWord >> 16) & 0xFFFF);
            }
            Nodes.Add(node);
            return node;
        }

        if (peek == MagicOpen)
        {
            // Container with children
            node.IsContainer = true;
            int childPos = payloadStart;
            int prevPos = -1;
            while (childPos + 8 <= payloadEnd && childPos > prevPos)
            {
                prevPos = childPos;
                var child = ParseNode(childPos, payloadEnd);
                if (child == null) break;
                node.Children.Add(child);
                Nodes.Add(child);
                childPos = child.BasePos + 8 + child.Size + 4; // skip open+size+payload+close
            }
            Nodes.Add(node);
            return node;
        }

        // Leaf node
        node.Payload = new byte[size];
        Array.Copy(_data, payloadStart, node.Payload, 0, size);
        Nodes.Add(node);

        // Store section
        Sections[sectionType] = new SaveSection
        {
            SectionType = sectionType,
            Offset = _baseOffset + payloadStart,
            Size = size,
            Data = node.Payload,
            Hi2Open = hi2Open,
        };

        return node;
    }
}
