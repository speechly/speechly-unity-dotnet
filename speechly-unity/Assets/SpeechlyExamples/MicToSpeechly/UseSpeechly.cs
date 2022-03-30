using UnityEngine;
using Speechly.SLUClient;
using UnityEngine.UI;
using TMPro;

public class UseSpeechly : MonoBehaviour
{
  public Slider SliderAudioPeak;
  public TMP_Text TranscriptText;
  private SpeechlyClient speechlyClient;
  private bool IsButtonHeld = false;

  void Start()
  {
    speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
    speechlyClient.OnSegmentChange += (segment) =>
    {
      Debug.Log(segment.ToString());
      TranscriptText.text = segment.ToString(
        (intent) => "",
        (words, entityType) => $"<color=#15e8b5>{words}<color=#ffffff>",
        ""
      );
    };
  }

  void Update()
  {
    SliderAudioPeak.value = MicToSpeechly.Instance.SpeechlyClient.Vad.Energy;
  }

  public async void OnMouseDown()
  {
    if (!IsButtonHeld && !speechlyClient.IsListening)
    {
      Debug.Log("Mouse Down");
      IsButtonHeld = true;
      await speechlyClient.StartContext();
    }
  }

  public async void OnMouseUp()
  {
      Debug.Log("Mouse Up 1");
    if (IsButtonHeld && speechlyClient.IsListening)
    {
      Debug.Log("Mouse Up");
      IsButtonHeld = false;
      await speechlyClient.StopContext();
    }
  }

}
