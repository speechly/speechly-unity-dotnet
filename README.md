# Speechly Client for Unity and .NET Standard 2.0 API

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is a cloud API for building voice features into games, XR, applications and web sites. This client library streams audio from an Unity app to Speechly cloud API and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

## Requirements

- A C# development environment conforming to .NET Standard 2.0 API
  - Unity 2018.1 or later (tested with 2019.4.36f1 and 2021.2.12f1)
  - Microsoft .NET Core 3 or later (tested with .NET 6.0.200)

## Contents of this repository

- [speechly-client-net-standard-2.0/](speechly-client-net-standard-2.0/) contains the Speechly client library code and a sample .NET console app.
- [speechly-unity/](speechly-unity/) folder contains the .NET Speechly client library plus Unity-specific microphone code along with Unity sample projects:
  - [speechly-unity/Assets/AudioFileToSpeechly/](speechly-unity/Assets/AudioFileToSpeechly/)
  - [speechly-unity/Assets/MicToSpeechly/](speechly-unity/Assets/MicToSpeechly/)
  - [speechly-unity/Assets/VoiceCommands/](speechly-unity/Assets/VoiceCommands/)
- [speechly-client.unitypackage](speechly-client.unitypackage) contains a snapshot of `speechly-unity/Assets/Speechly/` Speechly Unity client library.

## Unity usage

Copy the source files from of [speechly-unity/Assets/Speechly/](speechly-unity/Assets/Speechly/) folder to your own project or import `speechly-client.unitypackage`.

### Unity example

Streams a pre-recorded raw audio file (16 bit mono, 16000 samples/sec) to Speechly via the websocket API, logs data using callbacks.

`manualUpdate: true` postpones Speechly callbacks until you manually run `Update()`. This enables you to use Unity API in callbacks, which is not allowed outside the main Unity thread.

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
    
    // Set desired callbacks. Note: Callbacks can't access UI directly as they are called from async methods

    // Segment keeps record of all words and detected intents and entities. It's the recommended way to read SLU results.
    Logger.Log = Debug.Log;
    client.OnSegmentChange = (segment) => {
      Logger.Log(segment.ToString());
    };

    // Connect should be only done once
    await client.Connect();

    // Send test audio, see log for results
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

- See [MicToSpeechly.cs](https://github.com/speechly/speechly-unity-dotnet/blob/main/speechly-unity/Assets/Speechly/MicToSpeechly.cs) for an Unity example of streaming data from microphone to Speechly and [speechly-unity/Assets/MicToSpeechly/](speechly-unity/Assets/MicToSpeechly/) for a project using it to show speech-to-text in the UI.

## Developing

- `link-speechly-sources.sh` shell script will create hard links from `speechly-client-net-standard-2.0/Speechly/` to `speechly-unity/Assets/Speechly/` folder so shared code using .NET API only will remain in sync.
