# Speechly Client for .NET Standard 2.0 and Unity

Early preview version

## Requirements

- C# development environment with .NET Standard 2.0 APIs like Unity or Microsoft .NET
- Tested with Unity 2019.4.36f1 and .NET 6.0.200

## Key files and folders in this repo

- [speechly-client.unitypackage](speechly-client.unitypackage) contains a snapshot of `speechly-client-net-standard-2.0/Assets/Speechly` folder.
- [speechly-client-net-standard-2.0/](speechly-client-net-standard-2.0/) contains the Speechly client library code and a sample console app. See its README.md for details.
- [unity-mic-to-speechly/](unity-mic-to-speechly/) folder contains the Unity sample project using the Speechly client library. See its README.md for details.

## Usage

Copy the contents of `Assets/Speechly/` folder to your own project. Then see the example below for usage (API docs will follow!).

## Unity example

Streams a pre-recorded raw audio file (16 bit mono, 16000 samples/sec) to Speechly via the websocket API, logs data using callbacks.

Please note that altering UI is not possible in callbacks, they are (currently) run in an async receive loop.

```
using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

...

  async void Start()
  {
    Logger.Log = Debug.Log;

    // Get your app id from https://api.speechly.com/dashboard
    client = new SpeechlyClient(
        appId: "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1"; // Basic speech-to-text configuration
    );

    // Note: Callbacks can't access UI directly as they are called from async methods

    client.onTentativeTranscript = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
      msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms "));
      Logger.Log(sb.ToString());
    };
    client.onTentativeEntity = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
      msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.entity}': '{w.value}' @ {w.startPosition}..{w.endPosition} "));
      Logger.Log(sb.ToString());
    };
    client.onIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

    client.onTranscript = (msg) =>
    {
      lock(lastWord) {
        lastWord = msg.data.word;
      }
      Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    };
    client.onEntity = (msg) => Logger.Log($"Final entity '{msg.data.entity}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
    client.onTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");

    await client.connect();

    // Send test audio:
    // await client.startContext();
    // await client.sendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    // await client.stopContext();

  }

```

See `MicToSpeechly.cs` for an example of data streamed from microphone and showing last received word in the UI.

## Developing

- `link-speechly-sources.sh` shell script will create hard links from Speechly folder from `speechly-client-net-standard-2.0/Assets/Speechly` to `speechly-client-net-standard-2.0/Assets/Speechly` folder so code changes will be immediately reflected to both for quick development and testing.
