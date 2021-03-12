# Homehook

Simple API meant to handle Google Assistant webhooks and reroute them to Home Assistant.

# Description

This application will help configure and serve incoming and outgoing webhooks to help you route Google Assistant spoken or text commands to Home Assistant.

Please see swagger at homehook/swagger page on deployed API to see available hooks, how to call them and try them out.

Currently available hooks:
* [Jellyfin Simple Phrase](#-jellyfin-simple-phrase) - Will parse a simple search term, search for media links on a configured Jellyfin server, and post them to a Home Assistant server for media_player playback.
* [Jellyfin Conversation Phrase](#-jellyfin-conversation-phrase) - Will receive a conversation intent from Google Actions, search for media links on a configured Jellyfin server, and post them to a Home Assistant server for media_player playback.

Configuration is done via appsettings.json found at root. [See the configuration section below.](#-configuration)

[A container is also available at Docker Hub.](https://hub.docker.com/repository/docker/mattmckenzy/homehook)

# Usage

## Jellyfin Simple Phrase
Once configured appropriately, Homehook can receive POST requests at homehook/jelly/simple to receive a Jellyfin search term, parse it, search for media items, and post them to Home Assistant.
Phrase terms are parsed in the following order:
1. Order
2. Search Term
3. Media Type
4. Device
5. User

i.e.: [random] [chrono cross] [songs] [on basement] [as matt]

The incoming webhook can come from any source, such as IFTTT's simple phrase with text ingredient trigger with a webhook action, but you can also simply call the endpoint from anywhere. Here's a CURL example:

```bash
curl -X POST "http://homehook/jelly/simple?apiKey=69141f00c5fb4a4a93a1eb9e1a74aed7" -H  "accept: */*" -H  "Content-Type: application/json" -d "{\"content\":\"random chrono cross songs on basement\"}"
```

## Jellyfin Conversation Phrase
Google Conversation Actions can help build some very powerful phrase parsing intents that can be used to pre-parse search terms before sending them to Homehook.

This endpoint expects to receive a Google Conversation Action webhook POST to homehook/jelly/conversation (including apiKey query parameter). The following Action parameter names match the phrase terms defined in Homehook:
1. Order
2. Content - i.e. the search term.
3. MediaType
4. Device
5. UserName 

[Please read the Google Actions documentation for information on configuring its advanced phrase parsing features.](https://console.actions.google.com/)

## Home Assistant

Home assistant will receive the media items via direct REST API call. The only thing necessary to permit this is to add the following line to your configuration.yaml file:

### configuration.yaml
```yaml
api:
```

You can also set up a REST service call, using the RESTful Command integration, that you can then use to statically play media:

### Service - REST command
```yaml
rest_command:
  homehook_jelly:
    url: "http://homehook/jelly/simple?apiKey=be3366f6711e46eea8998770547ccc27"
    method: post
    payload: '{ "content":"{{ search_term }}" }'
    content_type: "application/json"
```

### Automation example - call REST command on NFC tag scan
```yaml
  alias: Tag chrono cross is scanned
  description: ''
  trigger:
  - platform: tag
    tag_id: 01d5b0ab-2e5e-4cb7-80b4-6e82bc856029
  condition: []
  action:
  - service: rest_command.homehook_jelly
    data:
      search_term: random chrono cross songs on basement
  - service: media_player.volume_set
    data:
      volume_level: 0.5
    entity_id: media_player.basement
  mode: single
```

# Configuration

Configuring the API is done either the appsettings.json found at root, or with associated environment variables (great for docker). Here's a list and small description of available configuration variables:

Variable | Default | Description|
---|---|---
Logging:LogLevel:Default | Debug | Default .NET logging level.
Logging:LogLevel:Microsoft | Warning | Default .NET Microsoft logging level.
Logging:LogLevel:Microsoft.Hosting.Lifetime | Information |  Default .NET Microsoft Hosting logging level.
AllowedHosts | * | Allowed Hosts.
UserMappings | | Array of users to define mappings between supported services.
UserMappings:0:Jellyfin | | A user's Jellyfin username.
UserMappings:0:HomeAssistant | | A user's HomeAssistant username.
UserMappings:0:Google | | A user's Google username (i.e. e-mail address).
UserMappings:0:Spoken |  | Any spoken names or nicknames to represent the user (comma delimited supported).
Services:Gotify:ServiceUri | | Gotify's service uri.
Services:Gotify:Header | X-Gotify-Key | Gotify's authentication header to use (the default is typically correct).
Services:Gotify:AccessToken | | Gotify's authentication token.
Services:Gotify:Priority | 4 | The minimum level of log to post to Gotify (1: Debug; 2: Information; 3: Warning; 4: Error; 5: Off;).
Services:Jellyfin:ServiceUri | | Jellyfin's service uri.
Services:Jellyfin:Header | X-Emby-Token | Jellyfin's authentication header to use (the default is typically correct).
Services:Jellyfin:AccessToken | | Jellyfin's authentication token.
Services:Jellyfin:DefaultUser | | The default user to use if the search term doesn't specify one.
Services:Jellyfin:DefaultDevice | | The default device to use if the search term doesn't specify one.
Services:Jellyfin:DefaultOrder | Newest | The default media item order to use if the search term doesn't specify one (Continue, Shuffle, Oldest, Newest, Shortest or Longest).
Services:Jellyfin:DefaultMediaType | All | The default media item type to use if the search term doesn't specify one (All, Audio, Video or Photo).
Services:Jellyfin:OrderTerms:Continue | Continue,Resume | Alternative terms that can be used to order for resumable media items.
Services:Jellyfin:OrderTerms:Shuffle | Shuffle,Random,Any | Alternative terms that can be used to shuffle media items.
Services:Jellyfin:OrderTerms:Ordered | Ordered,Order,Sequential | Alternative terms that can be used to order media items by episode or track number.
Services:Jellyfin:OrderTerms:Oldest | Oldest,First | Alternative terms that can be used to order media items by oldest created date.
Services:Jellyfin:OrderTerms:Newest | Last,Latest,Newest,Recent | Alternative terms that can be used to order media items by newest created date.
Services:Jellyfin:OrderTerms:Shortest | Shortest,Quickest,Fastest | Alternative terms that can be used to order media items by lowest runtime.
Services:Jellyfin:OrderTerms:Longest | Longest,Slowest | Alternative terms that can be used to order media items by lomgest runtime.
Services:Jellyfin:MediaTypeTerms:Audio | Song,Songs,Music,Track,<br />Tracks,Audio | Alternative terms that can be used to specify audio media items.
Services:Jellyfin:MediaTypeTerms:Video | Video,Videos,Movies,Movie,<br />Show,Shows,Episode,Episodes | Alternative terms that can be used to specify video media items.
Services:Jellyfin:MediaTypeTerms:Photo | Photo,Photos,Pictures,Picture | Alternative terms that can be used to specify photo media items.
Services:Jellyfin:MaximumQueueSize | 100 | Maximum media item queue size to post to Home Assistant.
Services:IFTTT:Token | | Authentication token used to post to Homehook, can be anything you generate and use on both sides.
Services:HomeAssistant:ServiceUri | | Home Assistant's service uri, can be anything you generate and use on both sides.
Services:HomeAssistant:Header | Authorization | Jellyfin's authentication header to use (the default is typically correct).
Services:HomeAssistant:AccessToken | | Home Assistant's authentication token. Make sure to add "Bearer " before the token generated in your user profile.
Services:HomeAssistant:Token | | Authentication token used to post to Homehook.
Services:HomeAssistant:JelllyDevices | | The list of media player devices available. Used during phrase parsing.
Services:Language:UserPrepositions | as,from | List of available prepositions to identify a user in a search term.
Services:Language:DevicePrepositions | on,to | List of available prepositions to identify a device in a search term.
Services:Language:WordMappings | | A dynamic list of key words, with values of comma delimited words. Used to map commonly misheard spoken words (i.e. Services:Language:WordMappings:Geoff = Jeff,Geoffry,Jeffry).

# Credits

Big thanks to everyone at the Home Assistant team for their great work!

# Donate

If you appreciate my work and feel like helping me realize other projects, you can donate at <a href="https://paypal.me/MattMckenzy">https://paypal.me/MattMckenzy</a>!
