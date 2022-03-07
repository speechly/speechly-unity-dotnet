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
  private bool IsButtonHeld = false;

  private string lastWord = "";

  async Task StartSpeechly()
  {
    Logger.Log = Debug.Log;

    client = new SpeechlyClient(
        appId: this.AppId
    );

    // Note: Callbacks can't access UI directly as they are called from async methods

    client.OnTentativeTranscript = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
      msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms "));
      Logger.Log(sb.ToString());
    };
    client.OnTentativeEntity = (msg) =>
    {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
      msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.entity}': '{w.value}' @ {w.startPosition}..{w.endPosition} "));
      Logger.Log(sb.ToString());
    };
    client.OnIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

    client.OnTranscript = (msg) =>
    {
      lock(lastWord) {
        lastWord = msg.data.word;
      }
      Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    };
    client.OnEntity = (msg) => Logger.Log($"Final entity '{msg.data.entity}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
    client.OnTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");

    await client.Connect();

    // Send test audio:
    // await client.StartContext();
    // await client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    // await client.StopContext();

  }

  async void Start()
  {
    await StartSpeechly();
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

  public async void OnMouseDown()
  {
    if (!IsButtonHeld && !client.IsListening) {
      Debug.Log("Mouse Down");
      IsButtonHeld = true;
      await client.StartContext();
    }
  }

  public async void OnMouseUp()
  {
    if (IsButtonHeld && client.IsListening) {
      Debug.Log("Mouse Up");
      IsButtonHeld = false;
      await client.StopContext();
    }
  }

  async void Update()
  {
    lock(lastWord) {
      TranscriptText.text = lastWord;
    }

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
        // TODO: Process looped samples as well
        if (!looped)
        {
          // Note: Always captures full buffer length, MicSampleRate * MicBufferLengthSecs samples
          clip.GetData(waveData, oldCaptureRingbufferPos);
          if (IsButtonHeld && client.IsListening)
          {
            await client.SendAudio(waveData, 0, samples);
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
