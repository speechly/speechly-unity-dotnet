using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

public class MicToSpeechly : MonoBehaviour
{
  [Tooltip("Capture device name or null for default")]
  public string CaptureDeviceName = null;
  public int MicSampleRate = 16000;
  public int MicBufferLengthSecs = 1;


  public TMP_Text TextAudioStatus;
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
  private SpeechlyClient client;

  async void Awake()
  {
    Logger.Log = Debug.Log;
    client = new SpeechlyClient(
        loginUrl: "https://staging.speechly.com/login",
        apiUrl: "wss://staging.speechly.com/ws/v1?sampleRate=16000",
        appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e" // Chinese
    );

    client.onTentativeTranscript = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
      msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms "));
      Logger.Log(sb.ToString());
    };
    client.onTranscript = (msg) =>
    {
      TranscriptText.text = msg.data.word;
      Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    };
    client.onTentativeEntity = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
      msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.entity}': '{w.value}' @ {w.startPosition}..{w.endPosition} "));
      Logger.Log(sb.ToString());
    };
    client.onEntity = (msg) => Logger.Log($"Final entity '{msg.data.entity}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
    client.onTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");
    client.onIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

    await client.connect();
    // await client.startContext();
    // await client.sendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    // await client.stopContext();

  }

  void Start()
  {
    // Start audio capture
    audioSource = GetComponent<AudioSource>();
    clip = Microphone.Start(CaptureDeviceName, true, MicBufferLengthSecs, MicSampleRate);

    if (clip != null)
    {
      audioSource.clip = clip;
      audioSource.loop = true;
      float playbackLatencySecs = 0;
      float playbackLatencySamples = playbackLatencySecs * MicSampleRate;
      waveData = new float[audioSource.clip.samples * audioSource.clip.channels];
      while (Microphone.GetPosition(CaptureDeviceName) <= playbackLatencySamples)
      {
        // wait for input to set latency
      }
      audioSource.Play();
      audioRunning = true;
      Debug.Log(string.Format("Audio running"));
      TextAudioStatus.text = string.Format("Running...");

    }
    else
    {
      Debug.LogError(string.Format("Could not open microphone {0}", CaptureDeviceName));
    }
  }

  public async void onMouseDown()
  {
    Debug.Log("Down");
    await client.startContext();
  }

  public async void onMouseUp()
  {
    Debug.Log("Up");
    await client.stopContext();
  }

  // Update is called once per frame
  async void Update()
  {
    if (audioRunning)
    {
      peak = peak * 0.95f;

      int captureRingbufferPos = Microphone.GetPosition(CaptureDeviceName);
      bool looped = false;
      if (captureRingbufferPos < oldCaptureRingbufferPos)
      {
        // Debug.Log(string.Format("Buffer loop"));
        looped = true;
      }
      int samples = captureRingbufferPos - oldCaptureRingbufferPos;
      if (samples != 0)
      {
        // Debug.Log(string.Format("Samples {0}", samples));
        // TODO: Process looped samples as well
        if (!looped)
        {
          // Note: Always captures full clip length (44100 samples)
          clip.GetData(waveData, oldCaptureRingbufferPos);
          if (client.isListening)
          {
            // await client.sendAudio(waveData, 0, samples);
          }
          int s = 0;
          while (s < samples)
          {
            peak = Mathf.Max(peak, waveData[s]);
            s++;
          }
          SliderAudioPeak.value = peak;
        }
        oldCaptureRingbufferPos = captureRingbufferPos;
      }
    }
  }
}
