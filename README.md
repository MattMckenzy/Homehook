# Homehook

Simple API meant to handle Google Assistant webhooks and reroute them to Home Assistant.

# Description

This application will help configure and serve incoming and outgoing webhooks to help you route Google Assistant spoken or text commands to Home Assistant.

Please see swagger at homehook/swagger page on deployed API to see available hooks, how to call them and try them out.

Currently available hooks:
* [Jellyfin Simple Phrase](#-jellyfin-simple-phrase) - Will parse a simple search term, search for media links on a configured Jellyfin server, and post them to a Home Assistant server for media_player playback.

Configuration is done via appsettings.json found at root. [See the configuration section below.](#-configuration)

[A container is also available at Docker Hub.](https://hub.docker.com/repository/docker/mattmckenzy/homehook)

# Usage

## Jellyfin Simple Phrase
Once configured appropriately, Homehook can receive POST requests at homehook/jelly to receive a Jellyfin search term, parse it, search for media items, and post them to Home Assistant.
Phrase terms are parsed in the following order:
1. Order
2. Search Term
3. Media Type
4. Device
5. User

i.e.: [random] [chrono cross] [songs] [on basement] [as matt]

The incoming webhook can come from any source, such as IFTTT's simple phrase with text ingredient trigger with a webhook action, but you can also simply call the endpoint from anywhere. Here's a CURL example:

```bash
curl -X POST "http://homehook/Jelly?apiKey=69141f00c5fb4a4a93a1eb9e1a74aed7" -H  "accept: */*" -H  "Content-Type: application/json" -d "{\"content\":\"random chrono cross songs on basement\"}"
```

## Home Assistant

Once Home Assistant receives the webhook calls with the items as a JSON payload, you can process them with the following automation and script:

### Automation - receiving Homehook webhooks
```yaml
  alias: Jelly
  description: Play incoming jellyfin media items.
  trigger:
  - platform: webhook
    webhook_id: 69141f00c5fb4a4a93a1eb9e1a74aed7
  condition: []
  action:
  - service: script.turn_on
    entity_id: script.playjellyitem
    data:
      variables:
        content: '{{ trigger.json }}'
  mode: queued
  max: 100
```

### Script - playing a media item
```yaml
playjellyitem:
  alias: PlayJellyItem
  sequence:
    - service: media_player.play_media
      data_template:
        entity_id: media_player.{{ content.Device }}
        media_content_type: "{{ content.MediaType }}"
        media_content_id: "{{ content.Url }}"
        extra:          
          enqueue: >
            {% if content.Index != 0 %} true 
            {% endif %}           
          metadata:
            title: "{{ content.Title }}"            
            images: >
              - url: {% if content.ImageUrl is defined %} "{{ content.ImageUrl }}"            
              {% endif %}
            metadataType: >
              {% if content.JellyVideoMetadata is defined %} 2 
              {% elif content.JellyAudioMetadata is defined %} 3
              {% elif content.JellyPhotoMetadata is defined %} 4
              {% endif %}
            subtitle: >
              {% if content.JellyVideoMetadata.Description is defined %} "{{ content.JellyVideoMetadata.Description }}"               
              {% endif %}            
            seriesTitle: >
              {% if content.JellyVideoMetadata.SeriesName is defined %} "{{ content.JellyVideoMetadata.SeriesName }}"
              {% endif %}            
            season: >
              {% if content.JellyVideoMetadata.Season is defined %} "{{ content.JellyVideoMetadata.Season }}"
              {% endif %}
            episode: >
              {% if content.JellyVideoMetadata.Episode is defined %} "{{ content.JellyVideoMetadata.Episode }}"
              {% endif %}
            originalAirDate: >
              {% if content.JellyVideoMetadata.PremiereDate is defined %} "{{ content.JellyVideoMetadata.PremiereDate }}" 
              {% endif %}
            albumName: >              
              {% if content.JellyAudioMetadata.Album is defined %} "{{ content.JellyAudioMetadata.Album }}"
              {% endif %}
            albumArtist: >               
              {% if content.JellyAudioMetadata.AlbumArtist is defined %} "{{ content.JellyAudioMetadata.AlbumArtist }}"
              {% endif %}
            trackNumber: >             
              {% if content.JellyAudioMetadata.Song is defined %} "{{ content.JellyAudioMetadata.Song }}"
              {% endif %}
            discNumber: > 
              {% if content.JellyAudioMetadata.Disc is defined %} "{{ content.JellyAudioMetadata.Disc }}"
              {% endif %}            
            releaseDate: >
              {% if content.JellyAudioMetadata.ProductionYear is defined %} "{{ content.JellyAudioMetadata.ProductionYear }}"
              {% endif %}            
            creationDateTime: >
              {% if content.JellyPhotoMetadata.DateCreated is defined %} "{{ content.JellyPhotoMetadata.DateCreated }}"
              {% endif %}
  mode: queued
  icon: 'mdi:video'
```


You can also set up a REST service call, using the RESTful Command integration, that you can use to statically play media:

### Service - REST command
```yaml
rest_command:
  homehook_jelly:
    url: "http://homehook/jelly?apiKey=be3366f6711e46eea8998770547ccc27"
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

Variable | Default | Description
---------|---------|------------
Logging:LogLevel:Default | Debug | Default .NET logging level.
Logging:LogLevel:Microsoft | Warning | Default .NET Microsoft logging level.
Logging:LogLevel:Microsoft.Hosting.Lifetime | Information |  Default .NET Microsoft Hosting logging level.
AllowedHosts | * | Allowed Hosts.
UserMappings | | Array of users to define mappings between supported services.
UserMappings:0:Jellyfin | | A user's Jellyfin username.
UserMappings:0:HomeAssistant | | A user's HomeAssistant username.
UserMappings:0:Google | | A user's Google username (i.e. e-mail address).
UserMappings:0:Spoken |  | Any spoken names or nicknames to represent the user (comma delimited supported).
Services:Gotify:Header | X-Gotify-Key | Gotify's authentication header to use (the default is typically correct).
Services:Gotify:ServiceUri | | Gotify's service uri.
Services:Gotify:Token | | Gotify's authentication token.
Services:Gotify:Priority | 4 | The minimum level of log to post to Gotify (1: Debug; 2: Information; 3: Warning; 4: Error; 5: Off;).
Services:Jellyfin:Header | X-Emby-Token | Jellyfin's authentication header to use (the default is typically correct).
Services:Jellyfin:ServiceUri | | Jellyfin's service uri.
Services:Jellyfin:Token | | Jellyfin's authentication token.
Services:Jellyfin:DefaultUser | | The default user to use if the search term doesn't specify one.
Services:Jellyfin:DefaultDevice | | The default device to use if the search term doesn't specify one.
Services:Jellyfin:DefaultOrder | Newest | The default media item order to use if the search term doesn't specify one (Continue, Shuffle, Oldest, Newest, Shortest or Longest).
Services:Jellyfin:DefaultMediaType | All | The default media item type to use if the search term doesn't specify one (All, Audio, Video or Photo).
Services:Jellyfin:OrderTerms:Continue | Continue,Resume | Alternative terms that can be used to order for resumable media items.
Services:Jellyfin:OrderTerms:Shuffle | Shuffle,Random,Any | Alternative terms that can be used to shuffle media items.
Services:Jellyfin:OrderTerms:Oldest | Oldest,First | Alternative terms that can be used to order media items by oldest created date.
Services:Jellyfin:OrderTerms:Newest | Last,Latest,Newest,Recent | Alternative terms that can be used to order media items by newest created date.
Services:Jellyfin:OrderTerms:Shortest | Shortest,Quickest,Fastest | Alternative terms that can be used to order media items by lowest runtime.
Services:Jellyfin:OrderTerms:Longest | Longest,Slowest | Alternative terms that can be used to order media items by lomgest runtime.
Services:Jellyfin:MediaTypeTerms:Audio | Song,Songs,Music,Track,Tracks,Audio | Alternative terms that can be used to specify audio media items.
Services:Jellyfin:MediaTypeTerms:Video | Video,Videos,Movies,Movie,Show,Shows,Episode,Episodes | Alternative terms that can be used to specify video media items.
Services:Jellyfin:MediaTypeTerms:Photo | Photo,Photos,Pictures,Picture | Alternative terms that can be used to specify photo media items.
Services:Jellyfin:MaximumQueueSize | 100 | Maximum media item queue size to post to Home Assistant.
Services:IFTTT:Token | | Authentication token used to post to Homehook.
Services:HomeAssistant:ServiceUri | | Home Assistant's service uri.
Services:HomeAssistant:Token | | Authentication token used to post to Homehook.
Services:HomeAssistant:JellyWebhookId | | The ID of the Home Assistant webhook on which to POST the media items.
Services:HomeAssistant:JelllyDevices | | The list of media player devices available. Used during phrase parsing.
Services:Language:UserPrepositions | as,from | List of available prepositions to identify a user in a search term.
Services:Language:DevicePrepositions | on,to | List of available prepositions to identify a device in a search term.

# Credits

Big thanks to everyone at the Home Assistant team for their great work!

# Donate

If you appreciate my work and feel like helping me realize other projects, you can donate at <a href="https://paypal.me/MattMckenzy">https://paypal.me/MattMckenzy</a>!
