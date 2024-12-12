namespace ERP_Billing
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Extensions.Configuration;
    using Npgsql;

    public class ERPMonthlyBill(IConfiguration configuration)
    {
        private const decimal DAYTIME_RATE = 0.15m;
        private const decimal NIGHTTIME_RATE = 0.05m;

        public void LoadDataAndGeneratoMonthlyBill()
        {
            var lookup = LoadCustomerLookup("C:\\Users\\tilieva\\Desktop\\test\\lookup.txt");
            var records = LoadRecords("C:\\Users\\tilieva\\Desktop\\test\\input.txt", out var invalidRecords, out var eRecords);

            SaveInvalidRecords("C:\\Users\\tilieva\\Desktop\\test\\invalid_records.txt", invalidRecords);
            SaveInvalidRecords("C:\\Users\\tilieva\\Desktop\\test\\E_records.txt", eRecords);

            foreach (var customerId in records.Keys)
            {
                if (lookup.TryGetValue(customerId, out var customerName))
                {
                    var (usage, daytimeUsageInTotal, nighttimeUsageInTotal) = CalculateMonthlyUsage(records[customerId]);
                    var totalDaytimeCost = daytimeUsageInTotal * DAYTIME_RATE;
                    var totalNighttimeCost = nighttimeUsageInTotal * NIGHTTIME_RATE;
                    var totalCost = totalDaytimeCost + totalNighttimeCost;
                    GenerateMonthlyBill(customerName, customerId, usage, daytimeUsageInTotal, nighttimeUsageInTotal, totalCost);
                    SaveBillToDb(customerName, customerId, daytimeUsageInTotal, nighttimeUsageInTotal, totalCost);
                }
            }
        }

        private static Dictionary<string, string> LoadCustomerLookup(string path)
        {
            return File.ReadAllLines(path)
                       .Select(line => line.Split(','))
                       .ToDictionary(parts => parts[0], parts => parts[1]);
        }

        private static Dictionary<string, List<string[]>> LoadRecords(string path, out List<string[]> invalidRecords, out List<string[]> eRecords)
        {
            var validRecords = new Dictionary<string, List<string[]>>();
            invalidRecords = [];
            eRecords = [];

            foreach (var line in File.ReadAllLines(path).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var parts = line.Split(',');
                if (ValidateRecord(parts))
                {
                    if (parts[6].Equals("E")) // check if the record is E
                    {
                        eRecords.Add(parts);
                        continue;
                    }

                    var customerId = parts[0];
                    if (!validRecords.ContainsKey(customerId))
                    {
                        validRecords[customerId] = [];
                    }

                    validRecords[customerId].Add(parts);
                }
                else
                {
                    invalidRecords.Add(parts);
                }
            }

            return validRecords;
        }

        private static bool ValidateRecord(string[] record)
        {
            var validQualities = new HashSet<string> { "A", "E" };
            var usage = record.Skip(2).Take(4).All(value => int.TryParse(value, out var result) && result >= 0);
            var quality = record[6] is not null && validQualities.Contains(record[6]);

            return usage && quality;
        }

        private static void SaveInvalidRecords(string path, List<string[]> invalidRecords)
        {
            File.WriteAllLines(path, invalidRecords.Select(parts => string.Join(",", parts)));
        }

        private static (Dictionary<string, List<int>>, int daytimeUsageInTotal, int nighttimeUsageInTotal) CalculateMonthlyUsage(List<string[]> records)
        {
            var result = new Dictionary<string, List<int>>();
            var daytimeUsageInTotal = 0;
            var nighttimeUsageInTotal = 0;

            foreach (var record in records)
            {
                var daytimeUsage = 0;
                var nighttimeUsage = 0;
                for (int i = 2; i <= 5; i++)
                {
                    var usage = int.Parse(record[i]);
                    // var hour = (i - 2) * 6;
                    if (i ==3 || i==4)
                    {
                        daytimeUsage += usage;
                        daytimeUsageInTotal += usage;
                    }
                    else
                    {
                        nighttimeUsage += usage;
                        nighttimeUsageInTotal += usage;
                    }
                }
                var usageList = new List<int> { daytimeUsage, nighttimeUsage };
                result.Add(record[1], usageList);
            }

            return (result, daytimeUsageInTotal, nighttimeUsageInTotal);
        }

        private static void GenerateMonthlyBill(
            string customerName, 
            string customerId, 
            Dictionary<string, List<int>> usage, 
            int daytimeUsageInTotal, 
            int nighttimeUsageInTotal, 
            decimal totalCost)
        {
            StringBuilder sb = new();
            sb.AppendLine($"<b>Monthly Bill for {customerName} (ID: {customerId})</b></br>");
            foreach (var item in usage)
            {
                sb.AppendLine($"Usage on {item.Key}: {item.Value[0]} kWh @ {DAYTIME_RATE} BGN/kWh</br>");
                sb.AppendLine($"Usage on {item.Key}: {item.Value[1]} kWh @ {NIGHTTIME_RATE} BGN/kWh</br>");
            }
            sb.AppendLine($"<b>Daytime Total Usage: {daytimeUsageInTotal} kWh @ {DAYTIME_RATE} BGN/kWh</b></br>");
            sb.AppendLine($"<b>Nighttime Total Usage: {nighttimeUsageInTotal} kWh @ {NIGHTTIME_RATE} BGN/kWh</b></br>");
            sb.AppendLine($"<b>Total Cost: {totalCost} BGN</b></br>");

            ChromePdfRenderer renderer = new();
            PdfDocument pdf = renderer.RenderHtmlAsPdf(sb.ToString());
            pdf.SaveAs($"C:\\Users\\tilieva\\Desktop\\test\\bill_{customerId}.pdf");
        }
        
        private void SaveBillToDb(string customerName, string customerId, int daytimeUsageInTotal, int nighttimeUsageInTotal, decimal totalCost)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            NpgsqlConnection connection = new(connectionString);
            const string insertQuery =
                "INSERT INTO MonthlyBill (customerid, customername, daytimeusageintotal, nighttimeusageintotal, totalcost) " +
                "VALUES (:customerId, :customerName, :daytimeUsageInTotal, :nighttimeUsageInTotal, :totalCost);";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = insertQuery;
            cmd.Parameters.AddWithValue(":customerId", Int32.Parse(customerId));
            cmd.Parameters.AddWithValue(":customerName", customerName);
            cmd.Parameters.AddWithValue(":daytimeUsageInTotal", daytimeUsageInTotal);
            cmd.Parameters.AddWithValue(":nighttimeUsageInTotal", nighttimeUsageInTotal);
            cmd.Parameters.AddWithValue(":totalCost", totalCost);
            connection.Open();
            cmd.ExecuteNonQuery();
            connection.Close();
        }
    }
}