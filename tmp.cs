public static Dictionary<string, string> ReadManyCells(
    WorksheetPart part,
    IEnumerable<string> addressEnumerable,
    WorkbookPart wbPart)
{
    // 用不分大小寫，行為與舊 ReadCell 一致
    var addresses = new HashSet<string>(addressEnumerable, StringComparer.OrdinalIgnoreCase);
    var result = new Dictionary<string, string>(addresses.Count, StringComparer.OrdinalIgnoreCase);

    if (addresses.Count == 0) 
        return result;

    var sst = wbPart.SharedStringTablePart?.SharedStringTable;

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

            // SharedString
            if (cell.DataType != null &&
                cell.DataType == CellValues.SharedString &&
                int.TryParse(raw, out int idx))
            {
                result[r] = sst?.ElementAtOrDefault(idx)?.InnerText ?? raw;
            }
            else
            {
                result[r] = raw;
            }

            // 找齊了所有需要的 Cell，就可以提早結束迴圈
            if (result.Count == addresses.Count)
                break;
        }
    }

    return result;
}