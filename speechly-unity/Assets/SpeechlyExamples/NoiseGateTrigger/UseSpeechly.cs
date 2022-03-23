﻿using System.Collections;
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
      SliderAudioPeak.value = MicToSpeechly.Instance.Peak;
      SliderEnergy.value = MicToSpeechly.Instance.Energy;
      SliderBaselineEnergy.value = MicToSpeechly.Instance.BaselineEnergy;
      Fill.color = MicToSpeechly.Instance.IsSignalDetected ? Color.red : Color.white;
      ButtonText.text = MicToSpeechly.Instance.IsSignalDetected ? "Signal Detected" : "No Signal";
    }

  }
}
