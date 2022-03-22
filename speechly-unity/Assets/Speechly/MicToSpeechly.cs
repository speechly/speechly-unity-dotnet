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
  public int MicBufferLengthMillis = 1000;
  [Tooltip("Milliseconds of history data to send upon StartContext to capture lead of the utterance.")]
  public int SendHistoryMillis = 200;
  public bool CalcAudioPeaks = true;
  public bool CalcEnergy = true;
  public bool VADUseEnergyGate = false;
  public float Peak {get; private set; } = 0f;
  public float Energy {get; private set; } = 0f;
  public float BaselineEnergy {get; private set; } = -1f;
  public int VADAnalysisWindowMillis = 30;
  [Range(0.0f, 1.0f)]
  [Tooltip("Energy treshold - below this won't trigger activation")]
  public float VADEnergyTreshold = 0.005f;
  [Range(1.0f, 10.0f)]
  [Tooltip("Signal-to-noise energy ratio needed for activation")]
  public float VADActivationRatio = 2.0f;
  public int VADActivationMillis = 150;
  public int VADReleaseMillis = 300;
  public int VADSustainMillis = 3000;
  public bool PrintDebug = false;
  public bool IsSpeechDetected {get; private set; }
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private float[] waveData;
  private int oldCaptureRingbufferPos;
  private int loops;

  private int historySamples;
  private float vadNoiseGateHeat = 0f;
  private int vadAnalysisWindowSamples;
  private int vadAnalysisWindowSamplesLeft;
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
      debug: PrintDebug
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
    clip = Microphone.Start(CaptureDeviceName, true, MicBufferLengthMillis / 1000, MicSampleRate);
    
    if (clip != null)
    {
      waveData = new float[clip.samples * clip.channels];
      // Debug.Log($"Mic frequency {clip.frequency} channels {clip.channels}");
    }
    else
    {
      throw new Exception($"Could not open microphone {CaptureDeviceName}");
    }

    historySamples = MicSampleRate * SendHistoryMillis / 1000;
    vadAnalysisWindowSamples = MicSampleRate * VADAnalysisWindowMillis / 1000;
    vadAnalysisWindowSamplesLeft = vadAnalysisWindowSamples;

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
      samples = Math.Min(samples, waveData.Length - historySamples);

      if (samples > 0) {
        if (loop) loops++;
        int effectiveHistorySamples = loops > 0 ? historySamples : Math.Min(captureRingbufferPos, historySamples);
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

        if (CalcEnergy) {
          int capturedSamplesLeft = samples;

          while (capturedSamplesLeft > 0) {
            int summedSamples = Math.Min(capturedSamplesLeft, vadAnalysisWindowSamplesLeft);
            int s = summedSamples;
            while (s > 0)
            {
              vadSum += waveData[s + effectiveHistorySamples] * waveData[s + effectiveHistorySamples];
              s--;
            }
            vadAnalysisWindowSamplesLeft -= summedSamples;
            if (vadAnalysisWindowSamplesLeft == 0) {
              vadAnalysisWindowSamplesLeft = vadAnalysisWindowSamples;
              Energy = (float)Math.Sqrt(vadSum / vadAnalysisWindowSamples);
              if (BaselineEnergy < 0f) {
                BaselineEnergy = Energy;
              }
              if (Energy > Math.Max(VADEnergyTreshold, BaselineEnergy * VADActivationRatio)) {
                vadNoiseGateHeat = (float)Math.Min(vadNoiseGateHeat + (1f * VADAnalysisWindowMillis / VADActivationMillis), 1f); 
              } else {
                vadNoiseGateHeat = (float)Math.Max(vadNoiseGateHeat - (1f * VADAnalysisWindowMillis / VADReleaseMillis), 0f); 
              }

              if (vadNoiseGateHeat == 1f) {
                if (!IsSpeechDetected) {
                  IsSpeechDetected = true;
                  if (VADUseEnergyGate) {
                    StartContext();
                  }
                }
              }

              if (vadNoiseGateHeat > 0.5f) {
                vadSustainContextMillis = VADSustainMillis;
              }

              if (vadNoiseGateHeat == 0f && vadSustainContextMillis == 0) {
                if (IsSpeechDetected) {
                  IsSpeechDetected = false;
                  vadNoiseGateHeat = 0f;
                  if (VADUseEnergyGate) {
                    StopContext();
                  }
                }
              }

              // Learn background noise level
              if (!IsSpeechDetected) {
                BaselineEnergy = (BaselineEnergy * 0.95f) + (Energy * 0.05f);
              }

              vadSum = 0f;
              vadSustainContextMillis = Math.Max(vadSustainContextMillis - VADAnalysisWindowMillis, 0);
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

  // Drop-and-forget wrapper for async StartContext
  public void StartContext() {
    _ = SpeechlyClient.StartContext();
  }

  // Drop-and-forget wrapper for async StopContext
  public void StopContext() {
    _ = SpeechlyClient.StopContext();
  }

}
