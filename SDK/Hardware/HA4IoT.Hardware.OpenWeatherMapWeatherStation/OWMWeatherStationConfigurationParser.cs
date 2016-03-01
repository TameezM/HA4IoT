﻿using System;
using System.IO;
using Windows.Data.Json;
using Windows.Storage;
using HA4IoT.Contracts.Logging;

namespace HA4IoT.Hardware.OpenWeatherMapWeatherStation
{
    public class OWMWeatherStationConfigurationParser
    {
        private readonly ILogger _logger;

        public OWMWeatherStationConfigurationParser(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _logger = logger;
        }

        public Uri GetUri()
        {
            double latitude = 0;
            double longitude = 0;
            string appId = null;

            JsonObject configuration;
            if (TryGetConfiguration(out configuration))
            {
                latitude = configuration.GetNamedNumber("lat", 0);
                longitude = configuration.GetNamedNumber("lon", 0);
                appId = configuration.GetNamedString("appID", string.Empty);
            }
            
            if (latitude == 0 || longitude == 0)
            {
                _logger.Warning("OWM weather station coordinates invalid.");
            }

            return new Uri($"http://api.openweathermap.org/data/2.5/weather?lat={latitude}&lon={longitude}&APPID={appId}&units=metric");
        }

        private bool TryGetConfiguration(out JsonObject configuration)
        {
            configuration = null;

            string filename = Path.Combine(ApplicationData.Current.LocalFolder.Path, "WeatherStationConfiguration.json");
            if (!File.Exists(filename))
            {
                _logger.Warning($"OWM weather station configuration ({filename}) not found.");
                return false;
            }

            try
            {
                configuration = JsonObject.Parse(File.ReadAllText(filename));
                return true;
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "Unable to parse OWM weather station configuration.");
            }

            return false;
        }
    }
}
