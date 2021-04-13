# Homehook

Simple API meant to handle Google Assistant webhooks and reroute them to Home Assistant.

 ## Description

This application will help configure and serve incoming and outgoing webhooks to help you route Google Assistant spoken or text commands to your Google Cast devices.

Please see swagger at homehook/swagger page on deployed API to see available hooks, how to call them and try them out.

Currently available hooks:
* [Jellyfin Simple Phrase](#-jellyfin-simple-phrase) - Will parse a simple search term, search for media links on a configured Jellyfin server, create a Jellyfin session, and play them on the selected or default Cast device.
* [Jellyfin Conversation Phrase](#-jellyfin-conversation-phrase) - Will receive a conversation intent from Google Actions, search for media links on a configured Jellyfin server, create a Jellyfin session, and play them on the selected or default Cast device.

Configuration is done via appsettings.json found at root. [See the configuration section below.](#-configuration)

[A container is also available at Docker Hub.](https://hub.docker.com/repository/docker/mattmckenzy/homehook)

## Usage

### Jellyfin Simple Phrase
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

### Jellyfin Conversation Phrase
Google Conversation Actions can help build some very powerful phrase parsing intents that can be used to pre-parse search terms before sending them to Homehook.

This endpoint expects to receive a Google Conversation Action webhook POST to homehook/jelly/conversation (including apiKey query parameter). The following Action parameter names match the phrase terms defined in Homehook:
1. Order
2. Content - i.e. the search term.
3. MediaType
4. Device
5. UserName 

[Please read the Google Actions documentation for information on configuring its advanced phrase parsing features.](https://console.actions.google.com/)

### Home Assistant

I like to use Home Assistant to interact with Homehook through automations by using the RESTful Command integration.

Here's an example of the REST command and an automation configuration:

#### Service - REST command
```yaml
rest_command:
  homehook_jelly:
    url: "http://homehook/jelly/simple?apiKey=be3366f6711e46eea8998770547ccc27"
    method: post
    payload: '{ "content":"{{ search_term }}" }'
    content_type: "application/json"
```

#### Automation example - call REST command on NFC tag scan
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

## Configuration

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
UserMappings:0:Spoken | | Any spoken names or nicknames to represent the user (comma delimited supported).
Services:HomehookApp:Token | | Authentication token used for the SignalR connection between Homehook's API and app, necessary for use of HomehookApp. Can be anything you generate and use on both sides.
Services:Gotify:ServiceUri | | Gotify's service uri.
Services:Gotify:Header | X-Gotify-Key | Gotify's authentication header to use (the default is typically correct).
Services:Gotify:AccessToken | | Gotify's authentication token.
Services:Gotify:Priority | 4 | The minimum level of log to post to Gotify (1: Debug; 2: Information; 3: Warning; 4: Error; 5: Off;).
Services:Jellyfin:ServiceUri | | Jellyfin's service uri.
Services:Jellyfin:Header | X-Emby-Authorization | Jellyfin's authentication header name to use (the default is typically correct).
Services:Jellyfin:HeaderValue | MediaBrowser Client=\"Homehook\",<br />Device=\"$Device\", DeviceId=\"$DeviceId\",<br /> Version=\"1.0.0\", Token=\"{0}\" | Jellyfin's header value to use (the default is typically correct).
Services:Jellyfin:HeaderAuthValue | MediaBrowser Client=\"Homehook\",<br />Device=\"$Device\", DeviceId=\"$DeviceId\",<br /> Version=\"1.0.0\"" | Jellyfin's authentication header value to use (the default is typically correct).
Services:Jellyfin:Credentials |  | A dynamic list of Jellyfin credentials used to authenticate users and provide session progress. (i.e. "Geoff":"1234", "George":"4567").
Services:Jellyfin:AccessToken | | Jellyfin's authentication token, if using static or API token.
Services:Jellyfin:DefaultUser | | The default user to use if the search term doesn't specify one.
Services:Jellyfin:DefaultDevice | | The default device to use if the search term doesn't specify one.
Services:Jellyfin:DefaultOrder | Ordered | The default media item order to use if the search term doesn't specify one (Continue, Shuffle, Oldest, Newest, Shortest or Longest).
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
Services:Google:Token | | Authentication token used to post to Homehook, can be anything you generate and use on both sides.
Services:Google:ApplicationId | C8030EA3 | The Google Cast Application Id to use. Replace this if you wish to use your own styled application instead of Homehook's.
Services:Language:UserPrepositions | as,from | List of available prepositions to identify a user in a search term.
Services:Language:DevicePrepositions | on,to | List of available prepositions to identify a device in a search term.
Services:Language:WordMappings | | A dynamic list of key words, with values of comma delimited words. Used to map commonly misheard spoken words (i.e. Services:Language:WordMappings:Geoff = Jeff,Geoffry,Jeffry).

# Homehook App

A web application that offers a real-time hub to all Google cast devices in home.

## Description

The Homehook web app will offer full control over all in-home Google cast devices through it's main (and currently only) page. You can control playback, media speed, repeat mode, queue items (move, add and remove) and volume. Whatever features the google API permit with the currently playing media should be available!

The cast hub page also gives an easy way to launch a new Jellyfin media queue.

Configuration is done via appsettings.json found at root. [See the configuration section below.](#-configuration)

[A container is also available at Docker Hub.](https://hub.docker.com/repository/docker/mattmckenzy/homehookapp)

## Usage

[Homehook cast hub page](Resources/CastHub.png)
The cast hub page will offer full control over ever cast device in-home while offering a quick and easy way to see each's status. The refresh button in the top right will reload all cast devices available on Homehook. Use this if any of you've changed your available devices or if any of them don't appear.

[Homehook cast hub device controls](Resources/DeviceControls.png)
Each device has full media controls (to the extent allowed by the sender application). The image shows all four popout menus used to control playback speed, queued items, repeat mode and volume. The queue controls offer a quick way to change current media, add (from Jellyfin search term) or remove items or move items up or down.

[Homehook cast hub launch Jellyfin queue](Resources/LaunchJellyfin.png)
You can use the media type icon show in the image here to launch a new Jellyfin queue on any available device! Simply type a jellyfin search term (including order and user keywords) to launch a new queue on the selected device.

## Configuration

Configuring the web app is done either the appsettings.json found at root, or with associated environment variables (great for docker). Here's a list and small description of available configuration variables:

Variable | Default | Description
---|---|---
Logging:LogLevel:Default | Debug | Default .NET logging level.
Logging:LogLevel:Microsoft | Warning | Default .NET Microsoft logging level.
Logging:LogLevel:Microsoft.Hosting.Lifetime | Information |  Default .NET Microsoft Hosting logging level.
AllowedHosts | * | Allowed Hosts.
Services:Homehook:ServiceUri | | Homehook's service uri.
Services:Homehook:AccessToken | | Authentication token used for the SignalR connection between Homehook's API and app, necessary for use of HomehookApp. Can be anything you generate and use on both sides.

# Credits

Big thanks to everyone at the Home Assistant team for their great work!

# Donate

If you appreciate my work and feel like helping me realize other projects, you can donate at <a href="https://paypal.me/MattMckenzy">https://paypal.me/MattMckenzy</a>!
