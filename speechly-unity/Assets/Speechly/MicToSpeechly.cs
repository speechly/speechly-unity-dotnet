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
  IDecoder ondevice_decoder = null;
  IDecoder decoder = null;
  Task decoderInitializationTask = null;
  private bool last_desired_state = false;  // Disabled/Enabled
  private bool on_enable_running = false;
  private bool on_disable_running = false;
  private object on_disable_lock = new object();

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

  async void OnEnable() {
    // Show device caps
    // int minFreq, maxFreq;
    // Microphone.GetDeviceCaps(CaptureDeviceName, out minFreq, out maxFreq);
    // Debug.Log($"minFreq {minFreq} maxFreq {maxFreq}");

    last_desired_state = true;
    if (on_enable_running) {
      // Already running OnEnable, nothing to do
      return;
    }
    on_enable_running = true;

    int capturedAudioBufferMillis = 500;
    int micBufferMillis = AudioProcessorSettings.FrameMillis * AudioProcessorSettings.HistoryFrames + capturedAudioBufferMillis;
    int micBufferSecs = (micBufferMillis / 1000) + 1;

    // Make sure the async OnDisable is not running and shutting down the decoder
    while (on_disable_running) {
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }
    if (last_desired_state == false) {
      // While running OnEnable, the component was disabled, so do not start
      on_enable_running = false;
      return;
    }

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
    on_enable_running = false;
  }

  async void OnDisable() {
    if (_instance == this) {
      last_desired_state = false;
      if (on_disable_running) {
        // Already running OnDisable, nothing to do
        return;
      }
      // Let OnEnable finish first, if it is running. We keep on_disable_running as false to avoid deadlocks with OnEnable.
      // However, multiple OnDisable calls may be waiting at the same time.
      while (on_enable_running && !on_disable_running) {
        await Task.Delay(TimeSpan.FromMilliseconds(100));
      }
      lock (on_disable_lock) {
        if (on_disable_running) {
          // Another instance of OnDisable started executing, nothing to do
          return;
        }
        on_disable_running = true;
      }

      // Make sure the decoder initialization completed before shutting down
      if (decoderInitializationTask != null) {
        while (decoderInitializationTask?.IsCompleted == false) {
          await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        decoderInitializationTask = null;
      }
      if (runSpeechlyCoroutine != null) StopCoroutine(runSpeechlyCoroutine);
      await SpeechlyClient.Shutdown();
      decoder = null;
      clip = null;
      runSpeechlyCoroutine = null;
      on_disable_running = false;
    }
  }

  private IEnumerator RunSpeechly()
  {
    if (SpeechlyEnv == SpeechlyEnvironment.OnDevice) {
      if (ondevice_decoder == null) {
        ondevice_decoder = new OnDeviceDecoder(
          async () => await Platform.Fetch($"{Application.streamingAssetsPath}/SpeechlyOnDevice/Models/{ModelBundle}"),
          deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
          debug: DebugPrint
        );
      }
      decoder = ondevice_decoder;
    } else if (SpeechlyEnv == SpeechlyEnvironment.Production || SpeechlyEnv == SpeechlyEnvironment.Staging) {
      decoder = new CloudDecoder(
        apiUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "https://staging.speechly.com",
        appId: String.IsNullOrWhiteSpace(this.AppId) ? null : this.AppId,
        deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
        debug: DebugPrint
      );
    }

    // Wait for connect
    decoderInitializationTask = SpeechlyClient.Initialize(decoder, AudioProcessorSettings, SpeechRecognitionSettings, preferLibSpeechlyAudioProcessor: SpeechlyEnv == SpeechlyEnvironment.OnDevice);
    yield return new WaitUntil(() => decoderInitializationTask.IsCompleted);
    decoderInitializationTask = null;

    while (true) {
      // Relay debug state
      SpeechlyClient.debug = DebugPrint;
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
