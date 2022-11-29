using System.Collections;
using UnityEngine;

using System;
using System.Threading.Tasks;
using Speechly.Tools;
using Speechly.Types;
using Logger = Speechly.Tools.Logger;

namespace Speechly.SLUClient {

public partial class MicToSpeechly : MonoBehaviour
{
  /// Set to false if you're calling AdjustAudioProcessor manually
  public const bool WATCH_VAD_SETTING = true;

  public enum SpeechlyEnvironment {
    Production,
    Staging,
    OnDevice,
  }

  private static MicToSpeechly _instance;

  public static MicToSpeechly Instance 
  { 
    get { return _instance; } 
  } 
  [Tooltip("Speechly environment to connect to")]
  public SpeechlyEnvironment SpeechlyEnv = SpeechlyEnvironment.Production;

  [Tooltip("Speechly App Id from https://api.speechly.com/dashboard")]
  public string AppId = "";

  [Tooltip("Capture device name or null for default.")]

  public string CaptureDeviceName = null;

  [Tooltip("Model Bundle filename")]
  public string ModelBundle = null;

  public bool DebugPrint = false;

  [SerializeField]
  public AudioProcessorOptions AudioProcessorSettings = new AudioProcessorOptions();

  [SerializeField]
  public ContextOptions SpeechRecognitionSettings = new ContextOptions();

  [SerializeField]
  public AudioInfo Output = new AudioInfo();

  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private float[] waveData;
  private int oldRingbufferPos;
  private bool wasVADEnabled;
  private Coroutine runSpeechlyCoroutine = null;
  IDecoder decoder = null;

  private void Awake() 
  { 
    if (_instance != null && _instance != this) 
    { 
      Destroy(this.gameObject);
      return;
    }

    Logger.Log = Debug.Log;
    Logger.LogError = Debug.LogError;

    SpeechlyClient = new SpeechlyClient(
      manualUpdate: true,
      output: this.Output,
      debug: DebugPrint
    );

    _instance = this;
    DontDestroyOnLoad(this.gameObject);
  }

  void OnEnable() {
    // Show device caps
    // int minFreq, maxFreq;
    // Microphone.GetDeviceCaps(CaptureDeviceName, out minFreq, out maxFreq);
    // Debug.Log($"minFreq {minFreq} maxFreq {maxFreq}");

    int capturedAudioBufferMillis = 500;
    int micBufferMillis = AudioProcessorSettings.FrameMillis * AudioProcessorSettings.HistoryFrames + capturedAudioBufferMillis;
    int micBufferSecs = (micBufferMillis / 1000) + 1;
    // Start audio capture
    clip = Microphone.Start(CaptureDeviceName, true, micBufferSecs, AudioProcessorSettings.InputSampleRate);

    if (clip != null)
    {
      waveData = new float[clip.samples * clip.channels];
      // Debug.Log($"Mic frequency {clip.frequency} channels {clip.channels}");
    }
    else
    {
      throw new Exception($"Could not open microphone {CaptureDeviceName}");
    }

    wasVADEnabled = this.AudioProcessorSettings.VADControlsListening;
    runSpeechlyCoroutine = StartCoroutine(RunSpeechly());
  }

  async void OnDisable() {
    if (runSpeechlyCoroutine != null) StopCoroutine(runSpeechlyCoroutine);
    runSpeechlyCoroutine = null;
    await SpeechlyClient.Shutdown();
  }

  private IEnumerator RunSpeechly()
  {
    if (SpeechlyEnv == SpeechlyEnvironment.OnDevice) {
      decoder = new OnDeviceDecoder(
        async () => await Platform.Fetch($"{Application.streamingAssetsPath}/SpeechlyOnDevice/Models/{ModelBundle}"),
        deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
        debug: DebugPrint
      );
    }

    if (SpeechlyEnv == SpeechlyEnvironment.Production || SpeechlyEnv == SpeechlyEnvironment.Staging) {
      decoder = new CloudDecoder(
        apiUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "https://staging.speechly.com",
        appId: String.IsNullOrWhiteSpace(this.AppId) ? null : this.AppId,
        deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
        debug: DebugPrint
      );
    }

    // Wait for connect
    Task task;
    task = SpeechlyClient.Initialize(decoder, AudioProcessorSettings, SpeechRecognitionSettings, preferLibSpeechlyAudioProcessor: SpeechlyEnv == SpeechlyEnvironment.OnDevice);
    yield return new WaitUntil(() => task.IsCompleted);

    while (true) {
      // Relay debug state
      SpeechlyClient.Debug = DebugPrint;
      // Fire handlers in main Unity thread
      SpeechlyClient.Update();

      int captureRingbufferPos = Microphone.GetPosition(CaptureDeviceName);
      
      int samples;
      if (captureRingbufferPos < oldRingbufferPos)
      {
        samples = (waveData.Length - oldRingbufferPos) + captureRingbufferPos;
      } else {
        samples = captureRingbufferPos - oldRingbufferPos;
      }

      if (samples > 0) {
        // Always captures full buffer length (MicSampleRate * MicBufferLengthMillis / 1000 samples), starting from offset
        clip.GetData(waveData, oldRingbufferPos);
        oldRingbufferPos = captureRingbufferPos;
        SpeechlyClient.ProcessAudio(waveData, 0, samples);
      }

      if (WATCH_VAD_SETTING) {
        if (this.AudioProcessorSettings.VADControlsListening != wasVADEnabled) {
          SpeechlyClient.AdjustAudioProcessor(vadControlsListening: this.AudioProcessorSettings.VADControlsListening);
          wasVADEnabled = this.AudioProcessorSettings.VADControlsListening;
        }
      }

      // Wait for a frame for new audio
      yield return null;
    }
  }
}

}