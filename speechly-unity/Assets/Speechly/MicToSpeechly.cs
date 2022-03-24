﻿using System.Collections;
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
  [Tooltip("Capture device name or null for default.")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  [Tooltip("Milliseconds of history data to send upon StartContext to capture lead of the utterance.")]
  public int FrameMillis = 30;
  [Range(1, 32)]
  [Tooltip("Number of frames to keep in memory. When listening is started, history frames are sent to capture the lead-in audio.")]
  public int HistoryFrames = 8;
  public bool CalcAudioPeaks = true;
  [Tooltip("Voice Activity Detection (VAD) using adaptive energy thresholding. Automatically controls listening based on audio 'loudness'.")]
  public bool EnergyThresholdVAD = false;
  [Range(0.0f, 1.0f)]
  [Tooltip("Energy threshold - below this won't trigger activation")]
  public float VADMinimumEnergy = 0.005f;
  [Range(1.0f, 10.0f)]
  [Tooltip("Signal-to-noise energy ratio needed for frame to be 'loud'")]
  public float VADSignalToNoise = 2.0f;
  [Range(1, 32)]
  [Tooltip("Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.")]
  public int VADFrames = 5;
  [Range(.0f, 1.0f)]
  [Tooltip("Minimum 'loud' to 'silent' frame ratio in history to activate 'IsSignalDetected'")]
  public float VADActivation = 0.7f;
  [Range(.0f, 1.0f)]
  [Tooltip("Maximum 'loud' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.")]
  public float VADRelease = 0.2f;
  [Range(0, 8000)]
  [Tooltip("Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.")]
  public int VADSustainMillis = 3000;
  [Range(0, 5000)]
  [Tooltip("Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.")]
  public int VADNoiseHalftimeMillis = 400;
  [Tooltip("Disable VAD listening control if you want to use the energy threshold but want to implement custom listening control by reading IsSignalDetected state.")]
  public bool VADControlListening = true;
  public bool DebugPrint = false;
  public float Peak {get; private set; } = 0f;
  public float Energy {get; private set; } = 0f;
  public float BaselineEnergy {get; private set; } = -1f;
  public bool IsSignalDetected {get; private set; }
  private int loudFrameBits = 0;
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private float[] waveData;
  private int oldCaptureRingbufferPos;
  private int loops;

  private int historySizeSamples;
  private int frameSamples;
  private int frameSamplesLeft;
  private float sumEnergySquared = 0f;
  private float vadSustainMillisLeft = 0;
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
          while (s >= effectiveHistorySamples) {
            Peak = Mathf.Max(Peak, waveData[s]);
            s--;
          }
        }

        if (EnergyThresholdVAD) {
          int capturedSamplesLeft = samples;

          while (capturedSamplesLeft > 0) {
            int summedSamples = Math.Min(capturedSamplesLeft, frameSamplesLeft);
            int s = summedSamples + effectiveHistorySamples - 1;
            while (s >= effectiveHistorySamples) {
              sumEnergySquared += waveData[s] * waveData[s];
              s--;
            }
            frameSamplesLeft -= summedSamples;
            if (frameSamplesLeft == 0) {
              frameSamplesLeft = frameSamples;
              Energy = (float)Math.Sqrt(sumEnergySquared / frameSamples);
              if (BaselineEnergy < 0f) {
                BaselineEnergy = Energy;
              }
              bool isLoudFrame = Energy > Math.Max(VADMinimumEnergy, BaselineEnergy * VADSignalToNoise);
              PushFrameHistory(isLoudFrame);

              int loudFrames = CountLoudFrames(VADFrames);
              float loudFrameRatio = (1f * loudFrames) / VADFrames;

              if (loudFrameRatio >= VADActivation) {
                vadSustainMillisLeft = VADSustainMillis;
                if (!IsSignalDetected) {
                  IsSignalDetected = true;
                  if (VADControlListening) {
                    StartContext();
                  }
                }
              }

              if (loudFrameRatio < VADRelease && vadSustainMillisLeft == 0) {
                if (IsSignalDetected) {
                  IsSignalDetected = false;
                  if (VADControlListening) {
                    StopContext();
                  }
                }
              }

              // Gradually learn background noise level
              if (!IsSignalDetected) {
                if (VADNoiseHalftimeMillis > 0f) {
                  var decay = (float)Math.Pow(2.0, -FrameMillis / (double)VADNoiseHalftimeMillis);
                  BaselineEnergy = (BaselineEnergy * decay) + (Energy * (1f - decay));
                }
              }

              sumEnergySquared = 0f;
              vadSustainMillisLeft = Math.Max(vadSustainMillisLeft - FrameMillis, 0);
            }
            capturedSamplesLeft -= summedSamples;
          }
        }

        // Turn off listening when VAD is disabled
        bool vadEnabled = EnergyThresholdVAD && VADControlListening;
        if (!vadEnabled && wasVADEnabled) {
          ResetVAD();
          if (SpeechlyClient.IsListening) {
            StopContext();
          }
        }
        wasVADEnabled = vadEnabled;

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

  private void PushFrameHistory(bool isLoud) {
    loudFrameBits = (isLoud ? 1 : 0) | (loudFrameBits << 1);
  }

  private int CountLoudFrames(int numHistoryFrames) {
    int numActiveFrames = 0;
    int t = loudFrameBits;
    while (numHistoryFrames > 0) {
      if ((t & 1) == 1) numActiveFrames++;
      t = t >> 1;
      numHistoryFrames--;
    }
    return numActiveFrames;
  }

  private void ResetVAD() {
    if (!EnergyThresholdVAD) {
      IsSignalDetected = false;
      loudFrameBits = 0;
      Energy = 0f;
      BaselineEnergy = -1f;
      frameSamplesLeft = frameSamples;
      sumEnergySquared = 0f;
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
