using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Web.Http;
using HA4IoT.Contracts;
using HA4IoT.Contracts.Actuators;
using HA4IoT.Contracts.Notifications;
using HA4IoT.Contracts.WeatherStation;
using HA4IoT.Core.Timer;
using HA4IoT.Networking;
using HttpMethod = HA4IoT.Networking.HttpMethod;
using HttpStatusCode = HA4IoT.Networking.HttpStatusCode;

namespace HA4IoT.Hardware.OpenWeatherMapWeatherStation
{
    public class OWMWeatherStation : IWeatherStation
    {
        private readonly INotificationHandler _notificationHandler;
        private readonly Uri _weatherDataSourceUrl;
        private readonly WeatherStationTemperatureSensor _temperature;
        private readonly WeatherStationHumiditySensor _humidity;
        private readonly WeatherStationSituationSensor _situation;

        private DateTime? _lastFetched;
        private TimeSpan _sunrise;
        private TimeSpan _sunset;
        
        public OWMWeatherStation(double lat, double lon, string appId, IHomeAutomationTimer timer, IHttpRequestController httpApiController, INotificationHandler notificationHandler)
        {
            if (timer == null) throw new ArgumentNullException(nameof(timer));
            if (httpApiController == null) throw new ArgumentNullException(nameof(httpApiController));
            if (notificationHandler == null) throw new ArgumentNullException(nameof(notificationHandler));

            _temperature = new WeatherStationTemperatureSensor(new ActuatorId("WeatherStation.Temperature"), httpApiController, notificationHandler);
            TemperatureSensor = _temperature;

            _humidity = new WeatherStationHumiditySensor(new ActuatorId("WeatherStation.Humidity"), httpApiController, notificationHandler);
            HumiditySensor = _humidity;

            _situation = new WeatherStationSituationSensor();
            SituationSensor = _situation;

            _notificationHandler = notificationHandler;
            _weatherDataSourceUrl = new Uri(string.Format("http://api.openweathermap.org/data/2.5/weather?lat={0}&lon={1}&APPID={2}&units=metric", lat, lon, appId));

            httpApiController.Handle(HttpMethod.Get, "weatherStation").Using(HandleApiGet);
            httpApiController.Handle(HttpMethod.Post, "weatherStation").WithRequiredJsonBody().Using(HandleApiPost);

            LoadPersistedValues();
            timer.Every(TimeSpan.FromMinutes(2.5)).Do(Update);
        }

        public Daylight Daylight => new Daylight(_sunrise, _sunset);
        public ITemperatureSensor TemperatureSensor { get; }
        public IHumiditySensor HumiditySensor { get; }
        public IWeatherSituationSensor SituationSensor { get; }

        public JsonObject GetStatus()
        {
            var result = new JsonObject();
            result.SetNamedValue("situation", SituationSensor.GetSituation().ToJsonValue());
            result.SetNamedValue("temperature", TemperatureSensor.GetValue().ToJsonValue());
            result.SetNamedValue("humidity", HumiditySensor.GetValue().ToJsonValue());
            result.SetNamedValue("lastFetched", _lastFetched.HasValue ? _lastFetched.Value.ToJsonValue() : JsonValue.CreateNullValue());
            result.SetNamedValue("sunrise", _sunrise.ToJsonValue());
            result.SetNamedValue("sunset", _sunset.ToJsonValue());

            return result;
        }

        private async void Update()
        {
            try
            {
                JsonObject weatherData = await FetchWeatherData();

                PersistWeatherData(weatherData);
                Update(weatherData);

                _lastFetched = DateTime.Now;
            }
            catch (Exception exception)
            {
                _notificationHandler.Warning("Could not fetch weather information. " + exception.Message);
            }
        }

        private void PersistWeatherData(JsonObject weatherData)
        {
            string filename = Path.Combine(ApplicationData.Current.LocalFolder.Path, "WeatherStationValues.json");
            File.WriteAllText(filename, weatherData.Stringify());
        }

        private void Update(JsonObject data)
        {
            var sys = data.GetNamedObject("sys");
            var main = data.GetNamedObject("main");
            var weather = data.GetNamedArray("weather");

            var sunriseValue = sys.GetNamedNumber("sunrise", 0);
            _sunrise = UnixTimeStampToDateTime(sunriseValue).TimeOfDay;

            var sunsetValue = sys.GetNamedNumber("sunset", 0);
            _sunset = UnixTimeStampToDateTime(sunsetValue).TimeOfDay;

            _situation.SetValue(weather.First().GetObject().GetNamedValue("id"));
            _temperature.SetValue(main.GetNamedNumber("temp", 0));
            _humidity.SetValue(main.GetNamedNumber("humidity", 0));
        }

        private async Task<JsonObject> FetchWeatherData()
        {
            using (var httpClient = new HttpClient())
            using (var result = await httpClient.GetAsync(_weatherDataSourceUrl))
            {
                var jsonContent = await result.Content.ReadAsStringAsync();
                return JsonObject.Parse(jsonContent);
            }
        }

        private DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            var buffer = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return buffer.AddSeconds(unixTimeStamp).ToLocalTime();
        }

        private void HandleApiPost(HttpContext context)
        {
            JsonObject requestData;
            if (JsonObject.TryParse(context.Request.Body, out requestData))
            {
                context.Response.StatusCode = HttpStatusCode.BadRequest;
                return;
            }

            _situation.SetValue(requestData.GetNamedValue("situation"));
            _temperature.SetValue((float)requestData.GetNamedNumber("temperature"));
            _humidity.SetValue((float)requestData.GetNamedNumber("humidity"));
            _sunrise = TimeSpan.Parse(requestData.GetNamedString("sunrise"));
            _sunset = TimeSpan.Parse(requestData.GetNamedString("sunset"));

            _lastFetched = DateTime.Now;
        }

        private void HandleApiGet(HttpContext httpContext)
        {
            httpContext.Response.Body = new JsonBody(GetStatus());
        }

        private void LoadPersistedValues()
        {
            string filename = Path.Combine(ApplicationData.Current.LocalFolder.Path, "WeatherStationValues.json");
            if (!File.Exists(filename))
            {
                return;
            }

            try
            {
                var values = JsonObject.Parse(File.ReadAllText(filename));
                Update(values);
            }
            catch (Exception)
            {
                _notificationHandler.Warning("Unable to load persisted weather station values.");
                File.Delete(filename);
            }
        }
    }
}