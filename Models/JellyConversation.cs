using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Homehook.Models
{
    public class JellyConversation
    {
        [JsonPropertyName("requestJson")]
        public RequestJson RequestJson { get; set; }
    }

    public class Handler
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Order
    {
        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("resolved")]
        public string Resolved { get; set; }
    }

    public class MediaType
    {
        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("resolved")]
        public string Resolved { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("resolved")]
        public string Resolved { get; set; }
    }

    public class Device
    {
        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("resolved")]
        public string Resolved { get; set; }
    }

    public class UserName
    {
        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("resolved")]
        public string Resolved { get; set; }
    }

    public class Params
    {
        [JsonPropertyName("Order")]
        public Order Order { get; set; }

        [JsonPropertyName("MediaType")]
        public MediaType MediaType { get; set; }

        [JsonPropertyName("Content")]
        public Content Content { get; set; }

        [JsonPropertyName("Device")]
        public Device Device { get; set; }

        [JsonPropertyName("UserName")]
        public UserName UserName { get; set; }
    }

    public class Intent
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("params")]
        public Params Params { get; set; }

        [JsonPropertyName("query")]
        public string Query { get; set; }
    }

    public class Slots
    {
    }

    public class Next
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Scene
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("slotFillingStatus")]
        public string SlotFillingStatus { get; set; }

        [JsonPropertyName("slots")]
        public Slots Slots { get; set; }

        [JsonPropertyName("next")]
        public Next Next { get; set; }
    }

    public class Session
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("params")]
        public Params Params { get; set; }

        [JsonPropertyName("typeOverrides")]
        public List<object> TypeOverrides { get; set; }

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; }
    }

    public class User
    {
        [JsonPropertyName("locale")]
        public string Locale { get; set; }

        [JsonPropertyName("params")]
        public Params Params { get; set; }

        [JsonPropertyName("accountLinkingStatus")]
        public string AccountLinkingStatus { get; set; }

        [JsonPropertyName("verificationStatus")]
        public string VerificationStatus { get; set; }

        [JsonPropertyName("packageEntitlements")]
        public List<object> PackageEntitlements { get; set; }

        [JsonPropertyName("gaiamint")]
        public string Gaiamint { get; set; }

        [JsonPropertyName("permissions")]
        public List<object> Permissions { get; set; }

        [JsonPropertyName("lastSeenTime")]
        public DateTime LastSeenTime { get; set; }
    }

    public class Home
    {
        [JsonPropertyName("params")]
        public Params Params { get; set; }
    }

    public class Device2
    {
        [JsonPropertyName("capabilities")]
        public List<string> Capabilities { get; set; }
    }

    public class RequestJson
    {
        [JsonPropertyName("handler")]
        public Handler Handler { get; set; }

        [JsonPropertyName("intent")]
        public Intent Intent { get; set; }

        [JsonPropertyName("scene")]
        public Scene Scene { get; set; }

        [JsonPropertyName("session")]
        public Session Session { get; set; }

        [JsonPropertyName("user")]
        public User User { get; set; }

        [JsonPropertyName("home")]
        public Home Home { get; set; }

        [JsonPropertyName("device")]
        public Device Device { get; set; }
    }
}