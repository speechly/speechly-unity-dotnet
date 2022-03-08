# Speechly Client for Unity using .NET Standard 2.0 API

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is a cloud API for building voice features into applications and web sites. This client library streams audio from an Unity app to Speechly cloud API and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

Early preview version.

## Requirements

- A C# development environment conforming to .NET Standard 2.0 API
  - Unity 2018.1 or later (tested with 2019.4.36f1 and 2021.2.12f1)
  - Microsoft .NET Core 3 or later (tested with .NET 6.0.200)

## Contents of this repo

- [speechly-client.unitypackage](speechly-client.unitypackage) contains a snapshot of `speechly-client-net-standard-2.0/Assets/Speechly` folder.
- [speechly-client-net-standard-2.0/](speechly-client-net-standard-2.0/) contains the Speechly client library code and a sample console app.
- [unity-mic-to-speechly/](unity-mic-to-speechly/) folder contains the Unity sample project using the Speechly client library.

## Unity usage

Copy the source files from of [speechly-client-net-standard-2.0/Assets/Speechly/](speechly-client-net-standard-2.0/Assets/Speechly/) folder to your own project or use `speechly-client.unitypackage` which contains the same files. Then see the example below for usage (API docs will follow!).

### Unity example

Streams a pre-recorded raw audio file (16 bit mono, 16000 samples/sec) to Speechly via the websocket API, logs data using callbacks.

Please note that altering UI is not possible in callbacks, they are (currently) run in an async receive loop.

```
using Speechly.SLUClient;

  async void Start()
  {
    // Get your app id from https://api.speechly.com/dashboard
    var client = new SpeechlyClient(
        appId: "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1" // Basic speech-to-text configuration
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

```

### Unity example with all callbacks set

Here's an example with all lower-level callbacks set.

```
using Speechly.SLUClient;
using System.Linq;

  async void Start()
  {
    Logger.Log = Debug.Log;

    // Get your app id from https://api.speechly.com/dashboard
    var client = new SpeechlyClient(
        appId: "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1" // Basic speech-to-text configuration
    );
        
    // Set desired callbacks. Note: Callbacks can't access UI directly as they are called from async methods

    // Segment keeps record of all words and detected intents and entities. It's the recommended way to read SLU results.
    client.OnSegmentChange = (segment) => {
      Logger.Log(segment.ToString());
    };

    // Set low-level callbacks to receive transcript and SLU results.
    client.OnIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");
    client.OnTranscript = (msg) => Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    client.OnEntity = (msg) => Logger.Log($"Final entity '{msg.data.entity}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");

    // Set low-level callbacks to receive tentative transcript and SLU results as soon as they arrive.
    client.OnTentativeTranscript = (msg) =>
    {
      var s = $"Tentative transcript ({msg.data.words.Length} words):";
      msg.data.words.ToList().ForEach(w => s = $"{s} '{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms ");
      Logger.Log(s);
    };
    
    client.OnTentativeEntity = (msg) =>
    {
      var s = $"Tentative entities ({msg.data.entities.Length}):";
      msg.data.entities.ToList().ForEach(w => s = $"{s} '{w.entity}': '{w.value}' @ {w.startPosition}..{w.endPosition} ");
      Logger.Log(s);
    };
    
    client.OnTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");

    // Display SLUClient's internal state changes
    client.OnStateChange = (clientState) => Logger.Log($"ClientState: {clientState}");

    // Connect should be only done once
    await client.Connect();

    // Send test audio, see log for results
    await client.StartContext();
    await client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    await client.StopContext();
  }


```

### More examples

- See `MicToSpeechly.cs` for an Unity example of streaming data from microphone and showing last received word in the UI.
- See `SpeechlyClientTest.cs` for a generic .NET Standard 2.0 example of streaming raw audio from a file and logging the results via callbacks.

## Developing

- `link-speechly-sources.sh` shell script will create hard links from `speechly-client-net-standard-2.0/Assets/Speechly` to `speechly-client-net-standard-2.0/Assets/Speechly` folder so code changes will be immediately reflected to both for quick development and testing.
