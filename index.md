# Speechly Client for Unity and .NET Standard 2.0 API

[Speechly](https://www.speechly.com/?utm_source=github&utm_medium=react-client&utm_campaign=text) is an API for building voice features into games, XR, applications and web sites. SpeechlyClient streams audio from a Unity or .NET app to Speechly cloud API (or on-device) and provides a C# API for receiving real-time speech-to-text transcription and natural language understanding results.

### API Documentation

- [Class SpeechlyClient](api/Speechly.SLUClient.SpeechlyClient.html)

### GitHub

- [Repository index](https://github.com/speechly/speechly-unity-dotnet)
- [Unity usage and code example](https://github.com/speechly/speechly-unity-dotnet#unity-usage)
- [MicToSpeechly.cs for Unity](https://github.com/speechly/speechly-unity-dotnet#mictospeechly)

### Overview of SpeechlyClient audio pipeline

- Input: Feed speech audio with [SpeechlyClient.ProcessAudio()](api/Speechly.SLUClient.SpeechlyClient.html#Speechly_SLUClient_SpeechlyClient_ProcessAudio_System_Single___System_Int32_System_Int32_System_Boolean_)
- Downsampling to 16kHz
- Update history ringbuffer
- Automatic voice activity detection (VAD)
- Send utterances to files
- Send utterances to Speechly SLU engine
