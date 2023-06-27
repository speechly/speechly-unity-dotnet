using UnityEngine;
using Speechly.SLUClient;
using Speechly.Types;
using UnityEngine.UI;
using TMPro;

namespace Speechly.Example.NoiseGateTriggerVR
{
  public class UseSpeechly : MonoBehaviour
  {
    public Slider SliderEnergy;
    public Slider SliderBaselineEnergy;
    public Image Fill;
    public TMP_Text ButtonText;
    public TMP_Text TranscriptText;
    private SpeechlyClient speechlyClient;
    private SegmentChangeDelegate SegmentUpdate;

    void Start()
    {
      speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
      SegmentUpdate = (segment) =>
      {
        Debug.Log(segment.ToString());
        TranscriptText.text = segment.ToString(
          (intent) => "",
          (words, entityType) => $"<color=#15e8b5>{words}<color=#ffffff>",
          "."
        );
      };
      speechlyClient.OnSegmentChange += SegmentUpdate;
    }

    void OnEnable()
    {
      MicToSpeechly.Instance?.gameObject.SetActive(true);
    }

    void OnDisable()
    {
      MicToSpeechly.Instance?.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
      speechlyClient.OnSegmentChange -= SegmentUpdate;
    }

    void Update()
    {
      speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
      SliderBaselineEnergy.value = speechlyClient.Output.NoiseLevelDb;
      SliderEnergy.value = speechlyClient.Output.NoiseLevelDb + speechlyClient.Output.SignalDb;
      Fill.color = speechlyClient.Output.IsSignalDetected ? Color.red : Color.white;
      ButtonText.text = speechlyClient.Output.IsSignalDetected ? "Signal Detected" : "Signal Status";
    }
  }
}
