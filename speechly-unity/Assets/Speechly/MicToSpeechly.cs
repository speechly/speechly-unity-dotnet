using System.Collections;
using UnityEngine;
using UnityEngine.UI;

using System;
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
  [Tooltip("Capture device name or null for default")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  [Tooltip("Milliseconds of history data to send upon StartContext to capture lead of the utterance.")]
  public bool CalcAudioPeaks = true;
  public bool VADUseEnergyGate = false;
  public float Peak {get; private set; } = 0f;
  public float Energy {get; private set; } = 0f;
  public float BaselineEnergy {get; private set; } = -1f;
  public int FrameMillis = 30;
  [Range(1, 32)]
  public int HistoryFrames = 5;
  [Range(0.0f, 1.0f)]
  [Tooltip("Energy treshold - below this won't trigger activation")]
  public float VADEnergyTreshold = 0.005f;
  [Range(1.0f, 10.0f)]
  [Tooltip("Signal-to-noise energy ratio needed for activation")]
  public float VADSignalToNoise = 2.0f;
  [Range(.0f, 1.0f)]
  public float VADActivationRatio = 0.7f;
  [Range(.0f, 1.0f)]
  public float VADReleaseRatio = 0.2f;
  public int VADSustainMillis = 3000;
  private int activeFrameBits = 0;
  public bool DebugVAD = false;
  public bool DebugPrint = false;
  public bool IsSpeechDetected {get; private set; }
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private float[] waveData;
  private int oldCaptureRingbufferPos;
  private int loops;

  private int historySizeSamples;
  private int frameSamples;
  private int frameSamplesLeft;
  private float vadSum = 0f;
  private float vadSustainContextMillis = 0;

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
      config: new SpeechlyConfig {
        deviceId = SystemInfo.deviceUniqueIdentifier
      },
      manualUpdate: true,
      debug: DebugPrint
    );

    _instance = this;
    DontDestroyOnLoad(this.gameObject);
  } 

  void Start()
  {
    // Show device caps
    // int minFreq, maxFreq;
    // Microphone.GetDeviceCaps(CaptureDeviceName, out minFreq, out maxFreq);
    // Debug.Log($"minFreq {minFreq} maxFreq {maxFreq}");

    // Start audio capture
    int micBufferMillis = FrameMillis * HistoryFrames + 500;
    int micBufferSecs = (micBufferMillis / 1000) + 1;
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
    frameSamplesLeft = frameSamples;
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
      Peak = Peak * 0.95f;

      int captureRingbufferPos = Microphone.GetPosition(CaptureDeviceName);
      
      int samples;
      bool loop = false;
      if (captureRingbufferPos < oldCaptureRingbufferPos)
      {
        samples = (waveData.Length - oldCaptureRingbufferPos) + captureRingbufferPos;
        loop = true;
      } else {
        samples = captureRingbufferPos - oldCaptureRingbufferPos;
      }
      samples = Math.Min(samples, waveData.Length - historySizeSamples);

      if (samples > 0) {
        if (loop) loops++;
        int effectiveHistorySamples = loops > 0 ? historySizeSamples : Math.Min(captureRingbufferPos, historySizeSamples);
        int effectiveCapturePos = (oldCaptureRingbufferPos + (waveData.Length - effectiveHistorySamples)) % waveData.Length;

        // Always captures full buffer length (MicSampleRate * MicBufferLengthMillis / 1000 samples), starting from offset
        clip.GetData(waveData, effectiveCapturePos);
        oldCaptureRingbufferPos = captureRingbufferPos;

        if (CalcAudioPeaks) {
          int s = samples + effectiveHistorySamples - 1;
          while (s >= effectiveHistorySamples)
          {
            Peak = Mathf.Max(Peak, waveData[s]);
            s--;
          }
        }

        if (VADUseEnergyGate) {
          int capturedSamplesLeft = samples;

          while (capturedSamplesLeft > 0) {
            int summedSamples = Math.Min(capturedSamplesLeft, frameSamplesLeft);
            int s = summedSamples;
            while (s > 0)
            {
              vadSum += waveData[s + effectiveHistorySamples] * waveData[s + effectiveHistorySamples];
              s--;
            }
            frameSamplesLeft -= summedSamples;
            if (frameSamplesLeft == 0) {
              frameSamplesLeft = frameSamples;
              Energy = (float)Math.Sqrt(vadSum / frameSamples);
              if (BaselineEnergy < 0f) {
                BaselineEnergy = Energy;
              }
              bool isLoudFrame = Energy > Math.Max(VADEnergyTreshold, BaselineEnergy * VADSignalToNoise);
              PushFrameAnalysis(isLoudFrame);

              int loudFrames = CountLoudFrames(HistoryFrames);
              float loudFrameRatio = (1f * loudFrames) / HistoryFrames;

              if (loudFrameRatio >= VADActivationRatio) {
                vadSustainContextMillis = VADSustainMillis;
                if (!IsSpeechDetected) {
                  IsSpeechDetected = true;
                  if (!DebugVAD) {
                    StartContext();
                  }
                }
              }

              if (loudFrameRatio < VADReleaseRatio && vadSustainContextMillis == 0) {
                if (IsSpeechDetected) {
                  IsSpeechDetected = false;
                  if (!DebugVAD) {
                    StopContext();
                  }
                }
              }

              // Learn background noise level
              if (!IsSpeechDetected) {
                BaselineEnergy = (BaselineEnergy * 0.95f) + (Energy * 0.05f);
              }

              vadSum = 0f;
              vadSustainContextMillis = Math.Max(vadSustainContextMillis - FrameMillis, 0);
            }
            capturedSamplesLeft -= summedSamples;
          }
        }

        if (SpeechlyClient.IsListening) {
          if (SpeechlyClient.SamplesSent == 0) {
            task = SpeechlyClient.SendAudio(waveData, 0, effectiveHistorySamples + samples);
          } else {
            task = SpeechlyClient.SendAudio(waveData, 0 + effectiveHistorySamples, samples + effectiveHistorySamples);
          }
          audioSent = true;
          yield return new WaitUntil(() => task.IsCompleted);
        }
      }
      
      if (!audioSent) {
        yield return null;
      }

    }

  }

  private void PushFrameAnalysis(bool active) {
    activeFrameBits = (active ? 1 : 0) | (activeFrameBits << 1);
  }

  private int CountLoudFrames(int numHistoryFrames) {
    int numActiveFrames = 0;
    int t = activeFrameBits;
    while (numHistoryFrames > 0) {
      if ((t & 1) == 1) numActiveFrames++;
      t = t >> 1;
      numHistoryFrames--;
    }
    return numActiveFrames;
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
