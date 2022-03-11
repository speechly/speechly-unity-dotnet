using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using System.Text;
using System.Threading.Tasks;
using System.Linq;

using Speechly.SLUClient;
using Logger = Speechly.SLUClient.Logger;

public class MicToSpeechly : MonoBehaviour
{
  [Tooltip("Speechly App Id")]
  public string AppId = "ef84e8ba-c5a7-46c2-856e-8b853e2c77b1"; // Speechly Client Demos / speech-to-text only configuration
  [Tooltip("Capture device name or null for default")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  public int MicBufferLengthSecs = 1;


  public Button PushToTalkButton;
  public Slider SliderAudioPeak;

  public TMP_Text TranscriptText;

  private AudioSource audioSource;
  private AudioClip clip;
  private Coroutine audioConsumerCoroutine;
  private bool audioRunning;
  private int oldCaptureRingbufferPos;
  private float[] waveData;
  private float peak = 0f;
  private SpeechlyClient speechlyClient;
  private bool IsButtonHeld = false;

  void Start()
  {
    // Start audio capture
    int minFreq, maxFreq;
    Microphone.GetDeviceCaps(CaptureDeviceName, out minFreq, out maxFreq);
    Logger.Log($"minFreq {minFreq} maxFreq {maxFreq}");
    clip = Microphone.Start(CaptureDeviceName, true, MicBufferLengthSecs, MicSampleRate);
    
    if (clip != null)
    {
      waveData = new float[clip.samples * clip.channels];
      Logger.Log($"Audiosource frequency {clip.frequency} channels {clip.channels}");
      audioRunning = true;
      Debug.Log(string.Format("Audio running"));
    }
    else
    {
      Debug.LogError($"Could not open microphone {CaptureDeviceName}");
    }

    StartCoroutine(StartSpeechly());
  }

  private IEnumerator StartSpeechly()
  {
    Logger.Log = Debug.Log;

    speechlyClient = new SpeechlyClient(
      appId: this.AppId,
      manualUpdate: true,
      debug: true
    );

    speechlyClient.OnSegmentChange = (segment) => Logger.Log(segment.ToString());
    speechlyClient.OnStateChange = (clientState) => Logger.Log($"ClientState: {clientState}");

    speechlyClient.OnTentativeTranscript = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
      msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms "));
      Logger.Log(sb.ToString());
    };
    speechlyClient.OnTentativeEntity = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
      msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.type}': '{w.value}' @ {w.startPosition}..{w.endPosition} "));
      Logger.Log(sb.ToString());
    };
    speechlyClient.OnIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

    speechlyClient.OnTranscript = (msg) =>
    {
      TranscriptText.text = msg.data.word;
      Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    };
    speechlyClient.OnEntity = (msg) => Logger.Log($"Final entity '{msg.data.type}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
    speechlyClient.OnTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");

    Task task;
    task = speechlyClient.Connect();
    yield return new WaitUntil(() => task.IsCompleted);
    /*
    task = client.StartContext();
    yield return new WaitUntil(() => task.IsCompleted);
    task = client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    yield return new WaitUntil(() => task.IsCompleted);
    task = client.StopContext();
    yield return new WaitUntil(() => task.IsCompleted);
    */


    while (true) {
      // Fire handlers in main Unity thread
      speechlyClient.Update();

      if (audioRunning)
      {
        peak = peak * 0.95f;

        int captureRingbufferPos = Microphone.GetPosition(CaptureDeviceName);

        int samples;
        if (captureRingbufferPos < oldCaptureRingbufferPos)
        {
          samples = (waveData.Length - oldCaptureRingbufferPos) + captureRingbufferPos;
        } else {
          samples = captureRingbufferPos - oldCaptureRingbufferPos;
        }

        if (samples > 0)
        {
          // Always captures full buffer length (MicSampleRate * MicBufferLengthSecs samples), starting from offset
          clip.GetData(waveData, oldCaptureRingbufferPos);
          if (IsButtonHeld && speechlyClient.IsListening)
          {
            SendAudio(waveData, 0, samples);
          }
          int s = 0;
          while (s < samples)
          {
            peak = Mathf.Max(peak, waveData[s]);
            s++;
          }
          SliderAudioPeak.value = peak;
          oldCaptureRingbufferPos = captureRingbufferPos;
        }
      }
      yield return null;
    }

  }

  public async void OnMouseDown()
  {
    if (!IsButtonHeld && !speechlyClient.IsListening) {
      Debug.Log("Mouse Down");
      IsButtonHeld = true;
      await speechlyClient.StartContext();
    }
  }

  public async void OnMouseUp()
  {
    if (IsButtonHeld && speechlyClient.IsListening) {
      Debug.Log("Mouse Up");
      IsButtonHeld = false;
      await speechlyClient.StopContext();
    }
  }

  // Wrap async Task SendAudio as async void
  // To enable using it in fire-and-forget manner. Awaiting introduces distortion, which needs to be investigated.
  async void SendAudio(float[] wavedata, int start, int end) {
    await speechlyClient.SendAudio(wavedata, start, end);
  }

  void Update()
  {
  }
}
