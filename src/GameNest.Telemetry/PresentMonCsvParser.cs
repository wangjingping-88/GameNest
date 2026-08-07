using System.Globalization;

namespace GameNest.Telemetry;

public sealed record PresentMonFrame(
    string SwapChain,
    double TimestampMilliseconds,
    double? MillisecondsBetweenPresents);

public sealed class PresentMonCsvParser
{
    private int _processIdIndex = -1;
    private int _swapChainIndex = -1;
    private int _timestampIndex = -1;
    private int _millisecondsBetweenPresentsIndex = -1;
    private bool _timestampIsMilliseconds;

    public bool TryRead(string line, int expectedProcessId, out PresentMonFrame frame)
    {
        frame = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var columns = SplitCsv(line);
        if (IsHeader(columns))
        {
            ConfigureHeader(columns);
            return false;
        }

        if (_processIdIndex < 0 || _timestampIndex < 0 ||
            columns.Count <= Math.Max(_processIdIndex, Math.Max(_timestampIndex, _swapChainIndex)))
        {
            return false;
        }

        if (!int.TryParse(columns[_processIdIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId) ||
            processId != expectedProcessId ||
            !double.TryParse(
                columns[_timestampIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var timestamp))
        {
            return false;
        }

        if (!_timestampIsMilliseconds)
        {
            timestamp *= 1000d;
        }

        var swapChain = _swapChainIndex >= 0 && !string.IsNullOrWhiteSpace(columns[_swapChainIndex])
            ? columns[_swapChainIndex]
            : "default";
        double? millisecondsBetweenPresents = null;
        if (_millisecondsBetweenPresentsIndex >= 0 &&
            _millisecondsBetweenPresentsIndex < columns.Count &&
            double.TryParse(
                columns[_millisecondsBetweenPresentsIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedMillisecondsBetweenPresents) &&
            double.IsFinite(parsedMillisecondsBetweenPresents) &&
            parsedMillisecondsBetweenPresents > 0d)
        {
            millisecondsBetweenPresents = parsedMillisecondsBetweenPresents;
        }

        frame = new PresentMonFrame(swapChain, timestamp, millisecondsBetweenPresents);
        return true;
    }

    private static bool IsHeader(IReadOnlyList<string> columns) =>
        columns.Any(static column => column.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) &&
        columns.Any(
            static column =>
                column.Equals("CPUStartTime", StringComparison.OrdinalIgnoreCase) ||
                column.Equals("DisplayedTime", StringComparison.OrdinalIgnoreCase) ||
                column.Equals("QPCTime", StringComparison.OrdinalIgnoreCase) ||
                column.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));

    private void ConfigureHeader(IReadOnlyList<string> columns)
    {
        _processIdIndex = IndexOf(columns, "ProcessID");
        _swapChainIndex = IndexOf(columns, "SwapChainAddress");
        _millisecondsBetweenPresentsIndex = IndexOf(columns, "msBetweenPresents");
        // PresentMon is started with --qpc_time_ms, so QPCTime is the only
        // timestamp that is guaranteed to be an absolute millisecond clock.
        _timestampIndex = IndexOf(columns, "QPCTime");
        _timestampIsMilliseconds = true;
        if (_timestampIndex < 0)
        {
            _timestampIndex = IndexOf(columns, "DisplayedTime");
            _timestampIsMilliseconds = false;
        }
        if (_timestampIndex < 0)
        {
            _timestampIndex = IndexOf(columns, "CPUStartTime");
            _timestampIsMilliseconds = true;
        }
        if (_timestampIndex < 0)
        {
            _timestampIndex = IndexOf(columns, "TimeInSeconds");
            _timestampIsMilliseconds = false;
        }
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> SplitCsv(string line)
    {
        var values = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        values.Add(builder.ToString());
        return values;
    }
}
