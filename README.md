# Speechly Client for Unity and .NET Standard 2.0 API

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is a cloud API for building voice features into games, XR, applications and web sites. This client library streams audio from a Unity or .NET app to Speechly cloud API and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

## Requirements

- A C# development environment conforming to .NET Standard 2.0 API
  - Unity 2018.1 or later (tested with 2019.4.36f1 and 2021.2.12f1)
  - Microsoft .NET Core 3 or later (tested with .NET 6.0.200)

## Contents of this repository

- [speechly-client-net-standard-2.0/](speechly-client-net-standard-2.0/) contains the Speechly client library code and a sample .NET console app.
- [speechly-unity/](speechly-unity/) folder contains the .NET Speechly client code, Unity-specific microphone code and Unity sample projects:
  - [speechly-unity/Assets/AudioFileToSpeechly/](speechly-unity/Assets/AudioFileToSpeechly/)
  - [speechly-unity/Assets/MicToSpeechly/](speechly-unity/Assets/MicToSpeechly/)
  - [speechly-unity/Assets/VoiceCommands/](speechly-unity/Assets/VoiceCommands/)
- [speechly-client.unitypackage](speechly-client.unitypackage) contains a snapshot of Speechly Unity client library in [speechly-unity/Assets/Speechly/](speechly-unity/Assets/Speechly/).

## Unity usage

Copy the source files from of [speechly-unity/Assets/Speechly/](speechly-unity/Assets/Speechly/) folder to your own project or import `speechly-client.unitypackage`.

### Unity example

The following code example streams a pre-recorded raw audio file (16 bit mono, 16000 samples/sec) to Speechly via the websocket API and logs speech and language recognition results to console.

Constructing SpeechlyClient requires an `appId` (or `projectId`) from [Speechly Dashboard](https://api.speechly.com/dashboard/) that determines which intents and keywords (entities) should be returned in addition to basic speech-to-text (ASR).

Setting `manualUpdate: true` postpones SpeechlyClient's callbacks (OnSegmentChange, OnTranscript...) until you manually run `SpeechlyClient.Update()`. This enables you to call Unity API in SpeechlyClient's callbacks, as Unity API should only be used in the main Unity thread.

```
using Speechly.SLUClient;

  SpeechlyClient client;

  async void Start()
  {
    // Get your app id from https://api.speechly.com/dashboard
    client = new SpeechlyClient(
      appId: "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1", // Basic speech-to-text configuration
      manualUpdate: true
    );
    
    // Set the desired callbacks.
    // OnSegmentChange fires on any change and keeps a record of all words, intents and entities until the end of utterance is signaled with `segment.isFinal`.
    // It's the recommended way to read SLU results.
    
    client.OnSegmentChange = (segment) => {
      Debug.Log(segment.ToString());
    };

    // Connect should be only done once
    await client.Connect();

    // Send test audio. Callback(s) will fire and log the results.
    await client.StartContext();
    await client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    await client.StopContext();
  }

  void Update() {
    // Manually fire Speechly callbacks in main thread instead of websocket thread
    client.Update();
  }

```

### More code examples

- [speechly-unity/Assets/MicToSpeechly/](speechly-unity/Assets/MicToSpeechly/) is a Unity sample project that streams data from microphone to Speechly using [MicToSpeechly.cs](https://github.com/speechly/speechly-unity-dotnet/blob/main/speechly-unity/Assets/Speechly/MicToSpeechly.cs) script running on a GameObject. The speech-to-text results are shown in the UI.

## Developing and contributing

- `link-speechly-sources.sh` shell script will create hard links from `speechly-client-net-standard-2.0/Speechly/` to `speechly-unity/Assets/Speechly/` so shared .NET code in `SLUClient` and `SLUClientTest` is in sync. Please run the script after checking out the repo and before making any changes. If you can't use the script please ensure that the files are identical manually before opening a PR.
