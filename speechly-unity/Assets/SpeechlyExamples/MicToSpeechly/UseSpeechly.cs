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

  public void OnMouseDown()
  {
    Debug.Log("Mouse Down");
    if (!IsButtonHeld && !speechlyClient.IsActive)
    {
      IsButtonHeld = true;
      _ = speechlyClient.StartContext();
    }
  }

  public void OnMouseUp()
  {
    Debug.Log("Mouse Up");
    if (IsButtonHeld && speechlyClient.IsActive)
    {
      IsButtonHeld = false;
      _ = speechlyClient.StopContext();
    }
  }

}
