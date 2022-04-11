﻿using System.Collections;
using UnityEngine;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Speechly.SLUClient {

public partial class MicToSpeechly : MonoBehaviour
{
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
  private bool wasVADEnabled = false;
  private Coroutine runSpeechlyCoroutine = null;
  IDecoder decoder = null;
  partial void CreateOnDeviceDecoder(bool debug);


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
      vad: this.Vad,
      manualUpdate: true,
      frameMillis: FrameMillis,
      historyFrames: HistoryFrames,
      inputSampleRate: MicSampleRate,
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
      CreateOnDeviceDecoder(debug: DebugPrint);
      if (this.decoder == null) {
        throw new Exception("Speechly on-device spoken language understanding (SLU) is not available. Most likely your Unity project does not contain the SpeechlyOnDevice folder. Please contact Speechly to enable on-device support - you'll need extra files delivered under Speechly on-device licence.");
      }
    }

    if (SpeechlyEnv == SpeechlyEnvironment.Production || SpeechlyEnv == SpeechlyEnvironment.Staging) {
      decoder = new CloudDecoder(
        loginUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "https://staging.speechly.com/login",
        apiUrl: SpeechlyEnv == SpeechlyEnvironment.Production ? null : "wss://staging.speechly.com/ws/v1?sampleRate=16000",
        appId: this.AppId,
        deviceId: Platform.GetDeviceId(SystemInfo.deviceUniqueIdentifier),
        debug: DebugPrint
      );
    }

    // Wait for connect
    Task task;
    task = SpeechlyClient.Initialize(decoder);
    yield return new WaitUntil(() => task.IsCompleted);

    while (true) {
      // Fire handlers in main Unity thread
      SpeechlyClient.Update();

      // Ensure VAD-initiated listening is stopped if VAD state is altered "on the fly"
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
        task = SpeechlyClient.ProcessAudio(waveData, 0, samples);
        yield return new WaitUntil(() => task.IsCompleted);
      } else {
        // Wait for a frame for new audio
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

}