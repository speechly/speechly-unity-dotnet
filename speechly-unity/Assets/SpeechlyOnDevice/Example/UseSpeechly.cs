using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Speechly.SLUClient;
using Speechly.Types;
using Speechly.Tools;
using Logger = Speechly.Tools.Logger;

namespace Speechly.Example.OfflineDecoder
{

  public class UseSpeechly : MonoBehaviour
  {
    [Tooltip("Model Bundle filename")]
    public string ModelBundle;
    public Slider SliderAudioPeak;
    public TMP_Text TranscriptText;
    public AudioInfo audioInfo;
    private bool IsButtonHeld = false;

    private OnDeviceDecoder decoder = null;
    private Coroutine runSpeechlyCoroutine = null;
    private ConcurrentQueue<SegmentMessage> messageQueue = new ConcurrentQueue<SegmentMessage>();

    void Awake()
    {
      Logger.Log = Debug.Log;
      Logger.LogError = Debug.LogError;
    }

    void OnEnable() {
      Logger.Log("OnEnable");
      runSpeechlyCoroutine = StartCoroutine(RunSpeechly());
    }

    private IEnumerator RunSpeechly()
    {
      decoder = UnityOnDeviceDecoderFactory.CreateDecoder(Platform.GetDeviceId(), ModelBundle);
      
      decoder.OnMessage += (MsgCommon msgCommon, string msgString) => {
        messageQueue.Enqueue(new SegmentMessage(msgCommon, msgString));
      };

      Debug.Log("Initializing...");
      var initTask = decoder.Initialize(
        new AudioProcessorOptions(),
        new ContextOptions(),
        audioInfo
      );
      yield return new WaitUntil(() => initTask.IsCompleted);

      Logger.Log($"================== Stream some bytes =================");
      string audioFile = "ExampleAudio/00_chinese_restaurant_float32.raw";
#if UNITY_ANDROID
      var fetchAudioTask = Platform.Fetch($"{Application.streamingAssetsPath}/{audioFile}");
      yield return new WaitUntil(() => fetchAudioTask.IsCompleted);
      var audio = fetchAudioTask.Result;
      Logger.Log($"Loaded audio. First byte: {audio[0]}");

      // create a float array and copy the bytes into it
      var floatArray = new float[audio.Length / sizeof(float)];
      Buffer.BlockCopy(audio, 0, floatArray, 0, audio.Length);
      decoder.SendAudio(floatArray, 0, floatArray.Length, true);
#else
      SendAudioFile($"Assets/StreamingAssets/{audioFile}", false);
#endif

      while (true) {
        SegmentMessage m;
        while (messageQueue.TryDequeue(out m)) {
          switch (m.msgCommon.type) {
            case "transcript": {
              var msg = JSON.Parse(m.msgString, new MsgTranscript());
              Debug.Log(msg.data.word);
              TranscriptText.text = msg.data.word;
              break;
            }
            default:
              Debug.Log(m.msgString);
              break;
          }
        }

        yield return null;
      }
    }

    async void OnDisable() {
      Logger.Log("OnDisable...");
      if (runSpeechlyCoroutine != null) StopCoroutine(runSpeechlyCoroutine);
      runSpeechlyCoroutine = null;
      if (decoder != null) await decoder.Shutdown();
      decoder = null;
    }

    public void SendAudioFile(string fileName, bool sendEnd = true) {
      var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);

      var b = new float[16*30];

      using (var reader = new BinaryReader(fileStream)) {
        int i = 0;
        try {
          while (true) {
            i = 0;
            while (i < b.Length) {
              b[i] = reader.ReadSingle();
              i++;
            }
            // Debug.Log($"Read {i}, first value {b[i-1]}");
            decoder.SendAudio(b, 0, i);
          }
        } catch {
        } finally {
          // Debug.Log($"Read final {i} samples");
          decoder.SendAudio(b, 0, i, sendEnd);
        }
      }

      fileStream.Close();
    }

    public void OnMouseDown()
    {
      if (!IsButtonHeld)
      {
        Debug.Log("Mouse Down");
        IsButtonHeld = true;
      }
    }

    public void OnMouseUp()
    {
      if (IsButtonHeld)
      {
        Debug.Log("Mouse Up");
        IsButtonHeld = false;
      }
    }

  }
}
