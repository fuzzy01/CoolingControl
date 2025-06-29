namespace CoolingControl;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class CSVLogger
{
    private readonly ConfigHelper _config;
    private readonly string _filePath;
    private readonly StreamWriter _streamWriter;
    private bool _isHeaderWritten;

    public CSVLogger(ConfigHelper config)
    {
        _config = config;
        _filePath = "logs/cooling_control_log.csv";

        _streamWriter = new StreamWriter(_filePath, append: true);
        _streamWriter.AutoFlush = true;
        _isHeaderWritten = false;
    }

    public void LogData(Dictionary<string, float?> sensorData, Dictionary<string, float> controlData)
    {
        // Prepare CSV line
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();

        if (!_isHeaderWritten)
        {
            // Write headers: Timestamp + dictionary keys
            sb.Append("Timestamp");
            if (sensorData.Count > 0)
            {
                sb.Append(',');
                sb.Append(string.Join(",", sensorData.Keys.Select(k => k)));
            }
            if (controlData.Count > 0)
            {
                sb.Append(',');
                sb.AppendLine(string.Join(",", controlData.Keys.Select(k => k)));
            }
            _streamWriter.Write(sb.ToString());
            _isHeaderWritten = true;
            sb.Clear();
        }

        // Write data: Timestamp + dictionary values
        sb.Append($"{timestamp}");
        if (sensorData.Count > 0)
        {
            sb.Append(',');
            sb.Append(string.Join(",", sensorData.Values.Select(v => v.HasValue ? v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")));
        }
        if (controlData.Count > 0)
        {
            sb.Append(',');
            sb.AppendLine(string.Join(",", controlData.Values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        // Append to file
        _streamWriter.Write(sb.ToString());
    }
}