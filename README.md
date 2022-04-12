# Speechly Client for Unity and .NET Standard 2.0 API

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is an API for building voice features into games, XR, applications and web sites. This client library streams audio from a Unity or .NET app to Speechly cloud API and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

Download [speechly-client.unitypackage](https://github.com/speechly/speechly-unity-dotnet/raw/main/speechly-client.unitypackage) to get the latest Speechly Unity client library and example scenes bundled up for Unity.

## Requirements

- A C# development environment conforming to .NET Standard 2.0 API
  - Unity 2018.1 or later (tested with 2019.4.36f1 and 2021.2.12f1)
  - Microsoft .NET Core 3 or later (tested with .NET 6.0.200)

## Contents of this repository

- [speechly-client-net-standard-2.0/](speechly-client-net-standard-2.0/) contains the Speechly client library code and a sample .NET console app.
- [speechly-unity/Assets/Speechly/](speechly-unity/Assets/Speechly/) folder contains the same basic .NET Speechly client code plus Unity-specific `MicToSpeechly.cs` microphone code and Unity sample projects:
  - [speechly-unity/Assets/SpeechlyExamples/MicToSpeechly/](speechly-unity/Assets/SpeechlyExamples/MicToSpeechly/)
  - [speechly-unity/Assets/SpeechlyExamples/AudioFileToSpeechly/](speechly-unity/Assets/SpeechlyExamples/AudioFileToSpeechly/)
  - [speechly-unity/Assets/SpeechlyExamples/VoiceCommands/](speechly-unity/Assets/SpeechlyExamples/VoiceCommands/)

## Unity usage

Import `Speechly/` folder from [speechly-client.unitypackage](https://github.com/speechly/speechly-unity-dotnet/raw/main/speechly-client.unitypackage) that contains code to use Speechly cloud API.

> If you want to skip straight to trying out a working sample scene, see [more code examples](#more-code-examples) below.

### Unity example

The following code example streams a pre-recorded raw audio file (16 bit mono, 16000 samples/sec) to Speechly via the websocket API and logs speech and language recognition results to console.

Constructing SpeechlyClient requires an `appId` (or `projectId`) from [Speechly Dashboard](https://api.speechly.com/dashboard/) that determines which intents and keywords (entities) should be returned in addition to basic speech-to-text (ASR).

Setting `manualUpdate: true` postpones SpeechlyClient's callbacks (OnSegmentChange, OnTranscript...) until you manually run `SpeechlyClient.Update()`. This enables you to call Unity API in SpeechlyClient's callbacks, as Unity API should only be used in the main Unity thread.

```
using UnityEngine;
using Speechly.SLUClient;
 
public class AudioFileToSpeechly : MonoBehaviour
{

  SpeechlyClient client;

  async void Start()
  {
    client = new SpeechlyClient(
      manualUpdate: true,
      debug: true
    );

    // Set the desired callbacks.
    // OnSegmentChange fires on any change and keeps a record of all words, intents and entities until the end of utterance is signaled with `segment.isFinal`.
    // It's the recommended way to read SLU results.
    
    client.OnSegmentChange = (segment) => {
      Debug.Log(segment.ToString());
    };

    // Get your app id from https://api.speechly.com/dashboard
    decoder = new CloudDecoder(
      appId: "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1", // Basic ASR
      deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
      debug: true
    );

    // Connect to CloudDecoder
    await SpeechlyClient.Initialize(decoder);

    // Send test audio. Callback(s) will fire and log the results.
    await client.StartContext();
    await client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    await client.StopContext();
  }

  void Update()
  {
    // Manually fire Speechly callbacks in main thread instead of websocket thread
    client.Update();
  }
}
```

## More code examples

### MicToSpeechly

Import [SpeechlyExamples/MicToSpeechly/](speechly-unity/Assets/SpeechlyExamples/MicToSpeechly/) and `Speechly/` folders from [speechly-client.unitypackage](https://github.com/speechly/speechly-unity-dotnet/raw/main/speechly-client.unitypackage) to run a Unity sample scene that streams data from microphone to Speechly using [MicToSpeechly.cs](https://github.com/speechly/speechly-unity-dotnet/blob/main/speechly-unity/Assets/Speechly/MicToSpeechly.cs) script running on a GameObject. App-specific logic is in `UseSpeechly.cs` which registers a callback and shows speech-to-text results in the UI.

### VoiceCommands

Import [SpeechlyExamples/VoiceCommands/](speechly-unity/Assets/SpeechlyExamples/VoiceCommands/) and `Speechly/` folders from `speechly-client.unitypackage` to run a Unity sample scene that showcases a point-and-talk interface: target an object and hold the mouse button to issue speech commands like "make it big and red" or "delete". Again, app-specific logic is in `UseSpeechly.cs` which registers a callback to respond to detected intents and keywords (entities).

## OS X

To enable microphone input on OS X, set `Player Settings > Settings for PC, Mac & Linux Standalone > Other Settings > Microphone Usage Description`, to for example, "Voice input is automatically processed by Speechly.com".

## Android

### Device testing

To diagnose problems with device builds, you can do the following:

- First try running MicToSpeechlyScene.unity in the editor without errors.
- Change to Android player, set MicToSpeechlyScene.unity as the main scene and do a `build and run` to deploy the build to on a device.
- On terminal, do `adb logcat -s Unity:D` to follow Unity-related logs from the device.
- Run the app on device. Keep `Hold to talk` button pressed and say "ONE, TWO, THREE". Then release the button.
- You should see "ONE, TWO, THREE" displayed in the top-left corner of the screen. If not, see the terminal for errors.

### Android troubleshooting

- `Exception: Could not open microphone` and green VU meter won't move. Cause: There's no implementation in place to wait for permission prompt to complete so mic permission is not given on the first run and Microphone.Start() fails. Fix: Implement platform specific permission check, or, restart app after granting the permission.
- `WebException: Error: NameResolutionFailure` and transcript won't change when button held and app is spoken to. Cause: Production builds restric access to internet. Fix: With Android target active, go Player settings and find "Internet Access" and change it to "required".
- IL2CPP build fails with `NullReferenceException` at `System.Runtime.Serialization.Json.JsonFormatWriterInterpreter.TryWritePrimitive`. Cause: System.Runtime.Serialization.dll uses reflection to access some methods. Fix: To prevent Unity managed code linker from stripping away these methods add the file `link.xml` with the following content:

```
<linker>
  <assembly fullname="System.Runtime.Serialization" preserve="all"/>
</linker>
```

### Command line usage with `dotnet`

SpeechlyClient features can be run with prerecorded audio on the command line in `speechly-client-net-standard-2.0/` folder:

- `dotnet run test` processes an example file, sends to Speechly cloud SLU and prints the received results in console.
- `dotnet run vad` processes an example file, sends the utterances audio to files in `temp/` folder as 16 bit raw and creates an utterance timestamp `.tsv` (tab-separated values) for each audio file processed.
- `dotnet run vad myaudiofiles/*.raw` processes a set of files with VAD.

## Developing and contributing

- `link-speechly-sources.sh` shell script will create hard links from `speechly-client-net-standard-2.0/Speechly/` to `speechly-unity/Assets/Speechly/` so shared .NET code in `SLUClient` is in sync. Please run the script after checking out the repo and before making any changes. If you can't use the script please ensure that the files are identical manually before opening a PR.
