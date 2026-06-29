using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Services
{
    public class ReportingService
    {
        public byte[] ExportToExcel<T>(string sheetName, string[] headers, List<T> data, Func<T, object?[]> rowMapper)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(sheetName);

            // Style headers
            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#5C5CFF");
            headerRow.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            // Write data rows
            for (int r = 0; r < data.Count; r++)
            {
                var values = rowMapper(data[r]);
                for (int c = 0; c < values.Length; c++)
                {
                    var val = values[c];
                    if (val == null)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = string.Empty;
                    }
                    else if (val is decimal decVal)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = decVal;
                        worksheet.Cell(r + 2, c + 1).Style.NumberFormat.Format = "#,##0.00";
                    }
                    else if (val is int intVal)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = intVal;
                    }
                    else if (val is DateTimeOffset dtoVal)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = dtoVal.LocalDateTime;
                        worksheet.Cell(r + 2, c + 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
                    }
                    else if (val is DateTime dtVal)
                    {
                        worksheet.Cell(r + 2, c + 1).Value = dtVal;
                        worksheet.Cell(r + 2, c + 1).Style.DateFormat.Format = "yyyy-MM-dd";
                    }
                    else
                    {
                        worksheet.Cell(r + 2, c + 1).Value = val.ToString();
                    }
                }
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportToCsv<T>(string[] headers, List<T> data, Func<T, object?[]> rowMapper)
        {
            var sb = new StringBuilder();
            
            // Write headers
            sb.AppendLine(string.Join(",", EscapeCsvFields(headers)));

            // Write data rows
            foreach (var item in data)
            {
                var values = rowMapper(item);
                var stringValues = new string[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    var val = values[i];
                    if (val == null)
                    {
                        stringValues[i] = string.Empty;
                    }
                    else if (val is DateTimeOffset dtoVal)
                    {
                        stringValues[i] = dtoVal.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                    }
                    else if (val is DateTime dtVal)
                    {
                        stringValues[i] = dtVal.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        stringValues[i] = val.ToString() ?? string.Empty;
                    }
                }
                sb.AppendLine(string.Join(",", EscapeCsvFields(stringValues)));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private string[] EscapeCsvFields(string[] fields)
        {
            var escaped = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f.Contains(",") || f.Contains("\"") || f.Contains("\n") || f.Contains("\r"))
                {
                    escaped[i] = $"\"{f.Replace("\"", "\"\"")}\"";
                }
                else
                {
                    escaped[i] = f;
                }
            }
            return escaped;
        }
    }
}
