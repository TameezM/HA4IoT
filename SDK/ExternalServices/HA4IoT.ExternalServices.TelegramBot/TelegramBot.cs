﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using HA4IoT.Contracts.Logging;
using HA4IoT.Contracts.PersonalAgent;
using HA4IoT.Contracts.Services;
using HA4IoT.Networking;
using HttpClient = System.Net.Http.HttpClient;

namespace HA4IoT.ExternalServices.TelegramBot
{
    public class TelegramBot : ServiceBase
    {
        private const string BaseUri = "https://api.telegram.org/bot";

        private int _latestUpdateId;
        
        public event EventHandler<TelegramBotMessageReceivedEventArgs> MessageReceived;

        public string AuthenticationToken { get; set; }

        public HashSet<int> Administrators { get; } = new HashSet<int>();
        public HashSet<int> ChatWhitelist { get; } = new HashSet<int>();
        public bool AllowAllClients { get; set; }

        public async Task TrySendMessageToAdministratorsAsync(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            foreach (var chatId in Administrators)
            {
                await TrySendMessageAsync(new TelegramOutboundMessage(chatId, text));
            }
        }

        public async Task<bool> TrySendMessageAsync(TelegramOutboundMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            try
            {
                await SendMessageAsync(message);
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Error while sending Telegram message.");
                return false;
            }
        }

        public async Task SendMessageAsync(TelegramOutboundMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            using (var httpClient = new HttpClient())
            {
                string uri = $"{BaseUri}{AuthenticationToken}/sendMessage";
                StringContent body = ConvertOutboundMessageToJsonMessage(message);
                HttpResponseMessage response = await httpClient.PostAsync(uri, body);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Sending Telegram message failed (StatusCode={response.StatusCode}).");
                }

                Log.Info($"Sent Telegram message '{message.Text}' to chat {message.ChatId}.");
            }
        }

        public void StartWaitForMessages()
        {
            Task.Factory.StartNew(
                async () => await WaitForUpdates(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private async Task WaitForUpdates()
        {
            while (true)
            {
                try
                {
                    await WaitForNextUpdates();
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "Error while waiting for next Telegram updates.");
                }
            }
        }

        private async Task WaitForNextUpdates()
        {
            using (var httpClient = new HttpClient())
            {
                string uri = $"{BaseUri}{AuthenticationToken}/getUpdates?timeout=60&offset={_latestUpdateId + 1}";
                HttpResponseMessage response = await httpClient.GetAsync(uri);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Failed to fetch new updates of TelegramBot (StatusCode={response.StatusCode}).");
                }

                string body = await response.Content.ReadAsStringAsync();
                await ProcessUpdates(body);
            }
        }

        private async Task ProcessUpdates(string body)
        {
            JsonObject response;
            if (!JsonObject.TryParse(body, out response))
            {
                return;
            }

            if (!response.GetNamedBoolean("ok", false))
            {
                return;
            }

            foreach (var updateItem in response.GetNamedArray("result"))
            {
                JsonObject update = updateItem.GetObject();

                _latestUpdateId = (int)update.GetNamedNumber("update_id");
                await ProcessMessage(update.GetNamedObject("message"));
            }
        }

        private async Task ProcessMessage(JsonObject message)
        {
            TelegramInboundMessage inboundMessage = ConvertJsonMessageToInboundMessage(message);

            if (!AllowAllClients && !ChatWhitelist.Contains(inboundMessage.ChatId))
            {
                await TrySendMessageAsync(inboundMessage.CreateResponse("Not authorized!"));

                await
                    TrySendMessageToAdministratorsAsync(
                        $"{Emoji.WarningSign} A none whitelisted client ({inboundMessage.ChatId}) has sent a message: '{inboundMessage.Text}'");
            }
            else
            {
                MessageReceived?.Invoke(this, new TelegramBotMessageReceivedEventArgs(this, inboundMessage));
            }
        }

        private TelegramInboundMessage ConvertJsonMessageToInboundMessage(JsonObject message)
        {
            string text = message.GetNamedString("text");
            DateTime timestamp = UnixTimeStampToDateTime(message.GetNamedNumber("date"));

            JsonObject chat = message.GetNamedObject("chat");
            int chatId = (int)chat.GetNamedNumber("id");

            return new TelegramInboundMessage(timestamp, chatId, text);
        }

        private StringContent ConvertOutboundMessageToJsonMessage(TelegramOutboundMessage message)
        {
            var json = new JsonObject();
            json.SetNamedNumber("chat_id", message.ChatId);
            json.SetNamedString("parse_mode", "HTML");
            json.SetNamedString("text", message.Text);

            return new StringContent(json.Stringify(), Encoding.UTF8, "application/json");
        }

        private DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            var buffer = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return buffer.AddSeconds(unixTimeStamp).ToLocalTime();
        }
    }
}