using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using BetterGenshinImpact.Service.Notification.Model;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BetterGenshinImpact.Service.Notifier;

public class DiscordWebhookNotifier : INotifier
{
    private static readonly ILogger<DiscordWebhookNotifier> Logger = App.GetLogger<DiscordWebhookNotifier>();

    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly string? _username;
    private readonly string? _avatarUrl;
    private readonly string _imageFormat;
    private readonly IImageEncoder _imageEncoder;

    public enum ImageEncoderEnum
    {
        Png,
        Jpeg,
        WebP
    }

    public DiscordWebhookNotifier(HttpClient httpClient, string webhookUrl, string username, string avatarUrl, string imageFormat)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl;
        _username = string.IsNullOrWhiteSpace(username) ? null : username;
        _avatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
        _imageFormat = imageFormat.ToLower();
        _imageEncoder = imageFormat switch
        {
            nameof(ImageEncoderEnum.Png) => new PngEncoder(),
            nameof(ImageEncoderEnum.WebP) => new WebpEncoder(),
            _ => new JpegEncoder()
        };
    }

    public string Name { get; set; } = "Discord Webhook";

    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrEmpty(_webhookUrl))
            throw new NotifierException("Discord webhook URL is not set");

        var url = _webhookUrl;
        var fileName = $"screenshot.{_imageFormat}";
        var hasScreenshot = content.Screenshot != null;

        var embed = new DiscordEmbed
        {
            Title = $"{content.Event} | {content.Result}",
            Description = content.Message,
            Footer = new DiscordEmbedFooter { Text = content.Timestamp.ToString() },
            Image = hasScreenshot ? new DiscordEmbedImage { Url = $"attachment://{fileName}" } : null
        };

        var payload = new DiscordPayload
        {
            Username = _username,
            AvatarUrl = _avatarUrl,
            Embeds = new List<DiscordEmbed> { embed }
        };

        HttpContent requestContent;

        if (hasScreenshot)
        {
            var multipart = new MultipartFormDataContent();

            using var ms = new MemoryStream();
            await content.Screenshot.SaveAsync(ms, _imageEncoder);
            var imageContent = new ByteArrayContent(ms.ToArray());
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse($"image/{_imageFormat}");
            multipart.Add(imageContent, "files[0]", fileName);

            var json = JsonContent.Create(payload);
            multipart.Add(json, "payload_json");

            requestContent = multipart;
        }
        else
        {
            requestContent = JsonContent.Create(payload);
        }

        try
        {
            var response = await _httpClient.PostAsync(url, requestContent);
            response.EnsureSuccessStatusCode();
        }
        catch (System.Exception ex)
        {
            Logger.LogDebug("Failed to send message to Discord: {ex}", ex.Message);
            throw new System.Exception("Failed to send message to Discord", ex);
        }
    }

    private class DiscordPayload
    {
        [JsonPropertyName("username")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Username { get; set; }

        [JsonPropertyName("avatar_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("embeds")]
        public List<DiscordEmbed> Embeds { get; set; } = new();
    }

    private class DiscordEmbed
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("image")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiscordEmbedImage? Image { get; set; }

        [JsonPropertyName("footer")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiscordEmbedFooter? Footer { get; set; }
    }

    private class DiscordEmbedImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private class DiscordEmbedFooter
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
