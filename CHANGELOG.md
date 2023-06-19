v2.2.0 (2023-06-16)
- Reworked MicToSpeechly state transitions
- Improved the control flow in the on-device decoder to avoid stray messages
- New decoder parameter: Block size

v2.1.0 (2022-11-01)
- Added libSpeechly v1.1.2 readiness for on-device speech recognition.
- Added HandsFreeListeningVR example. Enabled XR in project.
- Organized folder structure for Asset Store publishing: Everything should be in Assets/com.speechly.speechly-unity/ except for special StreamingAssets folder

v2.0.1 (2022-09-05)
- ModelExpiredException
- SpeechRecognitionSettings to contain silence segmentation setting. CloudDecoder support for boost vocabulary.

v2.0.0 (2022-08-31)
- Introduced AudioProcessorSettings and SpeechRecoginitionSettings in MicToSpeechly
- Improved support for On-device decoder on Oculus, Android, OS X (available separately from Speechly): Model bundle support, libSpeechly VAD support

v1.0.0
- First release
