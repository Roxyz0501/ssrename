using System;

namespace SSRename
{
    internal sealed class ScreenshotContext
    {
        public string CharacterName { get; set; }
        public string WorldName { get; set; }
        public string PlaceName { get; set; }
        public string WeatherName { get; set; }
        public string EorzeaTime { get; set; }
        public uint TerritoryId { get; set; }
        public DateTime CapturedAtUtc { get; set; }

        public ScreenshotContext Clone()
        {
            return (ScreenshotContext)MemberwiseClone();
        }
    }

    internal sealed class ContextStatus
    {
        public ScreenshotContext Context { get; set; }
        public bool Connected { get; set; }
        public string Error { get; set; }
    }

    internal sealed class TerritoryWeather
    {
        public string PlaceName { get; set; }
        public int WeatherRateId { get; set; }
    }

    internal sealed class WeatherRate
    {
        public int[] WeatherIds { get; set; }
        public int[] Rates { get; set; }
    }
}
