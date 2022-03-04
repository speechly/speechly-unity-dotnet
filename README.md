# Speechly Client for .Net Standard 2.0 and Unity

Early preview version

## Contents

- .Net code project in `speechly-client-net-standard-2.0/` folder. See its README.md for details
- Unity sample project in `speechly-client-net-standard-2.0/` folder. See its README.md for details

- Tested with Unity 2019.4.36f1 Personal and `dotnet` 6.0.200

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

    client = new SpeechlyClient(
        loginUrl: "https://staging.speechly.com/login",
        apiUrl: "wss://staging.speechly.com/ws/v1?sampleRate=16000",
        appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e" // Chinese
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
