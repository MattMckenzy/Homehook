using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Homehook.Models
{
    public class JellyConversation
    {
        [JsonProperty("requestJson")]
        public RequestJson RequestJson { get; set; }
    }

    public class Handler
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class Order
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }

    public class MediaType
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }

    public class Content
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }

    public class Device
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }

    public class UserName
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("resolved")]
        public string Resolved { get; set; }
    }

    public class Params
    {
        [JsonProperty("Order")]
        public Order Order { get; set; }

        [JsonProperty("MediaType")]
        public MediaType MediaType { get; set; }

        [JsonProperty("Content")]
        public Content Content { get; set; }

        [JsonProperty("Device")]
        public Device Device { get; set; }

        [JsonProperty("UserName")]
        public UserName UserName { get; set; }
    }

    public class Intent
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("query")]
        public string Query { get; set; }
    }

    public class Slots
    {
    }

    public class Next
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class Scene
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slotFillingStatus")]
        public string SlotFillingStatus { get; set; }

        [JsonProperty("slots")]
        public Slots Slots { get; set; }

        [JsonProperty("next")]
        public Next Next { get; set; }
    }

    public class Session
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("typeOverrides")]
        public List<object> TypeOverrides { get; set; }

        [JsonProperty("languageCode")]
        public string LanguageCode { get; set; }
    }

    public class User
    {
        [JsonProperty("locale")]
        public string Locale { get; set; }

        [JsonProperty("params")]
        public Params Params { get; set; }

        [JsonProperty("accountLinkingStatus")]
        public string AccountLinkingStatus { get; set; }

        [JsonProperty("verificationStatus")]
        public string VerificationStatus { get; set; }

        [JsonProperty("packageEntitlements")]
        public List<object> PackageEntitlements { get; set; }

        [JsonProperty("gaiamint")]
        public string Gaiamint { get; set; }

        [JsonProperty("permissions")]
        public List<object> Permissions { get; set; }

        [JsonProperty("lastSeenTime")]
        public DateTime LastSeenTime { get; set; }
    }

    public class Home
    {
        [JsonProperty("params")]
        public Params Params { get; set; }
    }

    public class Device2
    {
        [JsonProperty("capabilities")]
        public List<string> Capabilities { get; set; }
    }

    public class RequestJson
    {
        [JsonProperty("handler")]
        public Handler Handler { get; set; }

        [JsonProperty("intent")]
        public Intent Intent { get; set; }

        [JsonProperty("scene")]
        public Scene Scene { get; set; }

        [JsonProperty("session")]
        public Session Session { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("home")]
        public Home Home { get; set; }

        [JsonProperty("device")]
        public Device Device { get; set; }
    }
}