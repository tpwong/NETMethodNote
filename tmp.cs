// 在處理 worksheet 之前，只做一次
string[]? sharedStrings = null;
var sstPart = wbPart.SharedStringTablePart;
if (sstPart?.SharedStringTable != null)
{
    sharedStrings = sstPart.SharedStringTable
                           .Select(si => si.InnerText)
                           .ToArray();
}



public static Dictionary<string,string> ReadManyCells(
    WorksheetPart part,
    IEnumerable<string> addressEnumerable,
    string[]? sharedStrings)
{
    var addresses = new HashSet<string>(addressEnumerable, StringComparer.OrdinalIgnoreCase);
    var result = new Dictionary<string, string>(addresses.Count, StringComparer.OrdinalIgnoreCase);
    if (addresses.Count == 0) return result;

    using (var reader = OpenXmlReader.Create(part))
    {
        while (reader.Read())
        {
            if (reader.ElementType != typeof(Cell) || !reader.IsStartElement)
                continue;

            var cell = (Cell)reader.LoadCurrentElement();
            var r = cell.CellReference?.Value;
            if (r == null || !addresses.Contains(r))
                continue;

            var raw = cell.CellValue?.InnerText ?? string.Empty;

            if (cell.DataType == CellValues.SharedString &&
                int.TryParse(raw, out int idx) &&
                sharedStrings != null &&
                (uint)idx < (uint)sharedStrings.Length)
            {
                result[r] = sharedStrings[idx];
            }
            else
            {
                result[r] = raw;
            }

            if (result.Count == addresses.Count)
                break;
        }
    }

    return result;
}