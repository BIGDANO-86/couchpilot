using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace CouchPilot
{
    /// <summary>
    /// Fire-and-forget MQTT publishing, as an alternative to webhooks for people
    /// whose Home Assistant already speaks MQTT.
    ///
    /// Deliberately connects per publish rather than holding a session open: it
    /// publishes a handful of messages a day, and a long-lived connection would
    /// mean reconnect handling, backoff and a socket kept alive across sleep for
    /// no real benefit.
    /// </summary>
    internal static class Mqtt
    {
        public static async Task<bool> Publish(MqttSettings s, string topic, string payload)
        {
            if (s == null || !s.Enabled) return false;
            if (string.IsNullOrWhiteSpace(s.Host) || string.IsNullOrWhiteSpace(topic)) return false;

            IMqttClient client = null;
            try
            {
                var factory = new MqttFactory();
                client = factory.CreateMqttClient();

                var builder = new MqttClientOptionsBuilder()
                    .WithTcpServer(s.Host, s.Port <= 0 ? 1883 : s.Port)
                    .WithClientId(string.IsNullOrWhiteSpace(s.ClientId)
                        ? "couchpilot-" + Guid.NewGuid().ToString("N").Substring(0, 6)
                        : s.ClientId)
                    .WithCleanSession(true)
                    .WithTimeout(TimeSpan.FromSeconds(6));

                if (!string.IsNullOrWhiteSpace(s.Username))
                    builder = builder.WithCredentials(s.Username, s.Password ?? "");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(builder.Build(), cts.Token).ConfigureAwait(false);

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload ?? ""))
                    .WithRetainFlag(s.Retain)
                    .Build();

                await client.PublishAsync(message, cts.Token).ConfigureAwait(false);
                await client.DisconnectAsync().ConfigureAwait(false);

                Log.Write($"     mqtt published to {topic}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"     mqtt publish to {topic} failed: " + ex.Message);
                return false;
            }
            finally
            {
                try { client?.Dispose(); } catch { }
            }
        }

        public static bool Test(MqttSettings s, string topic, string payload)
        {
            try { return Publish(s, topic, payload).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                Log.Write("mqtt test failed: " + ex.Message);
                return false;
            }
        }
    }
}
