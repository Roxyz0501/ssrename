using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SSRename
{
    internal sealed class GameDataService
    {
        private readonly Dictionary<int, string> placeNames = new Dictionary<int, string>();
        private readonly Dictionary<int, string> weatherNames = new Dictionary<int, string>();
        private readonly Dictionary<int, WeatherRate> weatherRates = new Dictionary<int, WeatherRate>();
        private readonly Dictionary<uint, TerritoryWeather> territories = new Dictionary<uint, TerritoryWeather>();
        private readonly string language;

        public GameDataService(string language = null)
        {
            this.language = language;
            LoadNames("SSRename.Data.PlaceName.csv", placeNames);
            LoadNames("SSRename.Data.Weather.csv", weatherNames);
            LoadWeatherRates();
            LoadTerritories();
        }

        public string GetPlaceName(uint territoryId)
        {
            TerritoryWeather territory;
            return territories.TryGetValue(territoryId, out territory) ? territory.PlaceName : null;
        }

        public string GetWeatherName(uint territoryId, DateTime timestampUtc, string uiLanguage = null)
        {
            TerritoryWeather territory;
            WeatherRate rate;
            if (!territories.TryGetValue(territoryId, out territory) ||
                territory.WeatherRateId <= 0 || !weatherRates.TryGetValue(territory.WeatherRateId, out rate))
                return Localization.T(uiLanguage ?? language, "no_weather");

            var target = CalculateWeatherTarget(timestampUtc);
            var cumulative = 0;
            for (var index = 0; index < rate.Rates.Length; index++)
            {
                cumulative += rate.Rates[index];
                if (target < cumulative)
                {
                    string name;
                    return weatherNames.TryGetValue(rate.WeatherIds[index], out name) && !string.IsNullOrWhiteSpace(name)
                        ? name : Localization.T(uiLanguage ?? language, "unknown_weather");
                }
            }
            return Localization.T(uiLanguage ?? language, "unknown_weather");
        }

        public static string GetEorzeaTime(DateTime timestampUtc)
        {
            var unixSeconds = GetUnixSeconds(timestampUtc);
            var eorzeaSeconds = (long)Math.Floor(unixSeconds * (144d / 7d));
            var secondsInDay = 24L * 60L * 60L;
            var daySeconds = ((eorzeaSeconds % secondsInDay) + secondsInDay) % secondsInDay;
            var hour = daySeconds / 3600;
            var minute = (daySeconds % 3600) / 60;
            return string.Format(CultureInfo.InvariantCulture, "ET{0:00}{1:00}", hour, minute);
        }

        internal static int CalculateWeatherTarget(DateTime timestampUtc)
        {
            var unixSeconds = GetUnixSeconds(timestampUtc);
            var bell = unixSeconds / 175;
            var increment = (bell + 8 - (bell % 8)) % 24;
            var totalDays = unixSeconds / 4200;
            var calcBase = totalDays * 100 + increment;
            var step1 = (calcBase << 11) ^ calcBase;
            var step2 = (step1 >> 8) ^ step1;
            return (int)(step2 % 100);
        }

        private static long GetUnixSeconds(DateTime timestamp)
        {
            var utc = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
            return (long)Math.Floor((utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
        }

        private void LoadNames(string resourceName, IDictionary<int, string> destination)
        {
            foreach (var fields in ReadResource(resourceName))
            {
                int id;
                if (fields.Count > 1 && int.TryParse(fields[0], out id) && id > 0)
                    destination[id] = fields[1];
            }
        }

        private void LoadWeatherRates()
        {
            foreach (var fields in ReadResource("SSRename.Data.WeatherRate.csv"))
            {
                int id;
                if (fields.Count < 17 || !int.TryParse(fields[0], out id))
                    continue;
                var weatherIds = new int[8];
                var rates = new int[8];
                for (var i = 0; i < 8; i++)
                {
                    int.TryParse(fields[i + 1], out weatherIds[i]);
                    int.TryParse(fields[i + 9], out rates[i]);
                }
                weatherRates[id] = new WeatherRate { WeatherIds = weatherIds, Rates = rates };
            }
        }

        private void LoadTerritories()
        {
            foreach (var fields in ReadResource("SSRename.Data.TerritoryType.csv"))
            {
                uint id;
                int regionId, zoneId, placeId, weatherRateId;
                if (fields.Count < 22 || !uint.TryParse(fields[0], out id) || id == 0)
                    continue;
                int.TryParse(fields[8], out regionId);
                int.TryParse(fields[9], out zoneId);
                int.TryParse(fields[10], out placeId);
                int.TryParse(fields[21], out weatherRateId);
                string place;
                if (!placeNames.TryGetValue(placeId, out place) || string.IsNullOrWhiteSpace(place))
                    if (!placeNames.TryGetValue(zoneId, out place) || string.IsNullOrWhiteSpace(place))
                        placeNames.TryGetValue(regionId, out place);
                territories[id] = new TerritoryWeather { PlaceName = place, WeatherRateId = weatherRateId };
            }
        }

        private IEnumerable<IList<string>> ReadResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(Localization.T(language, "data_missing", resourceName));
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    var first = true;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (first) { first = false; continue; }
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return ParseCsvLine(line);
                    }
                }
            }
        }

        internal static IList<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); }
                else value.Append(c);
            }
            result.Add(value.ToString());
            return result;
        }
    }
}
