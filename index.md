# Speechly Client Library for Unity and C#

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is an API for building voice features into games, XR, applications and web sites. SpeechlyClient streams audio from a Unity or .NET app to Speechly cloud API (or on-device) and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

### API Documentation

- [SpeechlyClient](api/Speechly.SLUClient.SpeechlyClient.html) class is the main entry point to using Speechly

### GitHub

- [Repository index](https://github.com/speechly/speechly-unity-dotnet)
- [Unity usage and code example](https://github.com/speechly/speechly-unity-dotnet#unity-usage)
- [MicToSpeechly.cs for Unity](https://github.com/speechly/speechly-unity-dotnet#mictospeechly)

### Usage

```

using Speechly.SLUClient;
using Speechly.Tools;
using Speechly.Types;

...

var client = new SpeechlyClient(
  debug: true
);

client.OnSegmentChange = (segment) => {
  Logger.Log(segment.ToString());
};

var decoder = new CloudDecoder(
  appId: "MY_APP_ID_FROM_SPEECHLY_DASHBOARD",
  deviceId: Platform.GetDeviceId()
);

await client.Initialize(decoder);

await client.Start();
client.ProcessAudioFile(fileName);  // Raw 16bit mono 16kHz
// Alternatively use client.ProcessAudio(floats) to process audio samples from e.g. microphone
await client.Stop();

await client.Shutdown();
```

#### Learn more

- [SpeechlyClient.ProcessAudio()](api/Speechly.SLUClient.SpeechlyClient.html#Speechly_SLUClient_SpeechlyClient_ProcessAudio_System_Single___System_Int32_System_Int32_)
