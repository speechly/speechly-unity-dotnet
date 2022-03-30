using System.Collections;
using UnityEngine;
using UnityEngine.UI;

using System;
using System.Threading;
using System.Threading.Tasks;

using Speechly.SLUClient;
using Logger = Speechly.SLUClient.Logger;

public class MicToSpeechly : MonoBehaviour
{
  public enum SpeechlyEnvironment {
    Production,
    Staging,
  }

  private static MicToSpeechly _instance;

  public static MicToSpeechly Instance 
  { 
    get { return _instance; } 
  } 
  [Tooltip("Speechly environment to connect to")]
  public SpeechlyEnvironment SpeechlyEnv = SpeechlyEnvironment.Production;

  [Tooltip("Speechly App Id")]
  public string AppId = "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1"; // Speechly Client Demos / speech-to-text only configuration
  [Tooltip("Capture device name or null for default.")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  [Tooltip("Milliseconds of history data to send upon StartContext to capture lead of the utterance.")]
  public int FrameMillis = 30;
  [Range(1, 32)]
  [Tooltip("Number of frames to keep in memory. When listening is started, history frames are sent to capture the lead-in audio.")]
  public int HistoryFrames = 8;
  [Tooltip("Voice Activity Detection (VAD) using adaptive energy thresholding. Automatically controls listening based on audio 'loudness'.")]
  public EnergyTresholdVAD Vad = new EnergyTresholdVAD{ Enabled = true, VADControlListening = false };
  public bool DebugPrint = false;
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private float[] waveData;
  private int oldRingbufferPos;
  private int processedRingbufferPos;
  private int unprocessedSamplesLeft = 0;
  private int loops;

  private int historySizeSamples;
  private int frameSamples;
  private bool wasVADEnabled = false;

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
      loginUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "https://staging.speechly.com/login",
      apiUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "wss://staging.speechly.com/ws/v1?sampleRate=16000",
      appId: this.AppId,
      deviceId: SystemInfo.deviceUniqueIdentifier,
      vad: this.Vad,
      manualUpdate: true,
      debug: DebugPrint
    );

    _instance = this;
    DontDestroyOnLoad(this.gameObject);
  } 

  void Start()
  {
    Logger.Log($"Start mic loop @ {Thread.CurrentThread.ManagedThreadId}");

    // Show device caps
    // int minFreq, maxFreq;
    // Microphone.GetDeviceCaps(CaptureDeviceName, out minFreq, out maxFreq);
    // Debug.Log($"minFreq {minFreq} maxFreq {maxFreq}");

    int capturedAudioBufferMillis = 500;
    int micBufferMillis = FrameMillis * HistoryFrames + capturedAudioBufferMillis;
    int micBufferSecs = (micBufferMillis / 1000) + 1;
    // Start audio capture
    clip = Microphone.Start(CaptureDeviceName, true, micBufferSecs, MicSampleRate);

    if (clip != null)
    {
      waveData = new float[clip.samples * clip.channels];
      // Debug.Log($"Mic frequency {clip.frequency} channels {clip.channels}");
    }
    else
    {
      throw new Exception($"Could not open microphone {CaptureDeviceName}");
    }

    frameSamples = MicSampleRate * FrameMillis / 1000;
    historySizeSamples = frameSamples * HistoryFrames;

    StartCoroutine(RunSpeechly());
  }

  private IEnumerator RunSpeechly()
  {
    // Wait for connect if needed
    Task task;
    task = SpeechlyClient.Connect();
    yield return new WaitUntil(() => task.IsCompleted);

    while (true) {
      // Fire handlers in main Unity thread
      SpeechlyClient.Update();

      bool audioSent = false;

      int captureRingbufferPos = Microphone.GetPosition(CaptureDeviceName);
      
      int samples;
      bool loop = false;
      if (captureRingbufferPos < oldRingbufferPos)
      {
        samples = (waveData.Length - oldRingbufferPos) + captureRingbufferPos;
        loop = true;
      } else {
        samples = captureRingbufferPos - oldRingbufferPos;
      }
      // Limit number of samples, during 1st processing frame as there may be a lot of data
      samples = Math.Min(samples, waveData.Length - historySizeSamples);

      if (samples > 0) {
        if (loop) loops++;
        unprocessedSamplesLeft += samples;
        oldRingbufferPos = captureRingbufferPos;
  
        if (unprocessedSamplesLeft >= frameSamples) {
          int effectiveHistorySamples = loops > 0 ? historySizeSamples : Math.Min(captureRingbufferPos, historySizeSamples);
          int effectiveCapturePos = (processedRingbufferPos + (waveData.Length - effectiveHistorySamples)) % waveData.Length;

          // Always captures full buffer length (MicSampleRate * MicBufferLengthMillis / 1000 samples), starting from offset
          clip.GetData(waveData, effectiveCapturePos);

          int audioPos = effectiveHistorySamples;

          while (unprocessedSamplesLeft >= frameSamples) {
            // If listening, send audio
            if (SpeechlyClient.SamplesSent == 0) {
              task = SpeechlyClient.ProcessFrame(waveData, 0, audioPos + frameSamples);
            } else {
              task = SpeechlyClient.ProcessFrame(waveData, audioPos, frameSamples);
            }
            yield return new WaitUntil(() => task.IsCompleted);
            audioSent = true;

            // Ensure listening is stopped if VAD state is altered "on the fly"
            if (Vad != null) {
              if (Vad.Enabled && Vad.VADControlListening) {
                wasVADEnabled = true;
              } else {
                if (wasVADEnabled) {
                  wasVADEnabled = false;
                  if (SpeechlyClient.IsListening) {
                    StopContext();
                  }
                }
              }
            }

            // Next frame
            audioPos += frameSamples;
            unprocessedSamplesLeft -= frameSamples;
          }

          processedRingbufferPos = (processedRingbufferPos + audioPos - effectiveHistorySamples) % waveData.Length;
        }
      }
      
      if (!audioSent) {
        yield return null;
      }
    }
  }

  // Drop-and-forget wrapper for async StartContext
  public void StartContext() {
    _ = SpeechlyClient.StartContext();
  }

  // Drop-and-forget wrapper for async StopContext
  public void StopContext() {
    _ = SpeechlyClient.StopContext();
  }

}
