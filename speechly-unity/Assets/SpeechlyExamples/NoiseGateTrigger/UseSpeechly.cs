using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Speechly.SLUClient;
using UnityEngine.UI;
using TMPro;

namespace Speechly.Example.NoiseGateTrigger
{
  public class UseSpeechly : MonoBehaviour
  {
    public Slider SliderAudioPeak;
    public Slider SliderEnergy;
    public Slider SliderBaselineEnergy;
    public Image Fill;
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
      SliderAudioPeak.value = MicToSpeechly.Instance.Peak;
      SliderEnergy.value = MicToSpeechly.Instance.Energy;
      SliderBaselineEnergy.value = MicToSpeechly.Instance.BaselineEnergy;
      Fill.color = MicToSpeechly.Instance.IsSpeechDetected ? Color.red : Color.white;
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
      if (IsButtonHeld && speechlyClient.IsListening)
      {
        Debug.Log("Mouse Up");
        IsButtonHeld = false;
        await speechlyClient.StopContext();
      }
    }

  }
}
