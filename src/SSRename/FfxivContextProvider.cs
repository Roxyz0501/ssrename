using Advanced_Combat_Tracker;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace SSRename
{
    internal sealed class FfxivContextProvider
    {
        private readonly GameDataService gameData;
        private object repository;
        private MethodInfo getCombatants;
        private MethodInfo getPlayerId;
        private MethodInfo getTerritoryId;
        private MethodInfo getServerTimestamp;
        private string zoneNameFromLog;

        public FfxivContextProvider(GameDataService gameData)
        {
            this.gameData = gameData;
        }

        public void SetZoneName(string zoneName)
        {
            if (!string.IsNullOrWhiteSpace(zoneName))
                zoneNameFromLog = zoneName.Trim();
        }

        public ContextStatus Read(string language)
        {
            try
            {
                if (repository == null && !Connect())
                    return new ContextStatus { Error = Localization.T(language, "ffxiv_plugin_missing") };

                var playerId = Convert.ToUInt32(getPlayerId.Invoke(repository, null));
                var territoryId = Convert.ToUInt32(getTerritoryId.Invoke(repository, null));
                var serverTime = ReadServerTime();
                var context = new ScreenshotContext
                {
                    TerritoryId = territoryId,
                    CapturedAtUtc = serverTime,
                    PlaceName = gameData.GetPlaceName(territoryId),
                    WeatherName = gameData.GetWeatherName(territoryId, serverTime, language),
                    EorzeaTime = GameDataService.GetEorzeaTime(serverTime)
                };

                var combatants = getCombatants.Invoke(repository, null) as IEnumerable;
                if (combatants != null)
                {
                    foreach (var combatant in combatants)
                    {
                        if (combatant == null || ReadUInt(combatant, "ID") != playerId)
                            continue;
                        context.CharacterName = ReadString(combatant, "Name");
                        context.WorldName = ReadString(combatant, "WorldName");
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(context.PlaceName))
                {
                    var actZone = GetActZoneName();
                    context.PlaceName = !string.IsNullOrWhiteSpace(actZone) ? actZone
                        : (!string.IsNullOrWhiteSpace(zoneNameFromLog) ? zoneNameFromLog
                        : (territoryId == 0 ? Localization.T(language, "unknown_place") : "Territory" + territoryId));
                }
                return new ContextStatus
                {
                    Context = context,
                    Connected = playerId != 0,
                    Error = playerId == 0 ? Localization.T(language, "waiting_character") : null
                };
            }
            catch (Exception ex)
            {
                repository = null;
                return new ContextStatus { Error = Localization.T(language, "ffxiv_read_failed", ex.GetBaseException().Message) };
            }
        }

        private bool Connect()
        {
            var plugin = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(item =>
                item.pluginObj != null &&
                ((item.pluginFile != null && item.pluginFile.Name.Equals("FFXIV_ACT_Plugin.dll", StringComparison.OrdinalIgnoreCase)) ||
                 item.pluginObj.GetType().FullName == "FFXIV_ACT_Plugin.FFXIV_ACT_Plugin"));
            if (plugin == null)
                return false;
            var property = plugin.pluginObj.GetType().GetProperty("DataRepository", BindingFlags.Public | BindingFlags.Instance);
            repository = property == null ? null : property.GetValue(plugin.pluginObj, null);
            if (repository == null)
                return false;
            var type = repository.GetType().GetInterfaces()
                .FirstOrDefault(value => value.FullName == "FFXIV_ACT_Plugin.Common.IDataRepository") ?? repository.GetType();
            getCombatants = type.GetMethod("GetCombatantList", Type.EmptyTypes);
            getPlayerId = type.GetMethod("GetCurrentPlayerID", Type.EmptyTypes);
            getTerritoryId = type.GetMethod("GetCurrentTerritoryID", Type.EmptyTypes);
            getServerTimestamp = type.GetMethod("GetServerTimestamp", Type.EmptyTypes);
            if (getCombatants == null || getPlayerId == null || getTerritoryId == null)
            {
                repository = null;
                return false;
            }
            return true;
        }

        private DateTime ReadServerTime()
        {
            try
            {
                if (getServerTimestamp != null)
                {
                    var value = (DateTime)getServerTimestamp.Invoke(repository, null);
                    if (value > new DateTime(2000, 1, 1))
                        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
                }
            }
            catch { }
            return DateTime.UtcNow;
        }

        private string GetActZoneName()
        {
            try
            {
                var property = ActGlobals.oFormActMain.GetType().GetProperty("CurrentZone", BindingFlags.Public | BindingFlags.Instance);
                return property == null ? null : Convert.ToString(property.GetValue(ActGlobals.oFormActMain, null));
            }
            catch { return null; }
        }

        private static uint ReadUInt(object value, string propertyName)
        {
            var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property == null ? 0 : Convert.ToUInt32(property.GetValue(value, null));
        }

        private static string ReadString(object value, string propertyName)
        {
            var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : Convert.ToString(property.GetValue(value, null));
        }
    }
}
