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
    public Slider SliderEnergy;
    public Slider SliderBaselineEnergy;
    public Image Fill;
    public TMP_Text ButtonText;
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
          ""
        );
      };
    }

    void Update()
    {
      speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
      SliderEnergy.value = speechlyClient.Vad.Energy;
      SliderBaselineEnergy.value = speechlyClient.Vad.BaselineEnergy;
      Fill.color = speechlyClient.Vad.IsSignalDetected ? Color.red : Color.white;
      ButtonText.text = speechlyClient.Vad.IsSignalDetected ? "Signal Detected" : "No Signal";
    }

  }
}
