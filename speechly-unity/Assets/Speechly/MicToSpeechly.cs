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
  public float Peak {get; private set; } = 0f;
  public SpeechlyClient SpeechlyClient { get; private set; }
  private AudioClip clip;
  private int oldCaptureRingbufferPos;
  private float[] waveData;

  private void Awake() 
  { 
    if (_instance != null && _instance != this) 
    { 
      Destroy(this.gameObject);
      return;
    }

    Logger.Log = Debug.Log;

    SpeechlyClient = new SpeechlyClient(
      appId: this.AppId,
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
