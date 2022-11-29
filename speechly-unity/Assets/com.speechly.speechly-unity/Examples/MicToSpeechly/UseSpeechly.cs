using UnityEngine;
using Speechly.SLUClient;
using UnityEngine.UI;
using TMPro;

public class UseSpeechly : MonoBehaviour
{
  public Slider SliderAudioPeak;
  public TMP_Text TranscriptText;
  private SpeechlyClient speechlyClient;

  void Start()
  {
    speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
    speechlyClient.OnSegmentChange += (segment) =>
    {
      Debug.Log(segment.ToString());
      TranscriptText.text = segment.ToString(
        (intent) => "",
        (words, entityType) => $"<color=#15e8b5>{words}<color=#ffffff>",
        "."
      );
    };
  }

  void Update()
  {
    SliderAudioPeak.value = MicToSpeechly.Instance.SpeechlyClient.Output.SignalDb;
  }

  public void OnMouseDown()
  {
    if (!speechlyClient.IsActive)
    {
      _ = speechlyClient.Start();
    }
  }

  public void OnMouseUp()
  {
    if (speechlyClient.IsActive)
    {
      _ = speechlyClient.Stop();
    }
  }

}
