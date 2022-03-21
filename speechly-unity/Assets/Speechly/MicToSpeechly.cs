using System.Collections;
using UnityEngine;
using UnityEngine.UI;

using System;
using System.Threading.Tasks;

using Speechly.SLUClient;
using Logger = Speechly.SLUClient.Logger;

public class MicToSpeechly : MonoBehaviour
{
  private static MicToSpeechly _instance;

  public static MicToSpeechly Instance 
  { 
    get { return _instance; } 
  } 

  [Tooltip("Speechly App Id")]
  public string AppId = "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1"; // Speechly Client Demos / speech-to-text only configuration
  [Tooltip("Capture device name or null for default")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  public int MicBufferLengthSecs = 1;
  public bool CalcAudioPeaks = true;
  public bool CalcEnergyVAD = true;
  public float Peak {get; private set; } = 0f;
  public float Energy {get; private set; } = 0f;
  public float BaselineEnergy {get; private set; } = -1f;
  public float SpeechTolerance {get; private set; } = 0f;
  public int EnergyAnalysisWindowMillis = 30;
  public int AttackMillis = 100;
  public int ReleaseMillis = 300;
  public int InitialSpeakHoldMillis = 3000;
  public bool IsSpeechDetected {get; private set; }
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private int oldCaptureRingbufferPos;
  private float[] waveData;
  private int vadAnalysisWindowSamples;
  private int vadAnalysisWindowSamplesLeft;
  private float vadSum = 0f;
  private float speakHoldMillis = 0;

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
        loginUrl: "https://staging.speechly.com/login",
        apiUrl: "wss://staging.speechly.com/ws/v1?sampleRate=16000",
        appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e", // Restaurant booking configuration
//      appId: this.AppId,
      config: new SpeechlyConfig {
        deviceId = SystemInfo.deviceUniqueIdentifier
      },
      manualUpdate: true,
      debug: true
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
    clip = Microphone.Start(CaptureDeviceName, true, MicBufferLengthSecs, MicSampleRate);
    
    if (clip != null)
    {
      waveData = new float[clip.samples * clip.channels];
      // Debug.Log($"Mic frequency {clip.frequency} channels {clip.channels}");
    }
    else
    {
      throw new Exception($"Could not open microphone {CaptureDeviceName}");
    }

    vadAnalysisWindowSamples = MicSampleRate * EnergyAnalysisWindowMillis / 1000;
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
      if (captureRingbufferPos < oldCaptureRingbufferPos)
      {
        samples = (waveData.Length - oldCaptureRingbufferPos) + captureRingbufferPos;
      } else {
        samples = captureRingbufferPos - oldCaptureRingbufferPos;
      }

      if (samples > 0) {

        // Always captures full buffer length (MicSampleRate * MicBufferLengthSecs samples), starting from offset
        clip.GetData(waveData, oldCaptureRingbufferPos);
        oldCaptureRingbufferPos = captureRingbufferPos;

        if (CalcAudioPeaks) {
          int s = 0;
          while (s < samples)
          {
            Peak = Mathf.Max(Peak, waveData[s]);
            s++;
          }
        }

        if (CalcEnergyVAD) {
          int capturedSamplesLeft = samples;

          while (capturedSamplesLeft > 0) {
            int summedSamples = Math.Min(capturedSamplesLeft, vadAnalysisWindowSamplesLeft);
            int s = summedSamples;
            while (s > 0)
            {
              vadSum += waveData[s] * waveData[s];
              s--;
            }
            vadAnalysisWindowSamplesLeft -= summedSamples;
            if (vadAnalysisWindowSamplesLeft == 0) {
              vadAnalysisWindowSamplesLeft = vadAnalysisWindowSamples;
              Energy = (float)Math.Sqrt(vadSum / vadAnalysisWindowSamples);
              if (BaselineEnergy < 0f) {
                BaselineEnergy = Energy;
              }
              if (Energy > BaselineEnergy * 2.0f) {
                SpeechTolerance = (float)Math.Min(SpeechTolerance + (1f * EnergyAnalysisWindowMillis / AttackMillis), 1f); 
              } else {
                SpeechTolerance = (float)Math.Max(SpeechTolerance - (1f * EnergyAnalysisWindowMillis / ReleaseMillis), 0f); 
              }
              if (!IsSpeechDetected) {
                if (SpeechTolerance == 1f) {
                  IsSpeechDetected = true;
                  speakHoldMillis = InitialSpeakHoldMillis;
                  // SpeechlyClient.StartContext();
                }
              } else {
                if (SpeechTolerance == 0f && speakHoldMillis == 0) {
                  SpeechTolerance = 0f;
                  IsSpeechDetected = false;
                  // SpeechlyClient.StopContext();
                }
              }

              // Learn background noise level
              if (!IsSpeechDetected) {
                BaselineEnergy = (BaselineEnergy * 0.95f) + (Energy * 0.05f);
              } else {
                BaselineEnergy = (BaselineEnergy * 0.99f) + (Energy * 0.01f);
              }

              vadSum = 0f;
              speakHoldMillis = Math.Max(speakHoldMillis - EnergyAnalysisWindowMillis, 0);
            }
            capturedSamplesLeft -= summedSamples;
          }
        }

        if (SpeechlyClient.IsListening) {
          audioSent = true;
          task = SpeechlyClient.SendAudio(waveData, 0, samples);
          yield return new WaitUntil(() => task.IsCompleted);
        }
      }
      
      if (!audioSent) {
        yield return null;
      }

    }

  }
}
